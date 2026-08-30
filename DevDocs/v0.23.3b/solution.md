# 技术方案 — v0.23.3b Python 端 exe 化 + Unity 托管进程

> **状态**：已实现（2026-08-29 验收通过）
> **依据 PRD**：`PRD.md`
> **引用调研**：`DevDocs/v0.23.3b/验证报告.md`
> **验收复盘**：`DevDocs/v0.23.3b/验收报告.md`
> **最后更新**：2026-08-30

---

## 1. 方案概述

用 **PyInstaller（onedir + `--noconsole`）** 把 Python 端 `main.py` + 全部依赖 + 业务代码打成自包含 `agent_server.exe`（已验证可行：108MB、冷启动到监听 2.6s、无窗口、日志落盘）。**Unity 在 `BootstrapEntry` 自动拉起该 exe（无窗口）**，复用已有 `EnsureConnectedAsync`（等端口就绪，30s 超时），退出时清理子进程。

核心架构改动：
- **Python 侧**：新增统一「运行根目录」解析层（`path_config.py`），所有模块的路径依赖收敛到一处，同时兼容开发态（venv/python.exe）与打包态（exe）。
- **Unity 侧**：新增 `PythonProcessLauncher`（拉起/清理进程），`#if !UNITY_EDITOR` 隔离，开发期不受影响。
- **打包侧**：新增 `Tools/build_python_exe.cmd` 一键打包 + 复制外部资源。

---

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | `Src/PythonServer/path_config.py` | 新增：统一运行根目录解析 |
| Python | `main.py`、`config/api_config_loader.py`、`agent_framwork/agents/agent_interuptible.py`、`tools/console_logger.py` | 改：路径依赖改走 `path_config` |
| Unity | `BootstrapEntry.cs`（或新增 `PythonProcessLauncher.cs`） | 新增：拉起/清理 Python 进程 |
| Unity | `AgentService.cs` | 无（`EnsureConnectedAsync` 已存在） |
| Tools | `Tools/build_python_exe.cmd` | 新增：PyInstaller 打包脚本 |
| 协议 | `Tools/message.proto` | 无 |
| 配置 | `.gitignore` | 已加 `build/`、`dist/`、`*.spec` |

---

## 3. 详细设计

### 3.1 Python 统一运行根目录解析（path_config.py）

**问题**：打包态 `__file__` 指向 exe 内临时目录，`os.path.dirname(__file__)/..` 推导项目根失效（已验证）。

**方案**：新增 `path_config.py`，提供 `get_runtime_root()`，按优先级解析「运行根目录」：

```python
# path_config.py（伪代码，落地时按项目风格实现）
import os, sys

_RUNTIME_ROOT = None

def get_runtime_root() -> str:
    """返回运行根目录：打包态=exe 同级（或 AGENT_SERVER_ROOT 指定），开发态=项目根。"""
    global _RUNTIME_ROOT
    if _RUNTIME_ROOT is not None:
        return _RUNTIME_ROOT
    # 1. 显式环境变量（最高优先，便于外部指定数据目录）
    env = os.environ.get("AGENT_SERVER_ROOT")
    if env:
        _RUNTIME_ROOT = os.path.abspath(env)
        return _RUNTIME_ROOT
    # 2. 打包态（PyInstaller）：exe 同级目录
    if getattr(sys, "frozen", False):
        _RUNTIME_ROOT = os.path.abspath(os.path.dirname(sys.executable))
        return _RUNTIME_ROOT
    # 3. 开发态：由本文件推导项目根（Src/PythonServer）
    _RUNTIME_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    return _RUNTIME_ROOT

def get_port_config_file() -> str:
    return os.path.join(get_runtime_root(), "Data", "Config", "agent_server_port.txt")

def get_data_dir() -> str:   # db/ 等运行时写目录
    return os.path.join(get_runtime_root(), "db")

def get_log_dir() -> str:    # logs/
    return os.path.join(get_runtime_root(), "logs")

def ensure_runtime_writable() -> bool:
    """打包态启动自检：db/ 目录可写（试写临时文件）。
    不可写（如解压到 Program Files）返回 False，供 Unity 提示玩家改目录。"""
    if not getattr(sys, "frozen", False):
        return True  # 开发态跳过
    data_dir = get_data_dir()
    os.makedirs(data_dir, exist_ok=True)
    probe = os.path.join(data_dir, ".write_probe")
    try:
        with open(probe, "w") as f:
            f.write("ok")
        os.remove(probe)
        return True
    except OSError:
        return False
```

