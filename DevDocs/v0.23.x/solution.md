# 技术方案 — v0.23.x 玩家 API 配置落盘加密

> **状态**：待确认
> **依据 PRD**：`PRD.md`
> **参考方案**：`DevDocs/feature-design/打包方案.md`（§4.2、§8）
> **加密方案来源**：Cursor 本机实现（Electron `safeStorage` / Chromium 标准，已实测确认）
> **最后更新**：2026-09-02

---

## 1. 方案概述

把 `api_config.json` 的值从明文改为 **DPAPI 加密**（Windows 系统级、机器绑定）。

**核心思路**（复用 Cursor/Chromium 的 safeStorage 模式，但做**单层简化**）：

- **只做一层 DPAPI**：Unity 侧把 `ApiConfig` 序列化为 JSON 文本 → `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` → base64 落盘；Python 侧读文件 → base64 解码 → `CryptUnprotectData` 解密 → 解析 JSON。
- **不叠加 AES-256-GCM**（Cursor 是「DPAPI 保护 AES 主密钥 + AES-GCM 加密数据」两层，因为 Chromium 要跨平台复用主密钥）。本项目当前只做 Windows 单机，DPAPI 直接加密数据已足够、更简单；跨平台留到 macOS 时再引入「Keychain 存主密钥 + AES-GCM」的第二层（抽象已预留，见 §3.4）。

> 决策依据（对齐 PRD §7）：`ProtectedData`/`CryptUnprotectData` 本身就是 AES 加密（内部实现），且机器绑定。单层 DPAPI 的加解密目标已完全满足「落盘不可读明文」+「机器绑定」。叠加 AES 只增加复杂度，无单机场景收益。

---

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | `Src/PythonServer/config/api_config_loader.py` | 改：读取时 DPAPI 解密 |
| Python | `Src/PythonServer/config/dpapi_util.py`（新增） | 新增：DPAPI 加解密工具（`ctypes`，不新增依赖） |
| Python | `Src/PythonServer/lifecycle/lifecycle.py` | 无改动（复用 `load_api_config_into_env`） |
| Unity | `Assets/Scripts/IndependentAgentProject/Services/ApiConfigStore.cs` | 改：Save/Load 加解密 |
| Unity | `Assets/Scripts/IndependentAgentProject/Services/DpapiUtil.cs`（新增，或并入 ApiConfigStore） | 新增：DPAPI 加解密封装 |
| Unity | `Assets/Scripts/IndependentAgentProject/Model/ApiConfigModel.cs` | 无改动（`OnInit` 调 `ApiConfigStore.Load()`，内部已解密） |
| Unity | `Assets/Scripts/IndependentAgentProject/Command/SaveApiConfigCommand.cs` | 无改动（调 `ApiConfigStore.Save()`，内部已加密） |
| 协议 | `Tools/message.proto` | 无 |
| 打包 | `Tools/build_python_exe.cmd` | 无（纯 Python 标准库/ctypes，无新增依赖收集） |

---

## 3. 详细设计

### 3.1 落盘格式（密文）

`api_config.json` 内容从「12 键明文 JSON」改为「**单一字符串密文**」：

```json
"v1|DPAPI|<base64 密文>"
```

- **`v1|DPAPI|` 前缀**：标识加密版本与算法（用于：① 识别是否加密文件；② 未来 macOS 叠加 AES 时按 `v2|AES|` 分支）。
- 密文是**整个 `ApiConfig` JSON 文本**（`JsonUtility.ToJson(config)`）经 DPAPI 加密后再 base64。
- 字段名（12 个 SCREAMING_SNAKE_CASE 键）**不再明文可见**（因为整个 JSON 都加密了）。Python 侧 `API_CONFIG_KEYS` 不变，解密后仍能按原键读。

> 决策：**整个 JSON 一起加密**（而非逐字段加密）。理由：① 字段少、无部分加密需求；② 整体加密后字段名也不可见，隐私更好；③ 实现最简单。

### 3.2 Unity（C#）侧：`ApiConfigStore` 改造

新增 `DpapiUtil`（或并入 `ApiConfigStore`），用 **`System.Security.Cryptography.ProtectedData`**：

