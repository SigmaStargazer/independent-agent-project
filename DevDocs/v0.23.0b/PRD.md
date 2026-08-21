# PRD — v0.23.0b 实现方式优化（Title 零系统 / 进游戏初始化 / 回 Title 关闭 / UITitle 拆分）

> **状态**：已实现（Python 侧自测 + Unity 联调验收通过）
> **对应需求**：`requirements/实现方式优化.md`
> **前置分析**：`DevDocs/Analysis/analysis_实现方式疑问.md`
> **基于版本**：`DevDocs/v0.23.0a/`（已 commit）
> **最后更新**：2026-08-21

---

## 1. 背景与目标

v0.23.0a 已实现「Title 场景 API 配置 UI + Python 延迟初始化」并 commit。用户提出三点疑问（见 Analysis），并明确一个**架构原则**：

> 追求对架构最干净的实现，而非改动最小；哪怕彻底重构也要避免架构腐化。

**核心生命周期模型**（本版本一切设计的根基）：

```
Title 阶段：不应存在任何已初始化的系统
进游戏时：  才 initialize（使用当前 api_config.json 最新 Key）
回 Title 时：close（释放全部系统，回到「零系统」状态）
```

因此本版本目标：

1. **Title 阶段零系统**：Python 启动仅监听端口；Title 阶段 MemoryManager/AgentManager/EmbedderService 一律未初始化。进游戏（NewGame/ContinueGame）Flow 内触发初始化。
2. **回 Title 关闭**：ReturnToTitleFlow 关闭全部系统（含补 `EmbedderService.close`、清 Agent LLM 缓存），之后再次进游戏即全新初始化、使用最新 Key——**彻底消除「必须重启进程」问题**，且架构干净（无 reinitialize / 热更新）。
3. **UITitle 拆分**：配置读写下沉到 `UISetting`（挂 UIConfig），UITitle 仅留页面切换。

## 2. 范围

### 2.1 本期包含

- Python：生命周期编排（进游戏 init / 回 Title close），含 `EmbedderService.close` 补充、Agent LLM 缓存清理、**TimeSystem 归零**。
- 协议：新增 `CloseRequest`/`CloseResponse`（回 Title 时通知 Python 关闭系统）。
- Unity：新增 `InitializeStep`（进游戏 Flow 内，注入配置 + 确保系统就绪）、`CloseStep`（回 Title Flow 内）。
- Unity：`UITitle` 拆分 → 新增 `UISetting`（配置读写 + 完整性校验），UITitle 仅留页面切换。
- Unity：入口（OnClickNewGame/OnClickContinueGame）改为调用 `UISetting` 校验，通过后进 Flow（不再发 InitRequest）。
- Python：移除 `main()` 启动时的 `aset_time`（TimeSystem 启动归属 SceneStart、归零归属 leave_game）。
- 文档：更新场景绑定指引（12 个 InputField 引用迁移到 UISetting）。

### 2.2 本期不包含

- **加密**（api_config.json 仍明文，与 a 版一致，留正式发布前）。
- **reinitialize / 热更新**（按架构原则，不需要）。
- 场景内（非 Title）改 Key 的热更新（需回 Title，保持 a 版行为）。
- 打包脚本 / Python 子进程托管（另版）。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家 | 在 Title 配置面板改 Key → 保存 → 点「新游戏/继续游戏」 | 新 Key 直接生效（无需重启游戏/Python） |
| 玩家 | 游戏内点「回标题」→ 回 Title → 再点「新游戏/继续游戏」 | 再次进游戏用最新 Key（关闭后重新初始化） |
| 玩家 | 12 项配置不全时点「新游戏/继续游戏」 | 弹 MsgboxNoApiKey，不进 GameFlow |
| 开发者 | 手动起 Python 联调 | 初始化走正式链路（进 Flow 触发）；`--auto-init` 仅调试快速初始化 |

## 4. 功能需求

### 4.1 Title 阶段零系统（Python + Unity）

- Python 进程启动：仅监听端口，不初始化任何系统（无 Key 启动）。
- Unity 进游戏 Flow（NewGame/ContinueGame）新增 `InitializeStep`：发 `InitRequest` → Python 读 `api_config.json`（最新）注入 env + `MemoryManager.initialize()`。
- `--auto-init` 保留为纯调试用途（进程启动即初始化，便于开发期联调），不参与正式链路。

### 4.2 回 Title 关闭全部系统（Python + Unity）

- Unity `ReturnToTitleFlow`：**`CloseStep` 取代原 `StopAgentStep`**（方案 X，`leave_game` 完整负责停止 Agent + 关闭系统），`CloseStep` 发 `CloseRequest`。
- Python `CloseRequest` handler（`AgentLifecycle.leave_game()`）：停止全部 Agent + 关闭全部已初始化系统——
  - `AgentManager().aremove_all()`（停止 Agent，始终执行）
  - 清 Agent LLM 缓存（`agent_interuptible` 的 `_llm_with_tools`，**需补充 reset**）
  - `TimeSystem().areset()`（暂停 + 归零虚拟时间，**需补充**）
  - `MemoryManager().close()`
  - `DBConnectionService().close()`
  - `EmbedderService().close()`（**需补充**）