**改各模块**：

| 模块 | 现状 | 改为 |
|------|------|------|
| `main.py:28-32` | `os.path.dirname(__file__)/..` 推 `PROJECT_ROOT` | `from path_config import get_runtime_root, get_port_config_file`；`PROJECT_ROOT = get_runtime_root()`；`PORT_CONFIG_FILE = get_port_config_file()`；`sys.path.append(PROJECT_ROOT/Lib/proto)` |
| `main.py:363` | `os.path.dirname(__file__)/db/default_skills/exports` | `os.path.join(get_runtime_root(), "db", "default_skills", "exports")` |
| `main.py:395` | `os.path.dirname(__file__)` 传 console_logger | `get_runtime_root()` |
| `config/api_config_loader.py:21` | `__file__/..` 推 `_PYTHON_SERVER_DIR` | `from path_config import get_runtime_root`；`api_config_path()` 用 `get_runtime_root()/Data/Config/api_config.json` |
| `agent_interuptible.py:49-54` | `__file__/../..` 推项目根 | 用 `get_runtime_root()`（或通过环境变量/单例） |

**开发态零破坏**：`get_runtime_root()` 开发态返回 `Src/PythonServer`（与现状 `PROJECT_ROOT` 一致），所有路径行为不变。

**打包态布局**（exe 同级，单一可写游戏根）：

```
<gameRoot>/PythonServer/
  agent_server.exe          # PyInstaller onedir + noconsole
  _internal/                # 依赖（PyInstaller 生成）
  Data/Config/              # agent_server_port.txt, api_config.json（外部资源复制/运行时写）
  Lib/proto/                # 协议生成代码（外部资源复制）
  config/                   # idle_wakeup.json 等
  db/                       # Kuzu 图库、default_skills（运行时读写）
  logs/                     # console 日志（运行时写）
```

> **已定（PRD §7）**：zip 分发 + 数据放游戏根（方案 B，单一运行根）。`Data/Config`、`Lib/proto`、`config/`、`db/`、`logs/` 全部在 exe 同级游戏根，不采用 `%LOCALAPPDATA%` 外置（zip 无卸载程序，数据随目录迁移/删除即清理；且无 installer 强制装 Program Files 场景）。打包态启动时自检 `db/` 可写，不可写则提示「请解压到可写目录（勿放 Program Files）」。`get_runtime_root()` 保持单一根即可，无需区分「只读资源根/可写数据根」。

### 3.2 打包脚本（Tools/build_python_exe.cmd）

```bat
REM ============================================================
REM Build Python exe (v0.23.3b)
REM Output: Build/PythonServer/agent_server.exe (+_internal + 外部资源)
REM ============================================================
set "PS_DIR=%~dp0..\Src\PythonServer"
set "OUT_DIR=%~dp0..\Build\PythonServer"

REM 1. 用 .venv 的 pyinstaller 打包（onedir + noconsole）
"%PS_DIR%\.venv\Scripts\pyinstaller.exe" -y --onedir --noconsole ^
  --name agent_server ^
  --paths "%PS_DIR%" ^
  --collect-all graphiti_core ^
  --distpath "%OUT_DIR%" ^
  --workpath "%PS_DIR%\build" ^
  "%PS_DIR%\main.py"

REM 2. 复制外部资源到 exe 同级
xcopy /E /I /Y "%PS_DIR%\..\Data" "%OUT_DIR%\Data"
xcopy /E /I /Y "%PS_DIR%\..\Lib" "%OUT_DIR%\Lib"
if exist "%PS_DIR%\config" xcopy /E /I /Y "%PS_DIR%\config" "%OUT_DIR%\config"
if exist "%PS_DIR%\db\default_skills" xcopy /E /I /Y "%PS_DIR%\db\default_skills" "%OUT_DIR%\db\default_skills"

REM 3. 校验
"%OUT_DIR%\agent_server.exe" --version   REM 或启动冒烟
echo [build_python_exe] done
```