```csharp
using System.Security.Cryptography;

public static class DpapiUtil
{
    private const string Prefix = "v1|DPAPI|";

    public static string Protect(string plain)
    {
        byte[] plainBytes = Encoding.UTF8.GetBytes(plain);
        byte[] enc = ProtectedData.Protect(plainBytes, null,
            DataProtectionScope.CurrentUser);   // 机器+用户绑定
        return Prefix + Convert.ToBase64String(enc);
    }

    public static string Unprotect(string payload)
    {
        if (string.IsNullOrEmpty(payload) || !payload.StartsWith(Prefix))
            return null;                        // 非加密格式（旧明文 / 空）
        byte[] enc = Convert.FromBase64String(payload.Substring(Prefix.Length));
        byte[] plain = ProtectedData.Unprotect(enc, null,
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
```

- **依赖**：`System.Security.Cryptography.ProtectedData` 命名空间在 .NET Standard 2.1 中**不是默认引用程序集**，需添加 `System.Security.Cryptography.ProtectedData`（NuGet 包）并放到 Unity `Assets/Plugins/`（或直接 `DllImport` crypt32 用 `CryptProtectData`，避免额外 DLL——**实现时二选一，倾向 DllImport 零依赖**，见 §6 自测项）。
- `ApiConfigStore.Save()`：
  ```csharp
  string json = JsonUtility.ToJson(config, prettyPrint: true);
  string cipher = DpapiUtil.Protect(json);
  JsonConfigIO.SaveText("api_config.json", cipher);   // 新增 SaveText 落盘纯文本
  ```
- `ApiConfigStore.Load()`：
  ```csharp
  string payload = JsonConfigIO.LoadText("api_config.json");   // 新增 LoadText
  if (string.IsNullOrEmpty(payload)) return new ApiConfig();
  string json = DpapiUtil.Unprotect(payload);
  if (json == null) { Debug.LogWarning("[ApiConfigStore] 旧明文/非加密格式，视为未配置"); return new ApiConfig(); }
  return JsonUtility.FromJson<ApiConfig>(json) ?? new ApiConfig();
  ```
- `JsonConfigIO` 新增 `SaveText/LoadText`（或复用现有 `LoadJson/SaveJson<T>` 以 string 作为 T——更简单，见实现）。

### 3.3 Python 侧：`api_config_loader` 改造

新增 `config/dpapi_util.py`，用 **`ctypes` 调 Windows `CryptUnprotectData`**（零第三方依赖，避免给 PyInstaller 增加收集负担）：

```python
# config/dpapi_util.py
import base64
import ctypes
import ctypes.wintypes as wt

PREFIX = "v1|DPAPI|"


class DATA_BLOB(ctypes.Structure):
    _fields_ = [("cbData", wt.DWORD), ("pbData", ctypes.POINTER(ctypes.c_char))]


def _blob(data: bytes) -> DATA_BLOB:
    buf = ctypes.create_string_buffer(data, len(data))
    return DATA_BLOB(len(data), ctypes.cast(buf, ctypes.POINTER(ctypes.c_char)))


def _unprotect(data: bytes) -> bytes:
    b_in, b_out = _blob(data), DATA_BLOB()
    if not ctypes.windll.crypt32.CryptUnprotectData(
            ctypes.byref(b_in), None, None, None, None, 0, ctypes.byref(b_out)):
        raise ctypes.WinError()
    try:
        return ctypes.string_at(b_out.pbData, b_out.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(b_out.pbData)


def dpapi_unprotect(payload: str):
    """返回解密明文；非加密格式返回 None（不抛异常）。"""
    if not payload or not payload.startswith(PREFIX):
        return None
    try:
        enc = base64.b64decode(payload[len(PREFIX):])
        return _unprotect(enc).decode("utf-8")
    except Exception:
        return None
```

- `config/api_config_loader.load_api_config_into_env()` 读取处改造：
  ```python
  import json
  from config.dpapi_util import dpapi_unprotect

  text = open(path, "r", encoding="utf-8").read()
  plain = dpapi_unprotect(text)
  if plain is None:
      print("[api_config] 非加密格式或解密失败，跳过注入（回退 .env）")
      return {}
  data = json.loads(plain)
  ```
- 其余（遍历 `API_CONFIG_KEYS`、注入 `os.environ`、`force` 语义）**不变**。

### 3.4 跨平台预留（macOS，本期不实现）