- 关闭幂等、可重复；Title 阶段可多次进出（Agent 停止 / 时间归零始终执行，资源关闭幂等）。

### 4.3 切关不关闭

- `NextMapFlow`（游戏内切关）**不**触发初始化或关闭，记忆系统保持运行（Interrupt → Backup → LoadScene → Start）。

### 4.4 TimeSystem 生命周期归属（Python）

| 阶段 | 归属 | 动作 |
|------|------|------|
| 进程启动 | 无（**移除** `main()` 的 `aset_time`） | 不设置，Title 阶段 TimeSystem 完全零状态 |
| 进游戏 | `enter_game()` **设基准** + `SceneStart` **启动** | `enter_game`：`aset_time(2016,1,1)`（设基准，不启动）；`SceneStart`：`aset_speed(1440)` + `astart_time()` |
| 回 Title | `leave_game()`（新增） | `TimeSystem().areset()`（暂停 + 归零） |
| 场景间清场 | `SceneStop`（现状不动） | 仅 `apause_time()` |

- 时间**基准**在 `enter_game` 设置、时钟**启动**在 `SceneStart`（2026-08-21 修正）：Unity Flow 中 `CreateAgent`/`LoadAgent` 在 `SceneStart` 之前执行，需先有非 None 时间基准（否则 `EntityNode.created_at=None` 报错）。`SceneStart` 仍是时钟启动的统一终点。

### 4.5 UITitle 拆分（Unity）

- 新增 `UISetting`：12 个 `TMP_InputField` 引用、`ApiConfigStore` 读写、回填、变更检测（`HasConfigChanged`）、保存/取消（`OnConfirmSaveConfig`/`OnCancelSaveConfig`）、完整性校验（`IsConfigReady`）。
- `UITitle`：仅保留 `ShowPressAnyButton/ShowMainMenu/ShowConfig/SetSubPanelActive`、ESC 分发、4 个弹窗开关；入口按钮调用 `UISetting`。

## 5. 非功能需求

- 关闭链路需处理 Kuzu 文件锁（复用 backup/restore/delete 已验证的 close 流程）。
- 初始化/关闭必须幂等。
- 编码：所有新增/修改文档 UTF-8。

## 6. 验收标准

- [ ] Python 启动（不带 `--auto-init`）后 Title 阶段无任何系统初始化；改 `api_config.json` 后**不重启进程**，下次进游戏即用新 Key。
- [ ] NewGame / ContinueGame 进游戏时 `InitializeStep` 触发初始化（Python 日志确认）。
- [ ] 回 Title 时 `CloseStep` 关闭全部系统（Python 日志确认 close，含虚拟时间归零）；再进游戏重新初始化且用最新 Key、虚拟时间从 2016-01-01 重新开始。
- [ ] 切关 NextMapFlow 不触发关闭，记忆系统保持运行、虚拟时间继续走。
- [x] `main()` 不再设置虚拟时间（Title 阶段 TimeSystem 完全零状态）；时间基准由 `enter_game` 设置（2016-01-01），时钟由 `SceneStart` 启动。
- [ ] 12 项不全点开始 → 弹 MsgboxNoApiKey 且不进 GameFlow。
- [ ] `UITitle` 无 API 配置读写逻辑；`UISetting` 独立可绑定 12 个 InputField。
- [ ] 场景绑定指引已更新（InputField 引用迁移到 UISetting）。

## 7. 待确认问题

- [x] **协议（已决策）**：新增 `CloseRequest`/`CloseResponse`（field 34/11）。`SceneStopRequest` 是**场景级停止**（停时间 + 移除 Agent，系统仍初始化），用于 NewGame/ContinueGame 清场；`CloseRequest` 是**系统级关闭**（清 LLM 缓存 + 关 Memory/DB/Embedder，回到零系统），仅用于回 Title。两者职责不同，不能复用。
- [x] **ReturnToTitle 职责（已决策：方案 X）**：`CloseStep` 取代 ReturnToTitleFlow 里的 `StopAgentStep`，`leave_game()` 完整负责「停止 Agent + 关闭全部系统」，无职责重叠。
- [x] **ContinueGameFlow 双场景（已决策：方案 A）**：继续复用 ContinueGameFlow（Title 继续游戏 + 关卡内 Retry），靠幂等 `InitializeStep` 兼容两种初始化状态，不拆分 RetryGameFlow。
- [x] **Step 命名（已决策）**：`InitializeMemoryStep` → `InitializeStep`（初始化的不止 Memory）；`CloseMemoryStep` → `CloseStep`（关闭的也不止 Memory，与 `InitializeStep` 对称）。
- [x] **`--auto-init`（已决策：保留）**：保留为**纯调试开关**（开发期不起 Unity 也能初始化自测），不参与正式打包链路。
- [x] **UITitle 拆分（已决策：一起改）**：Unity 侧改动由**用户手动完成**，Agent 提供《场景绑定指引》说明迁移步骤。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