> 注意：`Data/`、`Lib/` 在仓库根 `Src/` 下（`Src/Data`、`Src/Lib`），不是 `Src/PythonServer` 下。脚本里路径需按实际（`%PS_DIR%\..\Data` = `Src/Data`）。

### 3.3 Unity PythonProcessLauncher（S3）

新增 `PythonProcessLauncher.cs`（或并入 BootstrapEntry）：

```csharp
public static class PythonProcessLauncher
{
    static System.Diagnostics.Process _process;

    public static void Launch()
    {
#if !UNITY_EDITOR   // 开发期不拉起，由开发者手动起 Python
        // 多开互斥：先探测端口，已通则复用，不拉起新 exe
        if (IsPythonAlive())
        {
            Debug.Log("[PythonProcessLauncher] 检测到已有 Python 实例，直接复用。");
            return;
        }
        string exePath = Path.Combine(GetPythonServerDir(), "agent_server.exe");
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath),
            UseShellExecute = false,
            CreateNoWindow = true,      // 无窗口
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        _process = Process.Start(psi);
#else
        // 编辑器：不拉起 exe，由开发者手动 `uv run python main.py`
        Debug.Log("[PythonProcessLauncher] 编辑器模式：不自动拉起 Python，请手动启动。");
#endif
    }

    public static void Shutdown()
    {
#if !UNITY_EDITOR
        try
        {
            // 优雅关闭：发 CloseRequest，等 ≤2s，仍存活则 Kill
            SendCloseRequest();               // 触发 Python 停止 Agent、flush 记忆、释放 Kuzu 锁
            if (!_process.WaitForExit(2000))  // 2s 超时
                _process.Kill();              // 兜底
        }
        catch { /* 忽略 */ }
#endif
    }

    static bool IsPythonAlive()
    {
        // 读端口文件 + TCP 探测，已有 Python 实例则返回 true
        int port = ReadPortFile();
        if (port <= 0) return false;
        using var client = new System.Net.Sockets.TcpClient();
        try { client.Connect("127.0.0.1", port); return client.Connected; }
        catch { return false; }
    }

    static string GetPythonServerDir()
    {
        // 打包：<游戏根>/PythonServer（Application.dataPath 的上级是游戏根）
        return Path.Combine(Directory.GetParent(Application.dataPath)?.FullName, "PythonServer");
    }
}
```

**接入点**：
- `BootstrapEntry.Start()`：先 `PythonProcessLauncher.Launch()`，再 `EnsureConnectedAsync()` 等就绪，然后进 Title。
- `BootstrapEntry.OnApplicationQuit()`：`PythonProcessLauncher.Shutdown()`（优雅关闭 + 超时 + Kill 兜底）。
- **返回 Title 场景不调用 Shutdown**（仅 `SceneStop`，Agent 进程保持存活，可 `astart_all` 继续）。

### 3.4 Unity 退出清理（S4）

- **返回 Title 不 Kill**：返回 Title 仅 `SceneStop`（Agent 被打断但进程仍存活，`astart_all` 可继续跑）。绝不在返回 Title 时清理进程。
- **Unity 进程退出时清理**：
  - **触发方式**：`PythonProcessLauncher` 静态构造函数注册 `Application.quitting` 事件（进程级，正常/异常/强制退出均触发）。`BootstrapEntry.OnApplicationQuit()` 保留作为双保险（注意：BootstrapEntry 挂在 Bootstrap 场景对象上，`LoadScene("Title")` 后对象销毁，其 OnApplicationQuit 实际不会触发——所以**不能依赖它**，v0.23.3b 验收发现此问题）。
  - **清理动作**：
    - 先发 `CloseRequest`（Python 端已有 `handle_close_request`，做资源清理：停止 Agent、flush 记忆、释放 Kuzu 文件锁），等有限短超时（≤2s）。
    - 超时仍存活 → `Kill()` 兜底。
    - **覆盖「复用已有实例」场景**：`Shutdown()` 不依赖 `_process` 是否为空。若 `_process == null`（Unity 复用了已存在的 Python 实例，`Launch()` 复用分支不赋值 `_process`），改为读 Python 单实例 PID 文件（`<游戏根>/PythonServer/db/agent_server.pid`）定位进程后关闭。
