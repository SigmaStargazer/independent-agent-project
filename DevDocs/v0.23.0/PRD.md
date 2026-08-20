# PRD — v0.23.0 Title API 配置 UI 与注入（提前实现）

> **状态**：已确认
> **对应需求**：`requirements/LLM配置实现.md`
> **引用方案**：`DevDocs/feature-design/打包方案.md`（§4.2 玩家 API 配置存储、§4.3 API Key 注入时序、§10 版本规划 v0.23.4）
> **最后更新**：2026-08-20

---

## 1. 背景与目标

`v0.23` 大版本围绕 `DevDocs/feature-design/打包方案.md` 实施。按打包方案 §10，`Title API 配置 UI 与注入` 原计划在 **v0.23.4** 实现；本期（v0.23.0）**提前实现该功能**，以便美术能据此开展 UI 设计工作。

`Title.unity` 场景中已具备配置面板的静态骨架（`PanelLLMAgent` / `PanelLLMMemory` / `PanelEmbedding` / `PanelReranker` 四个配置子面板，各含 `PanelBase` / `PanelApiKey` / `PanelModel` 三个文本框区域；`MsgboxSaveConfig` / `MsgboxNoApiKey` 两个弹窗，基于 `Resources/UI/Msgbox.prefab` 实例化）。但**文本框尚未与任何配置数据双向绑定**，弹窗按钮未接实际保存逻辑，进入游戏入口也**未做配置完整性校验**。

本期目标：

1. 让四个配置面板的 12 个文本框能读取并显示配置数据（`api_config.json`）。
2. 退出配置面板时若配置发生改动，弹出 `MsgboxSaveConfig` 询问是否保存。
3. 点击保存按钮调用 `UITitle.OnConfigSaveConfig` 更新配置。
4. 模型相关 12 项配置存在未配置项时，`OnClickNewGame` / `OnClickContinueGame` 弹出 `MsgboxNoApiKey`，不进入 GameFlow。
5. 按需求「疑问」：Python 侧 `MEMORY_API` / `EMBEDDING_API` / `RERANKER_API`（连同 `AGENT_API_*`）改由读取 `Data/Config/api_config.json`，替代 `.env` 作为运行时配置源，并打通「游戏中更改配置后如何生效」的链路。

## 2. 范围

### 2.1 本期包含

- Unity Title 场景：配置面板文本框 ↔ 配置数据（`ApiConfigStore`）双向绑定。
- Unity Title 场景：退出面板变更检测 + `MsgboxSaveConfig` 保存确认弹窗。
- Unity Title 场景：保存按钮 `OnConfigSaveConfig` 落地保存逻辑。
- Unity Title 场景：`OnClickNewGame` / `OnClickContinueGame` 配置完整性校验 + `MsgboxNoApiKey` 拦截。
- Unity 新增 `ApiConfigStore`：`Data/Config/api_config.json` 的读写、校验；并将 `api_config.json` 加入 `.gitignore`，防止 Key 上传远程仓库。
- Python 新增统一配置读取层：`api_config.json` 为运行时配置源，未配置项回退 `.env`。
- Python **延迟初始化改造**：无 Key 也能启动监听端口；收到 Unity 初始化信号后再读 `api_config.json` 注入 `os.environ` 并执行 `MemoryManager.initialize()`（打包方案 §4.3/§4.6、v0.23.3 内容提前到本期）。

### 2.2 本期不包含

- **加密**：`api_config.json` 本期**明文存储**。打包方案 §4.2/§8 已定「必须加密（AES + 机器绑定密钥）」，留待正式发布前版本实施（见 §7 待确认）。
- **打包脚本**（`build_package.cmd` 等，v0.23.5）。
- **Python 子进程托管**（`PythonProcessLauncher`，v0.23.2）。
- **Unity 端口路径改造与连接重试**（v0.23.0 另一主题，若本期需同版实现另行拆分）。
- 供应商模板、Key 预校验请求（v0.23.4 可选增强，本期不做）。
- 游戏设置（分辨率/语言等）与 `GameSettingsStore`：仅作架构预留，本期不实现。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家/美术 | 首次进入 Title，打开「设置 → LLM Agent」等面板 | 文本框显示当前已保存的 `API_BASE` / `API_KEY` / `MODEL`（无配置则空） |
| 玩家 | 修改任一文本框内容后退出面板 | 弹出 `MsgboxSaveConfig`，确认保存则写入配置，取消则不保存 |
| 玩家 | 点「新游戏」/「继续游戏」但 12 项配置未填全 | 弹出 `MsgboxNoApiKey`，不进入 GameFlow |
| 开发者 | 开发期 Python 直接运行 | `api_config.json` 存在时以其为准；缺失时回退 `.env`，开发工作流不受影响 |
| 玩家 | 游戏中/退出后修改配置 | 下次进入游戏（Python 重新初始化）时按新配置生效 |

## 4. 功能需求

### 4.1 配置面板读取与回填

- 打开任一配置子面板（`PanelLLMAgent`/`PanelLLMMemory`/`PanelEmbedding`/`PanelReranker`）时，将对应 3 项配置（`API_BASE`/`API_KEY`/`MODEL`）读取并填充到三个文本框。
- 四个面板共 12 项，映射到配置键：

