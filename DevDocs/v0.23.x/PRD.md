# PRD — v0.23.x 玩家 API 配置落盘加密

> **状态**：待确认
> **对应需求**：`requirements/`（本需求由用户口头提出，见本文档 §1.1 与 §7）
> **参考方案**：`DevDocs/feature-design/打包方案.md`（§4.2「必须加密，禁止明文」、§8 决策项 8）
> **最后更新**：2026-09-02

---

## 1. 背景与目标

### 1.1 背景

打包方案（`feature-design/打包方案.md`）已定：**玩家自备 API Key**，Title 场景配置，持久化在玩家本机 `Data/Config/api_config.json`，运行时注入给 Python。其中 §4.2 明确要求「**必须加密，禁止明文**」。

**当前现状**（v0.23.4 已实现）：

- Unity 侧 `ApiConfigStore.Save()` 用 `JsonConfigIO.SaveJson` 写 **明文** `api_config.json`（12 个 SCREAMING_SNAKE_CASE 键，UTF-8）。
- Python 侧 `config/api_config_loader.load_api_config_into_env()` 读明文 JSON 注入 `os.environ`。
- `v0.23.4/solution.md` 已明确标注：「**API Key 明文风险基线**：`api_config.json` 当前为明文存储……加密属后续版本（Unity+Python 两端协同），不在本版本范围」。

即：**当前打包版分发的 `api_config.json` 里，玩家填的 API Key 是明文落盘的**。这是打包版真实的 Key 泄露风险——任何拿到游戏目录的人都能直接读到 Key。

### 1.2 目标

把 `api_config.json` 从明文改为 **加密存储**，且加密方案满足：

1. **机器绑定**：Key 只能在本机解密，拷贝到别处无法使用。
2. **跨平台预留**：当前 Windows（DPAPI），为 macOS（Keychain）预留空间，不堵死跨平台路（对齐打包方案 §8 决策项 8）。
3. **Unity + Python 两端协同**：Unity 写（加密）、Python 读（解密），两端能解密同一份文件。
4. **开发态不破坏**：开发期（编辑器 + `.env`）行为不受影响。

### 1.3 方案来源（已确认）

**直接复用 Cursor 本机的加密机制**（Electron `safeStorage` / Chromium 标准，用户已确认采用）：**DPAPI（机器绑定主密钥） + AES-256-GCM**。

已实测确认 Cursor 本机实现（`C:\Users\liudo\AppData\Roaming\Cursor\`）：

- `Local State` 里 `os_crypt.encrypted_key`：`DPAPI` 前缀 + base64，即 **DPAPI 加密的 AES 主密钥**。
- `state.vscdb`（SQLite）里存模型 Key，key 名如 `secret://cursorAuth/openAIKey`，value 为 base64 密文（非明文）。
- 加解密链路：DPAPI 解出 AES-256 主密钥 → AES-256-GCM（12 字节 nonce + 密文 + 16 字节 tag）加解密每个 secret。

---

## 2. 范围

### 2.1 本期包含

- Unity 侧 `ApiConfigStore`：落盘改为 **DPAPI 加密**（写密文 base64），读取时解密。
- Python 侧 `config/api_config_loader`：读取改为 **DPAPI 解密**。
- `api_config.json` 的**文件格式与字段名保持不变**（12 个 SCREAMING_SNAKE_CASE 键），只是**值加密**；保持 Python 侧 `API_CONFIG_KEYS` / Unity `ApiConfig` 无需改动字段。
- 与打包方案 §4.2/§8 决策项 8 对齐（AES + 机器绑定密钥、为 macOS 预留）。

### 2.2 本期不包含