- **已定（PRD §7）**：优雅关闭（CloseRequest）+ 短超时 + Kill 兜底。Kuzu 文件锁敏感（`Doc/kuzu被文件锁时处理办法.md`），优雅关闭可避免下次启动时 `.wal` 锁问题；Unity 退出时间有限，必须短超时 + Kill 兜底保证清干净。

### 3.5 进程异常兜底（S6）

- 复用 `EnsureConnectedAsync` 的 30s 超时：超时抛 `TimeoutException` → Bootstrap 捕获 → 提示「Python 服务启动失败」+ 重试/退出按钮。
- 运行中断开：`AgentService` 现有断线处理基础上，增加「Python 进程是否存活」判断，异常退出可提示。

### 3.6 多开互斥（E7，已定）

**已定（PRD §7）：本期处理，双重防线。**

1. **Unity 启动前 TCP 探测**：`PythonProcessLauncher.Launch()` 前先探测端口（读端口文件 + 尝试连接）。若已连通，说明已有 Python 实例在跑，**不拉起新 exe**，直接复用。
2. **exe 启动自检**：`agent_server.exe` 启动时绑定端口失败（已被占用）→ 立即退出（`servers.py` 绑定端口抛异常是天然防线），避免重复监听。

**Mutex 暂不做**（Unity 单游戏进程 + 端口占用自检已覆盖主要场景，且 Mutex 处理「端口未绑上窗口期」价值有限）。

---

## 4. 实现步骤

> 每步完成后立即跑对应自测（编号见 §6.1），通过后再进入下一步。

| # | 步骤 | 完成后自测 |
|---|------|-----------|
| 1 | 新增 `path_config.py`（运行根目录解析） | P1、P2、P3 |
| 2 | 改 `main.py`、`api_config_loader.py`、`agent_interuptible.py`、`console_logger.py` 走 `path_config` | P4（开发态零破坏） |
| 3 | 新增 `Tools/build_python_exe.cmd` | P5 |
| 4 | 打包态验证：产 exe + 资源，启动→监听→写端口→TCP 可连 | P6、P7、P8、P10 |
| 5 | 打包态多开自检 | P9 |
| 6 | Unity 新增 `PythonProcessLauncher`，接入 `BootstrapEntry` | U1、U2、U3 |
| 7 | Unity 退出清理 + 异常路径 | U4、U5、U6 |
| 8 | 整体联调（打包版 + 编辑器双模式回归） | 见 §6.2 联调清单 |

---

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| kuzu/graphiti 打包收集不全 | 已实测 `--collect-all graphiti_core` 解决；kuzu 自动收集成功 |
| 中文路径下 exe 异常 | 需实测（E9）；PyInstaller 本身支持 Unicode 路径 |
| Kuzu 文件锁（退出残留） | 优雅关闭（发 CloseRequest）+ 退出 Kill；`Doc/kuzu被文件锁时处理办法.md` |
| Program Files 只读导致 db 写失败 | 启动时 `db/` 可写自检 + 提示「请解压到可写目录（勿放 Program Files）」（已定：数据放游戏根，方案 B） |
| 杀软误报 | onedir 降低概率；必要时白名单 |
| 破坏开发态 | `#if !UNITY_EDITOR` 隔离 + `path_config` 开发态回退现状；开发态回归测试 |

---

## 6. 测试方案

> 测试分两层：
> - **§6.1 开发者自测**：我在开发过程中必须自己跑通的测试（开发纪律「可自测的功能必须自测完成后再提交验收」）。不依赖 Unity 的部分必须在交付前自测通过；依赖 Unity 的部分明确标注联调项。
> - **§6.2 验收方案**：提供给你（用户）验收用，对应 PRD §6 验收标准，逐条给出操作步骤与预期结果。

### 6.1 开发者自测方案

**A. Python 侧（不依赖 Unity，交付前必须全部自测通过）**