- 前缀带版本号（`v1|DPAPI|`），未来 macOS 引入 **`v2|AES|`**：主密钥存 macOS **Keychain**（替代 DPAPI），AES-256-GCM 加密数据。Unity `DpapiUtil` / Python `dpapi_util` 各加一个分支即可，上层（`ApiConfigStore` / `api_config_loader`）零改动。
- 对齐打包方案 §8 决策项 8「AES + 机器绑定密钥，为 macOS 预留跨平台能力」。

### 3.5 开发态隔离

- Python 开发态：`api_config.json` 不存在/非加密格式 → 回退 `.env`（现有逻辑），开发工作流零破坏。
- Unity 编辑器：`Load()` 读不到加密文件 → 返回空配置 → Title 引导填写（现状逻辑），`Save()` 在编辑器下也写加密格式（统一，避免编辑器/打包格式分叉）。

---

## 4. 实现步骤

1. Python：新增 `config/dpapi_util.py`（`ctypes` 版 `CryptUnprotectData`）。
2. Python：改 `config/api_config_loader.py` 读取处（解密 → 解析 → 注入）。
3. Unity：新增 `DpapiUtil`（DllImport `CryptProtectData/CryptUnprotectData` 或 `ProtectedData` NuGet，实现时实测二选一）。
4. Unity：改 `ApiConfigStore.Save/Load`（整体 JSON 加密/解密 + 前缀）。
5. Unity：`JsonConfigIO` 补 `SaveText/LoadText`（或复用泛型）。
6. 自测（§6）+ 联调验收（§6.2）。

---

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| Unity 侧 `ProtectedData` 依赖不可用（.NET Standard 2.1 非默认程序集） | 改用 `DllImport` `CryptProtectData/CryptUnprotectData`（零依赖），或 NuGet 包放 Plugins；实现时实测二选一 |
| Python 侧 `ctypes.windll` 在非 Windows 报错 | 仅 Windows 调用；macOS 分支本期不实现，`sys.platform == "win32"` 守卫 |
| 旧明文 `api_config.json` 无法解密 | `Unprotect` 返回 `None` → 视为未配置，引导重配；不崩溃、不泄露 |
| 解密失败（文件损坏/跨机器） | 回退 `.env`（开发态）或提示重配（打包态），打日志 |
| PyInstaller 打包后 ctypes 调用失败 | `ctypes`/`crypt32` 属系统 DLL，`--collect-all` 无需特殊处理；自测 P6 覆盖打包态 |
| 跨机器不可用（DPAPI 机器绑定） | 单机游戏场景可接受（PRD §3）；文档标注为已知取舍 |

---

## 6. 测试建议

### 6.1 开发者自测（不依赖 Unity 即可测，交付前必过）

| # | 自测项 | 步骤 | 预期 |
|---|--------|------|------|
| P1 | `dpapi_util` 加解密往返 | Python 加密→解密 | 明文一致 |
| P2 | `dpapi_util` 非加密格式返回 None | 喂普通 JSON 文本 | 返回 None 不抛异常 |
| P3 | `load_api_config_into_env` 走加密文件 | 写加密 `api_config.json` → 调用 | 12 键注入 `os.environ` |
| P4 | 回退 `.env` | 无文件 / 旧明文 / 损坏密文 | 注入为空或回退 env，不崩溃 |
| P5 | 跨机器不可解 | 用本机密文在另一机器/另一用户 | 解密失败（回退，不崩溃） |
| P6 | 打包态 exe | `build_python_exe.cmd` 后 exe 启动 + 读加密文件 | 正常解密，推理可用 |

### 6.2 验收（需 Unity + Python 联调）

- [ ] Title 保存配置 → `api_config.json` 为 `v1|DPAPI|...` 密文，无明文 Key。
- [ ] 保存后进游戏 → Python 正常解密 → Agent 推理 / 记忆检索正常。
- [ ] 直接查看文件无法还原 Key；拷到另一机器/用户无法解密。
- [ ] 旧明文文件 → Title 引导重新配置，不崩溃。
- [ ] 编辑器 `.env` 工作流不受影响。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-09-02 | 创建 PRD / solution（待确认）。加密方案：单层 DPAPI（Windows 机器绑定），复用 Cursor/Chromium safeStorage 思路，为 macOS 预留 `v2|AES|` 分支。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