- **不做** AES-256-GCM 的第二层包装（直接用 DPAPI 加密整个 JSON 文本——见 §3.1 决策，避免过度设计）。
- **不做** macOS Keychain 实现（仅预留跨平台抽象，Windows 落地）。
- **不改** API Key 策略（玩家自备）、注入时序（`load_api_config_into_env` 延后初始化）、`InitRequest` 信号链路。
- **不做**「配置迁移」：已存在的明文 `api_config.json` 由玩家在 Title 重新填写（见 §4.3）。
- **不做** 打包脚本（v0.23.5 已另定，此版本不碰）。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家 | 在 Title 配置 API Key 并保存 | Key 加密落盘，`api_config.json` 里看不到明文 Key |
| 玩家 | 保存后进入游戏，Python 读取配置 | Python 能正确解密，正常推理/记忆检索 |
| 玩家 | 把游戏目录整个拷到另一台机器 | 新机器上配置不可用（DPAPI 机器绑定），需重新配置——**可接受**（单机场景） |
| 玩家/第三方 | 拿到游戏目录、直接查看 `api_config.json` | 看到的是密文，无法还原 Key |
| 开发者 | 编辑器 Play + `.env` 工作流 | 行为与现状一致，不强制走加密文件 |

---

## 4. 功能需求

### 4.1 API 配置落盘加密（Unity 侧）

- `ApiConfigStore.Save()`：序列化 `ApiConfig` 为 JSON 文本后，**DPAPI 加密**，写为 `api_config.json`（密文 + base64，UTF-8）。
- `ApiConfigStore.Load()`：读 `api_config.json`，**DPAPI 解密**后反序列化为 `ApiConfig`。
- 文件字段名（12 个 SCREAMING_SNAKE_CASE 键）保持不变，Python 侧零感知（除了解密步骤）。

### 4.2 API 配置读取解密（Python 侧）

- `config/api_config_loader.load_api_config_into_env()`：读取 `api_config.json` 时，先 **DPAPI 解密**再解析 JSON，注入 `os.environ`。
- 保持「`api_config.json`（存在且非空）> `.env`」的优先级不变；加密文件解密失败时回退 `.env` 并打日志（不崩溃）。

### 4.3 明文旧文件迁移

- 若检测到 `api_config.json` 是**旧明文格式**（非加密头），Unity 侧 `Load()` 视为空配置（返回空 `ApiConfig`），触发 Title 引导玩家重新配置——**不自动迁移明文 Key**（避免明文转存）。

---

## 5. 非功能需求

- **加密强度**：DPAPI 系统级加密（绑定用户 + 机器），满足「机器绑定密钥」要求。
- **兼容性**：Unity 2021.3（.NET Standard 2.1，需 `System.Security.Cryptography.ProtectedData`）+ Windows；Python 3.11/3.12（`ctypes` 调 `CryptUnprotectData`，不新增依赖）。
- **开发态零破坏**：编辑器下 `load_dotenv()` + `.env` 工作流不变；无加密文件时回退 `.env`。
- **可观测性**：解密失败打清晰日志（区分「文件不存在」「格式旧」「解密失败」）。

---

## 6. 验收标准

- [ ] Unity Title 保存 API 配置后，`api_config.json` 内容为**密文**（base64），不包含任何明文 Key 字段。
- [ ] 保存后 Python 端能正确解密读取，Agent 正常推理（对话）、记忆检索（Embedding/Reranker）正常。
- [ ] 直接查看 `api_config.json` 无法还原 Key；把文件拷到另一台机器（或另一用户）无法解密。
- [ ] 旧明文 `api_config.json`（无加密头）被识别为无效，Title 引导重新配置，不崩溃、不泄露。
- [ ] 开发态（编辑器 + `.env`）行为与现状一致，不强制走加密文件。
- [ ] 解密失败时 Python 回退 `.env` 并打日志，不崩溃。

---

## 7. 待确认问题

- [x] **加密方案**：已确认 DPAPI（Windows 机器绑定），复用 Cursor/Chromium 方案（用户确认）。
- [ ] **是否叠加 AES-256-GCM 第二层**：方案倾向「直接 DPAPI 加密整个 JSON」（不叠加），因单机场景下 DPAPI 已足够且更简单；若需跨平台纯 AES 可后续叠加。**待你确认**。
- [ ] **旧明文文件处理**：倾向「不自动迁移，引导重新配置」。**待你确认**。
- [ ] **版本号**：待定（本目录暂为 `v0.23.x`，定版时改目录名）。

---

*本文档由 Cursor Agent 根据用户口头需求 + 打包方案 §4.2/§8 生成；确认前请勿直接据此改代码。*