| # | 自测项 | 对应实现 | 步骤 | 预期结果 |
|---|--------|----------|------|----------|
| P1 | **path_config 开发态路径正确** | S1 | 写最小脚本 `from path_config import get_runtime_root, get_port_config_file, get_data_dir, get_log_dir; print(...)` | 开发态返回 `Src/PythonServer`，`get_port_config_file()` 指向 `Src/PythonServer/Data/Config/agent_server_port.txt`（与现状一致） |
| P2 | **path_config 模拟打包态路径正确** | S1 | 伪造 `sys.frozen` + 临时目录，断言 `get_runtime_root()` 返回 exe 同级 | 打包态返回 exe 同级目录 |
| P3 | **ensure_runtime_writable 可写自检** | S5 | ① 正常可写目录 → 返回 True ② 伪造只读（把 db 指到只读路径）→ 返回 False | 可写返回 True；只读返回 False，不抛异常 |
| P4 | **开发态行为零破坏** | S1 | `uv run python main.py` 正常启动 | 端口文件、db、logs 路径与改造前完全一致；能监听、写端口文件（对照验证 3 方法） |
| P5 | **打包脚本产出** | S2 | 运行 `Tools/build_python_exe.cmd` | 产出 `agent_server.exe` + `_internal/` + 复制的外部资源（Data/、Lib/、config/、db/） |
| P6 | **打包态 exe 冒烟** | S1+S2 | 启动 `agent_server.exe`，观察 | 无窗口；启动到监听 ≤3s；写端口文件；进程稳定存活 |
| P7 | **打包态外部资源定位** | S1 | 启动 exe 后检查 `Data/Config/agent_server_port.txt`、`Lib/proto`（sys.path）、`db/`、`logs/` 是否都从 exe 同级正确解析 | 全部指向 exe 同级；`logs/console/` 有日志落盘 |
| P8 | **打包态 TCP 可连** | S1+S2 | 复用验证 3 方法（`/dev/tcp` 或 Python socket 连 127.0.0.1:port） | 可建立 TCP 连接 |
| P9 | **打包态多开自检** | S6 | 已有一个 exe 在跑时，再启动第二个 exe | 第二个立即退出（绑定端口失败），不出现两个监听进程 |
| P10 | **中文路径运行** | S1 | 把 Build 产物放到含中文/空格的目录（如 `e:\游戏\PythonServer\`）再启动 exe | 正常启动、监听、写端口（E9） |

**B. Unity 侧（需编辑器/打包联调，标注联调项）**

| # | 自测项 | 对应实现 | 步骤 | 预期结果 |
|---|--------|----------|------|----------|
| U1 | **编辑器不拉起 exe** | S3 | 编辑器 Play，观察进程列表 | 不出现 `agent_server.exe`；`PythonProcessLauncher` 打日志「编辑器模式：不自动拉起」 |
| U2 | **打包版自动拉起** | S3 | Unity Build Windows64 → 双击 exe | 自动拉起 `agent_server.exe`（无窗口），`EnsureConnectedAsync` 等端口就绪后进 Title |
| U3 | **多开互斥（Unity 侧）** | S3+S6 | ① 先手动起一个 Python，再 Play 打包版 → 应复用不拉起 ② 正常情况拉起一个 | ① 不出现第二个 exe 进程 ② 只出现一个 exe 进程 |
| U4 | **退出清理** | S4 | 打包版运行中正常退出游戏 | `agent_server.exe` 被清理，任务管理器无残留 |
| U5 | **退出清理（异常路径）** | S4 | 打包版运行中任务管理器强杀 Unity | `agent_server.exe` 被清理（OnApplicationQuit 触发） |
| U6 | **返回 Title 不 Kill** | S4 | 打包版从游戏场景返回 Title，观察 Python 进程 | Python 进程仍存活（仅 SceneStop），不 Kill |

### 6.2 验收方案（提供给你验收）

**前置**：确认已按 PRD §6 逐条验收；开发态与打包态分开验证。

| PRD §6 验收项 | 操作步骤 | 预期结果 |
|---------------|----------|----------|
| 1. `build_python_exe.cmd` 一键产出 exe + 资源 | 运行 `Tools/build_python_exe.cmd`，检查 `Build/PythonServer/` | 产出 `agent_server.exe` + `_internal/` + `Data/`、`Lib/`、`config/`、`db/` 外部资源 |
| 2. 打包态 exe 无窗口启动、监听、写端口、响应最小请求 | 启动 exe → 观察无窗口 → 检查端口文件 → TCP 连接 | 无窗口；启动 ≤3s；`agent_server_port.txt` 有端口；TCP 可连；进程存活 |
| 3. 打包态 `db/`、`logs/` 写入玩家可写目录 | 解压到**可写目录**启动 → 检查 db/logs | `db/` 建 Kuzu 库、`logs/console/` 有日志 |
| 4. 打包态下解压到只读目录有引导（S5 兜底） | 解压到只读目录启动 exe | 不自检崩溃；Unity 侧提示「请解压到可写目录」 |
| 5. Unity 打包版自动拉起 + 进 Title | 双击打包版 exe | 自动起 Python（无窗口）、等端口就绪、进 Title |
| 6. Unity 退出清理 | 打包版退出 → 任务管理器检查 | 无残留 `agent_server.exe` |
| 7. 开发态不受影响 | 编辑器 Play（开发者手动起 Python） | 行为与现状一致；编辑器不拉起 exe |
| 8. 中文路径运行 | Build 放到含中文目录 → 打包版运行 | 正常启动、连接、进 Title（E9） |

**联调清单（需 Unity + Python 同时在场才能验收）**：
- [ ] 打包版双击 exe → 自动起 Python → 进 Title → 进游戏 → Agent 正常推理
- [ ] 返回 Title → Python 进程仍存活 → 再进游戏正常
- [ ] 退出游戏（正常/任务管理器强杀）→ 无 Python 残留
- [ ] 多开：连开两个游戏 → 只有一个 Python 实例
- [ ] 解压到只读目录 → 有「请解压到可写目录」提示
- [ ] 中文路径目录下完整跑一遍启动→进游戏→退出

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-27 | 实现 S1-S6：path_config/single_instance（runtime/）、各模块路径收敛、build_python_exe.cmd、PythonProcessLauncher + BootstrapEntry；Python 侧自测 P1-P4/P8/P9 通过；Unity 侧待联调 U1-U6 |
| 2026-08-28 | 联调修复：(1) `console_logger.py` TeeWriter 对 `--noconsole` 下 `sys.stdout=None` 崩溃（AttributeError）→ 加 `_NullStream` 兜底；(2) PyInstaller 漏打包 `tiktoken_ext` → 报 `Unknown encoding cl100k_base`，Agent 无法接收消息 → `build_python_exe.cmd` 增加 `--collect-all tiktoken_ext`/`--hidden-import tiktoken_ext`；(3) Unity 退出未清理 Python 进程（「退出 Unity 即关 Python」验收不通过）→ 根因：BootstrapEntry 挂在 Bootstrap 场景对象上，`LoadScene("Title")` 后对象销毁，`OnApplicationQuit` 不再触发；且复用已有实例时 `_process` 为空，`Shutdown()` 直接 return。修复：`PythonProcessLauncher` 静态构造函数注册 `Application.quitting` + `Shutdown()` 支持读 PID 文件定位进程（覆盖复用场景） |
| 2026-08-29 | 联调修复：(4) `requests` 缺 CA 证书 → `FileNotFoundError: [Errno 2] No such file or directory`（`requests/adapters.py` 加载 `cacert.pem` 失败）→ PyInstaller 漏打包 `certifi` 数据文件 → `build_python_exe.cmd` 增加 `--collect-all certifi`，重新打包后 `_internal/certifi/cacert.pem` 就位；(5) **Kuzu 中文路径**：Windows 下 Kuzu C++ 底层用 `CreateFileA`（ANSI），含中文的绝对路径在新建库时报 `Error 3` / `UnicodeDecodeError` → `db_connection_service.py` 增加 NTFS junction 方案（`mklink /J` 把含非 ASCII 的 db 目录映射到 `%TEMP%\pskuzu\<hash>` 纯 ASCII 路径，按 db 路径 hash 隔离不同运行根避免文件锁冲突），实测中文路径下新建/打开/删除/重建均正常；(6) **api_config.json 大小写**：字段名统一为 SCREAMING_SNAKE_CASE（`AGENT_API_KEY` 等 12 项），与 Unity `ApiConfig` / Python `API_CONFIG_KEYS` 对齐（JsonUtility 大小写敏感），编辑器读 key 正常 |
| 2026-08-30 | **验收通过**（v0.23.3b 全量验收：开发态/打包态、新游戏/续玩/退出清理、中文路径、无残留进程）。完整复盘见 `验收报告.md` |

---

*本文档由 Cursor Agent 根据 PRD + 验证报告生成；**你确认后** Agent 方可按本方案修改代码。*