| 面板 | 配置键 |
|------|--------|
| PanelLLMAgent | `AGENT_API_BASE` / `AGENT_API_KEY` / `AGENT_MODEL` |
| PanelLLMMemory | `MEMORY_API_BASE` / `MEMORY_API_KEY` / `MEMORY_MODEL` |
| PanelEmbedding | `EMBEDDING_API_BASE` / `EMBEDDING_API_KEY` / `EMBEDDING_MODEL` |
| PanelReranker | `RERANKER_API_BASE` / `RERANKER_API_KEY` / `RERANKER_MODEL` |

### 4.2 退出变更检测与保存弹窗

- 从任一配置子面板退出时（ESC 返回 / 按钮返回设置总览），比较文本框当前内容与**进入面板时快照**：
  - 有任意一项变更 → 弹出 `MsgboxSaveConfig`；
  - 无变更 → 直接返回，不弹窗。
- `MsgboxSaveConfig` 两个按钮：
  - **保存**：绑定 `UITitle.OnConfigSaveConfig`，将 12 个文本框内容写入配置并持久化，然后返回设置总览。
  - **取消**：放弃变更，返回设置总览（文本框保留未保存内容但下次进入重新回填）。

### 4.3 配置完整性校验拦截

- `OnClickNewGame` / `OnClickContinueGame` 执行前校验 12 项配置（`API_BASE`/`API_KEY`/`MODEL` 均非空）：
  - 全部配置 → 正常进入 `GameFlowManager.StartNewGame` / `ContinueGame`。
  - 存在未配置项 → 弹出 `MsgboxNoApiKey`，**不**进入 GameFlow。

### 4.4 配置持久化与 Python 读取

- Unity 侧新增 `ApiConfigStore`：
  - `Load()`：从游戏根 `Data/Config/api_config.json` 读取；文件不存在则返回空配置。
  - `Save(ApiConfig)`：将 12 项配置写入同路径 JSON 文件（本期明文，UTF-8）。
  - `IsComplete()`：12 项关键字段是否全部非空。
- Python 侧：`agent_interuptible.py` / `memory_manager.py` / `embedder_service.py` 从 `.env` 直读改为经**统一配置层**读取，配置优先级：`api_config.json` > `.env`。
- 生效方式：Python 启动时（或收到初始化信号时）读取 `api_config.json` 并注入 `os.environ`，后续 `os.getenv` 逻辑复用；配置变更在 Python 重启（开发期手动重启 / 打包版随游戏重启 Python 子进程）后生效。
- Python 延迟初始化：Python 无 Key 也可启动并监听端口；Unity 发 init 信号后，Python 读 `api_config.json` 注入 `os.environ` 再执行 `MemoryManager.initialize()` 与 Agent LLM 构造。

## 5. 非功能需求

- **UTF-8**：`api_config.json` 与相关 `.cs`/`.py` 文件均按 UTF-8 读写（见 `.cursor/rules/file-encoding.mdc`）。
- **兼容开发期**：开发环境无 `api_config.json` 时，Python 回退 `.env`；Unity 编辑器下未保存过配置时文本框留空（不自动读 `.env`，本期只认 `api_config.json`）。
- **不泄露 Key**：`api_config.json` **必须加入 `.gitignore`**，禁止提交到仓库（开发 Key 或玩家 Key 均不得入库）。
- 12 项配置键名与打包方案 §4.2 完全一致（`AGENT_*`/`MEMORY_*`/`EMBEDDING_*`/`RERANKER_*`）。

## 6. 验收标准

- [ ] 打开四个配置面板，文本框正确显示 `api_config.json` 中已保存的 `API_BASE`/`API_KEY`/`MODEL`（无文件或缺失项为空）。
- [ ] 修改任一文本框后退出面板，弹出 `MsgboxSaveConfig`；未修改退出不弹窗。
- [ ] 点击保存按钮，`api_config.json` 被正确写入 12 项配置；重新打开面板内容正确回填。
- [ ] 点击取消按钮，配置不被写入。
- [ ] 12 项配置存在未配置项时，点「新游戏」/「继续游戏」弹出 `MsgboxNoApiKey` 且不进入 GameFlow；配置齐全时可正常进入。
- [ ] `api_config.json` 已加入 `.gitignore`，`git status` 不显示该文件。
- [ ] Python 无 Key 启动：不带任何 API Key 可正常建 Kuzu 库、监听端口、接受 Unity 连接（不报错）。
- [ ] Unity 发 init 信号后，Python 读 `api_config.json` 注入 `os.environ`，再执行 `MemoryManager.initialize()` 构造 LLM/Embedder；按其中 Key 正常推理/记忆检索（可联调验证）。
- [ ] `api_config.json` 缺失时 Python 回退 `.env`，现有开发工作流可正常运行。
- [ ] 修改 `api_config.json` 后重启 Python，新配置生效。
- [ ] 未发 init 信号直接调 Agent 推理，应报「未初始化」错（而非用空 Key 静默失败）。

## 7. 待确认问题（已确认）

- [x] `api_config.json` **本期明文存储**，加密（AES + 机器绑定密钥）留待正式发布前版本实施（打包方案 §8 已定必须加密，此处为分期）。**已确认（2026-08-20）**。
- [x] 编辑器开发期：文本框**只认 `api_config.json`**，缺失留空，不从 `.env` 回填显示。**已确认（2026-08-20）**。
- [x] **Python 延迟初始化改造放入本期**：无 Key 启动 + init 信号（打包方案 v0.23.3 内容提前）。**已确认（2026-08-20）**。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
