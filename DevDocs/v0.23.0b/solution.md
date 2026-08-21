# 技术方案 — v0.23.0b 生命周期重构（Title 零系统 / 进游戏初始化 / 回 Title 关闭 / UITitle 拆分）

> **状态**：已实现（Python 侧自测 + Unity 联调验收通过）
> **依据 PRD**：`PRD.md`
> **前置分析**：`DevDocs/Analysis/analysis_实现方式疑问.md`
> **基于版本**：`DevDocs/v0.23.0a/`（已 commit）
> **最后更新**：2026-08-21

---

## 1. 方案概述

**架构原则**（用户明确）：追求最干净的实现，而非改动最小；彻底重构以避免架构腐化。

本版本以「**生命周期**」为第一公民重构 v0.23.0a 的初始化方式，根除「必须重启进程才生效」问题：

```
┌─ Title 阶段 ────────────────┐        ┌─ 游戏场景阶段 ───────────────┐
│ 零系统：任何系统均未初始化     │ ──进游戏→ │ InitializeStep:          │
│ （Python 启动仅监听端口）      │        │   InitRequest → 全新 initialize │
│                              │        │   → CreateAgent/LoadAgent/Start │
└──────────────────────────────┘        └───────────────────────────────┘
        ▲                                          │
        │ ReturnToTitleFlow:                       │ 游戏内（切关 NextMapFlow）：
        │   CloseStep → CloseRequest              │ 不 init 不 close，系统保持运行
        │   → close 全部系统                        │
        └──────────────────────────────────────────┘
```

**核心**：
- **不需要 reinitialize / 热更新**——Title 阶段本就没有已初始化系统，进游戏永远是一次全新 `initialize()`，自然使用当前 `api_config.json` 最新 Key。
- **新增 `CloseRequest`**：回 Title 时通知 Python 关闭全部系统，Title 回到「零系统」状态。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| 协议 | `Tools/message.proto` | 新增 `CloseRequest`/`CloseResponse`（field 34/11） |
| Unity | `Services/AgentService.cs` / `AgentServiceAsyncExtensions.cs` | 修改（接入 CloseRequest/CloseResponse） |
| Unity | `GameFlow/Steps/InitializeStep.cs`（新增） | 新增 |
| Unity | `GameFlow/Steps/CloseStep.cs`（新增） | 新增 |
| Unity | `GameFlow/Flows/NewGameFlow.cs` / `ContinueGameFlow.cs` | 修改（插入 Init Step） |
| Unity | `GameFlow/Flows/ReturnToTitleFlow.cs` | 修改（CloseStep 取代 StopAgentStep） |
| Unity | `ViewController/UI/UITitle.cs` | 修改（拆分，仅留页面切换） |
| Unity | `ViewController/UI/UISetting.cs`（新增，挂 UIConfig） | 新增 |
| Unity | `Title.unity` / 配置面板 Prefab | 修改（场景绑定迁移） |
| Python | `lifecycle/lifecycle.py`（新增，`AgentLifecycle` 生命周期编排） | 新增 |
| Python | `main.py` | 修改（CloseRequest handler + InitRequest 收敛 + 移除启动时 aset_time） |
| Python | `agent_framwork/systems/time_system.py` | 修改（补 `areset()`） |
| Python | `agent_framwork/agents/agent_interuptible.py` | 修改（补 `reset_llm_cache`） |
| Python | `memory_system/embedder/embedder_service.py` | 修改（补 `close`） |
| 文档 | `DevDocs/v0.23.0b/场景绑定指引.md` | 新增（InputField 迁移到 UISetting） |
| 文档 | `DevDocs/feature-design/打包方案.md` | 修改（改 Key 生效描述更新） |

## 3. 详细设计

### 3.0 生命周期编排（Python 侧统一入口）

> 本版本的生命周期架构梳理（跨版本原则、各系统归属、Unity Flow 对应关系）见 **`DevDocs/Architecture/生命周期架构.md`**；本节约实现细节。

为保持架构干净，在 Python 侧提供一个**统一的生命周期编排模块**，进游戏/回 Title 都走它，避免逻辑散落在各 handler：

```python
# lifecycle/lifecycle.py（新增）——进程级生命周期编排，与 memory_system/、agent_framwork/ 平级
class AgentLifecycle:
    """进游戏 / 回 Title 的统一生命周期入口。"""

    @staticmethod
    async def enter_game() -> None:
        """进游戏：注入最新配置并全新初始化。
        - 幂等：已在游戏内（已初始化）则直接返回，不重复初始化。"""
        if MemoryManager().is_initialized:
            print("[lifecycle] 已在游戏中（已初始化），跳过 enter_game")
            return
        load_api_config_into_env(force=True)      # 读最新 api_config.json 注入 env
        await MemoryManager().initialize()         # 内部 init dbsvc + embedder + graphiti + action_skill

    @staticmethod
    async def leave_game() -> None:
        """回 Title：停止全部 Agent、归零时间并关闭全部系统，回到零系统状态。幂等。
        - 停止 Agent / 归零时间始终执行（即使记忆系统未初始化，也要兜底清理）。"""
        await AgentManager().aremove_all()         # 1. 停止并移除全部 Agent（始终执行）
        reset_llm_cache()                          # 2. 清 Agent LLM 缓存（agent_interuptible）
        await TimeSystem().areset()                # 3. 暂停并归零虚拟时间（始终执行）
        if not MemoryManager().is_initialized:
            print("[lifecycle] 记忆系统本就在零系统状态，跳过资源关闭")
            return
        await MemoryManager().close()              # 4. 关 MM（worker/graphiti/driver）
        await DBConnectionService().close()        # 5. 关 Kuzu 连接
        await EmbedderService().close()            # 6. 关 Embedder/Reranker（需补）
        print("[lifecycle] 已关闭全部系统，回到零系统状态")
```

- `enter_game` 由 `InitRequest` handler 调用；`leave_game` 由 `CloseRequest` handler 调用。
- **幂等**：`enter_game` 在已初始化时跳过（防重复 InitRequest）；`leave_game` 在未初始化时跳过资源关闭（但 Agent/时间清理始终执行）。
- 关闭顺序：先 Agent（停止推理）→ 清 LLM 缓存 → 归零时间 → 再 Memory → DB → Embedder（资源依赖自内而外）。

> `AgentLifecycle` 是**新模块**还是并入现有类，见 §7 待确认；倾向独立模块（架构职责清晰）。

### 3.0.1 TimeSystem 生命周期归属（已确认）

TimeSystem 是**本地虚拟时钟**（单例、进程内状态），不依赖 Key/Memory/Embedder，但与游戏生命周期绑定。盘点结果：

| 阶段 | 归属 | 动作 | 理由 |
|------|------|------|------|
| **进程启动** | 无（**移除** `main()` 的 `aset_time`） | 不设置 | 冗余：进游戏会完整设置并启动；且让 Title 阶段 TimeSystem 处于「半初始化」 |
| **进游戏** | `enter_game()` **设基准** + `SceneStart` **启动** | `enter_game`：`aset_time(2016,1,1)`（设虚拟时间基准，不启动）；`SceneStart`：`aset_speed(1440)` + `astart_time()` | Unity Flow 中 `CreateAgent`/`LoadAgent` 在 `SceneStart` 之前执行，需先有非 None 时间基准（否则 `EntityNode.created_at=None` 报错）；时钟启动仍在 SceneStart 统一终点 |
| **回 Title** | `leave_game()`（新增） | `TimeSystem().areset()`（暂停 + 归零） | 方案 X 后 ReturnToTitle 不再发 SceneStop，`apause_time` 不会被调用，必须由 `leave_game` 兜底归零 |
| **场景间清场** | `SceneStop`（现状，不动） | 仅 `apause_time()` | NewGame/ContinueGame 开头清场，随后 SceneStart 会重设，只需暂停不需归零 |

**关键点**：
- **时间「基准」在 `enter_game` 设置、时钟「启动」在 `SceneStart`**（2026-08-21 修正）：`aset_time` 只设基准（`virtual_time` 非 None，时钟不走动），`astart_time` 才真正启动时钟。这样创建/加载 Agent 时 `aget_current_time()` 能取到有效时间。
- `main()` 移除 `aset_time` 后，Title 阶段 `TimeSystem.virtual_time` 为 `None`（完全零状态）；进游戏由 `enter_game` 设基准 + `SceneStart` 启动，回 Title 由 `leave_game` 归零。

### 3.1 协议：`CloseRequest` / `CloseResponse`（新增）

复用 a 版 `InitRequest`/`InitResponse` 作为「进游戏」信号，新增「回 Title」信号：

```proto
// Tools/message.proto
message InitRequest { }        // 进游戏（现有，语义不变：全新初始化）
message InitResponse {
  bool success = 1;
  string errormsg = 2;
}
message CloseRequest { }       // 回 Title：关闭全部系统
message CloseResponse {
  bool success = 1;
  string errormsg = 2;
}
```

- `NetMessageRequest` oneof 新增 `closeRequest = 34`；`NetMessageResponse` 新增 `closeResponse = 11`。
- **协议修改流程（强制）**：`Tools/message.proto` → `1.genproto.cmd` → `MessageDispatch.cs` → Rebuild `CSharpClient.sln` → `2.copyprotocol.cmd`。**禁止**手改生成物。

### 3.2 Unity：进游戏 Flow 插入 `InitializeStep`

> 命名说明：本 Step 不只初始化「记忆」，还负责「注入最新 API 配置 + 确保记忆/技能等系统就绪」，故命名 `InitializeStep`（而非 `InitializeMemoryStep`）。

```csharp
// GameFlow/Steps/InitializeStep.cs（新增）
public class InitializeStep : IFlowStep
{
    public string DisplayName => "初始化系统";
    public async UniTask Execute()
    {
        await AgentServiceAsyncExtensions.InitAsync();
    }
}
```

| Flow | 步骤序列（改动） |
|------|------------------|
| **NewGameFlow** | StopAgent → **InitializeStep** → DeleteMemory → CreateAgent → Backup(0) → SaveData → LoadAgent → LoadScene → StartAgent(1) |
| **ContinueGameFlow** | StopAgent → **InitializeStep** → RestoreMemory(0) → LoadAgent → LoadScene → StartAgent(1) |

- `InitializeStep` 调 `InitAsync()`（a 版已有静态方法）→ 发 `InitRequest` → Python `AgentLifecycle.enter_game()`。
- **ContinueGameFlow 双场景（已确认方案 A）**：`ContinueGame()` 有 3 个调用点——Title「继续游戏」、`UIMenu.OnClickRetry`、`UI.OnClickRetry`（后两者为关卡内 Retry）。两种场景的差异仅在「是否已初始化」：
  - Title：零系统（未初始化）→ `InitializeStep` 执行全新初始化。
  - 关卡内 Retry：已初始化 → `enter_game()` 幂等跳过，直接走 Restore。
  - 因 `InitializeStep` 幂等，**同一 ContinueGameFlow 天然兼容两场景，无需拆分 RetryGameFlow**（Retry 语义上就是「从最近存档恢复当前关」= ContinueGame；拆分会产生一份几乎相同的 Flow，违背 DRY）。
- 与后续 `DeleteMemoryStep`/`RestoreMemoryStep` 内部自带的 close→initialize 兼容：`enter_game` 先 `load_api_config_into_env(force=True)` + `initialize()`，后续 Delete/Restore 再 close→re-init（此时 env 已是最新 Key），行为正确。

### 3.3 Unity：回 Title Flow 插入 `CloseStep`

> 命名说明：本 Step 关闭的也是**全部系统**（Agent/Memory/Embedder/LLM 缓存），与 `InitializeStep` 对称，命名 `CloseStep`。

```csharp
// GameFlow/Steps/CloseStep.cs（新增）
public class CloseStep : IFlowStep
{
    public string DisplayName => "关闭系统";
    public async UniTask Execute()
    {
        await AgentServiceAsyncExtensions.CloseAsync();
    }
}
```

| Flow | 步骤序列（改动） |
|------|------------------|
| **ReturnToTitleFlow** | **CloseStep** → LoadScene(title) |

- `CloseAsync()` 为 `AgentServiceAsyncExtensions` 新增静态方法（模式同 `StopSceneAsync`/`InitAsync`）→ 发 `CloseRequest` → Python `AgentLifecycle.leave_game()`。
- **`CloseStep` 取代原 `StopAgentStep`（已确认方案 X）**：`leave_game()` 内部**完整负责「停止 Agent + 关闭全部系统」**（见 §3.0），ReturnToTitle 不再需要单独的 `StopAgentStep`，避免两个 Step 职责重叠。`SceneStopRequest` 仍保留，用于 NewGame/ContinueGame 开头的清场（那些场景只需停止 Agent，不需关闭系统）。

### 3.4 Python：`main.py` handler 收敛

```python
@server.on_message(message_pb2.InitRequest)
async def handle_init_request(msg, context):
    response = message_pb2.InitResponse()
    try:
        await AgentLifecycle.enter_game()        # 幂等
        response.success = True
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
    await context['server'].send_message(response, context)

@server.on_message(message_pb2.CloseRequest)
async def handle_close_request(msg, context):
    response = message_pb2.CloseResponse()
    try:
        await AgentLifecycle.leave_game()        # 幂等
        response.success = True
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
    await context['server'].send_message(response, context)
```

- `InitRequest` 语义保持 a 版（读 json → 注入 → 初始化），但**逻辑收敛到 `AgentLifecycle.enter_game()`**。
- 未初始化保护（`AgentCreateRequest`/`AgentLoadRequest`/`SceneStartRequest` 的 `is_initialized` 检查）**保留**——Title 阶段零系统时这些请求应明确报错。
- `--auto-init` 保留调试用途：进程启动即 `enter_game()`（开发期快速联调）。
- **`main()` 移除 `TimeSystem().aset_time(...)`（原 365 行）**：`main()` 不再设置时间基准——Title 阶段 TimeSystem 完全零状态。时间基准由 `enter_game` 设置、时钟启动归属 `SceneStart`、归零归属 `leave_game`（见 §3.0.1）。

### 3.5 Python：补齐关闭能力

#### 3.5.1 `embedder_service.py` 补 `close()`

```python
# embedder_service.py（新增）
async def close(self) -> None:
    """释放 embedder/reranker，复位初始化状态（供 leave_game 调用）。"""
    self._embedder = None
    self._reranker = None
    self._initialized = False
    self._init_lock = None
    print("✅ [EmbedderService] closed")
```

> 当前 `EmbedderService` 无 `close`，是「回 Title 关闭」的关键缺口。补上后 `initialize()` 可再次执行（`_initialized` 已复位）。

#### 3.5.2 `agent_interuptible.py` 补 `reset_llm_cache()`

```python
# agent_interuptible.py（新增）
def reset_llm_cache():
    """清除模块级 LLM 缓存，使下次 get_llm_with_tools() 用最新 Key 重建。"""
    global _llm_with_tools
    _llm_with_tools = None
```

#### 3.5.3 `MemoryManager.close()` 已够用

`MemoryManager.close()`（870 行）已释放 worker/graphiti/driver 并复位 `_initialized`；`initialize()` 幂等可重建。无需新增 `reinitialize`。

#### 3.5.4 `time_system.py` 补 `areset()`

```python
# time_system.py（新增）
async def areset(self):
    """暂停并归零虚拟时间（供 leave_game 调用，回 Title 回到零时间状态）。"""
    async with self._lock:
        if self.running:
            # 先结算当前虚拟时间（与 apause_time 一致）
            current_real_time = asyncio.get_event_loop().time()
            elapsed_real = current_real_time - self.real_start_time
            self.virtual_time += timedelta(seconds=elapsed_real * self.speed)
        self.virtual_time = None
        self.real_start_time = None
        self.speed = 1.0
        self.running = False
        self.alarm_callbacks.clear()
    if self._task and not self._task.done():
        self._task.cancel()
        self._task = None
```

> `TimeSystem` 无 `reset` 方法，`leave_game` 需要归零（方案 X 后 ReturnToTitle 不再发 SceneStop，`apause_time` 不会被调用）。`areset()` 幂等：已零状态直接无副作用。

### 3.6 UITitle 拆分（Unity）

#### 3.6.1 `UISetting.cs`（新增，挂 UIConfig）

从 `UITitle` 迁移：

| 成员 | 来源（v0.23.0a UITitle） |
|------|------|
| 12 个 `[SerializeField] TMP_InputField` | `mAgentBaseInput` 等 |
| `ApiConfigStore.Load()/Save()` | 读写 |
| 回填 `RefreshInputsFromConfig()` | 打开面板时填充 |
| 变更检测 `HasConfigChanged()` | 退出子面板时比对 |
| `OnConfirmSaveConfig()` / `OnCancelSaveConfig()` | 保存/取消弹窗 |
| 完整性校验 `IsConfigReady()` | 入口拦截 |

对外接口：
```csharp
public bool IsConfigReady()          // 12 项非空 → true
public bool HasConfigChanged()       // 文本框 vs 当前配置
public void RefreshInputsFromConfig()
public void SaveConfig()             // 收集 → Save → 回填
```

#### 3.6.2 `UITitle.cs`（修改，仅留页面切换）

保留：`ShowPressAnyButton` / `ShowMainMenu` / `ShowConfig` / `SetSubPanelActive`（4 子面板）、ESC 分发与消抖、4 个弹窗（NewGame/SaveConfig/NoApiKey/Quit）开关。

移除：12 个 InputField、`ApiConfigStore` 相关、`HasConfigChanged`、`CollectInputsToConfig`、`RefreshInputsFromConfig`、`SendInitAndWait`。

保留与 `UISetting` 的交互：
- `UITitle.OnClickNewGame` → `if (!mSetting.IsConfigReady()) { mNoApiKeyMsgbox.SetActive(true); return; }` → `GameFlowManager.Instance.StartNewGame(...)`。
- `UITitle.ShowConfig` → 打开配置面板时 `mSetting.RefreshInputsFromConfig()`；退出子面板 ESC 时 `mSetting.HasConfigChanged()` 决定是否弹 `mSaveConfigMsgBox`。
- 场景中 `mSetting` 引用指向 UIConfig 上的 `UISetting`。

#### 3.6.3 场景绑定迁移（用户手动）

- `UITitle` 组件的 12 个 InputField 引用**移除**，绑定到 `UIConfig` 上的 `UISetting`。
- `UITitle.mSetting` 拖入 UIConfig 上的 `UISetting`。
- `MsgboxSaveConfig` 保存/取消按钮改挂 `UISetting.OnConfirmSaveConfig`/`OnCancelSaveConfig`。
- 详见 `场景绑定指引.md`（b 版更新）。

### 3.7 配置生效链路（b 版）

```
Title 保存 → ApiConfigStore.Save → Data/Config/api_config.json（明文）
   ↓
Python 启动（零系统，仅监听端口；开发期可 --auto-init）
   ↓
Title 点开始游戏 → UISetting.IsConfigReady() 校验 → 进 GameFlow
   ↓
NewGameFlow/ContinueGameFlow → InitializeStep → InitRequest
   → AgentLifecycle.enter_game()
       → load_api_config_into_env(force)  # 读最新 json
       → MemoryManager.initialize()       # 全新初始化（新 Key）
   ↓
DeleteMemory/Restore（内部 re-init 用新 Key）→ CreateAgent/LoadAgent → StartAgent
   ↓ 游戏进行中
游戏内点「回标题」→ ReturnToTitleFlow → CloseStep → CloseRequest
   → AgentLifecycle.leave_game()          # 停止 Agent + 时间归零 + close 全部，回零系统
   ↓
Title（再次零系统，TimeSystem 归零）→ 改 Key → 再进游戏 → 全新初始化 → 新 Key 生效
```

**改 Key 生效**：只要回 Title（自动 close）再进游戏（自动 init），即用最新 Key。**无需重启 Python 进程、无 reinitialize、无热更新**。

## 4. 实现步骤

1. 协议：`message.proto` 新增 `CloseRequest`/`CloseResponse`（field 34/11）；按流程生成分发。
2. Python `embedder_service.py`：补 `close()`。
3. Python `agent_interuptible.py`：补 `reset_llm_cache()`。
4. Python `time_system.py`：补 `areset()`（暂停 + 归零）。
5. Python 新增 `AgentLifecycle`（`enter_game`/`leave_game`，幂等；`leave_game` 含 Agent 停止 + LLM 缓存清理 + 时间归零 + 资源关闭）。
6. Python `main.py`：`InitRequest` handler 收敛到 `enter_game`；新增 `CloseRequest` handler 调 `leave_game`；**移除 `main()` 的 `aset_time`**；保留未初始化保护与 `--auto-init`（调试）。
7. Unity `AgentService.cs`/`AgentServiceAsyncExtensions.cs`/`MessageDispatch.cs`：接入 `CloseRequest`/`CloseResponse`；新增 `CloseAsync()`。
8. Unity 新增 `GameFlow/Steps/InitializeStep.cs`、`CloseStep.cs`。
9. Unity 修改 `NewGameFlow.cs`/`ContinueGameFlow.cs`（插 Init Step）、`ReturnToTitleFlow.cs`（CloseStep 取代 StopAgentStep）。
10. Unity 新增 `UISetting.cs`，从 `UITitle` 迁移配置读写。
11. Unity 修改 `UITitle.cs`（仅留页面切换，调用 `UISetting`）。
12. 场景绑定迁移（用户手动，依 `场景绑定指引.md`）。
13. 自测（见 §6），更新文档状态与 `打包方案.md` 改 Key 生效描述。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `EmbedderService` 无 close，回 Title 后旧 Key 残留 | 补 `close()`（§3.5.1），`leave_game` 统一关闭 |
| Agent LLM 缓存（`_llm_with_tools`）跨进出残留 | 补 `reset_llm_cache()`（§3.5.2），`leave_game` 调用 |
| 关闭顺序错误导致 Kuzu 文件锁残留 | `leave_game` 严格顺序：Agent → LLM 缓存 → Memory → DB → Embedder；复用 backup/restore/delete 已验证 close 链 |
| Flow 内 Init 与 Delete/Restore 重复初始化 | `enter_game` 幂等短路；Delete/Restore 内部 re-init 用注入后新 Key，行为正确 |
| `CloseRequest` 重复到达 | `leave_game` 在未初始化时跳过资源关闭（幂等），但 Agent/时间清理始终执行 |
| 回 Title 后虚拟时间仍在跑 | `leave_game` 调 `TimeSystem().areset()`（方案 X 后 ReturnToTitle 不再发 SceneStop，必须由 leave_game 归零） |
| `main()` 移除 aset_time 后 Title 阶段读时间为 None | 预期行为（零系统）；进游戏由 `enter_game` 设时间基准 + `SceneStart` 启动，Title 阶段不应有读时间请求 |
| Title 场景残留 `SendInitAndWait` | `UITitle` 移除，入口只做校验 |
| 12 个 InputField 引用迁移遗漏 | 场景绑定指引逐项列出，用户手动核对 |
| 切关 NextMapFlow 误触发关闭 | 方案明确 NextMapFlow 不插任何生命周期 Step，保持运行 |

回退：还原 `message.proto` 与生成物（`closeRequest=34`/`closeResponse=11` 编号预留可平滑回滚）；还原 `main.py`/`agent_interuptible.py`/`embedder_service.py`；移除 `AgentLifecycle` 模块；移除 `InitializeStep.cs`/`CloseStep.cs` 与 Flow 插入；恢复 `UITitle` 或移除 `UISetting` 并还原场景绑定。

## 6. 测试建议

**Python 侧（可自测）**：
1. 单测 `EmbedderService.close()`：close 后 `_initialized=False`，再 initialize 成功。
2. 单测 `reset_llm_cache()`：调用后 `_llm_with_tools is None`。
3. 单测 `TimeSystem.areset()`：运行中 → 归零（`virtual_time=None`/`running=False`/`speed=1.0`/`alarm_callbacks` 清空）；已零 → 无副作用。
4. 单测 `AgentLifecycle.enter_game`：未初始化 → 注入 + 初始化；已初始化 → 幂等跳过。
5. 单测 `AgentLifecycle.leave_game`：已初始化 → Agent 停止 + 时间归零 + close 全部 → 零系统；未初始化 → Agent/时间清理仍执行、资源关闭跳过。
6. 端到端：`enter_game`（用 json Key）→ 改 `api_config.json` → `leave_game`（确认时间归零）→ 再 `enter_game` → 确认用新 Key（构造参数/日志）。

**Unity 侧（需编辑器/Play）**：
1. Title 改 Key 保存 → 点开始 → 进 NewGame/ContinueGame → Python 日志确认初始化。
2. 游戏内回标题 → Python 日志确认 close（含时间归零）；再进游戏 → 确认重新初始化且用最新 Key、虚拟时间从 2016-01-01 重新开始。
3. 12 项不全点开始 → 弹 MsgboxNoApiKey 不进 GameFlow；补全后进。
4. 打开配置面板回填正确；改值退出弹 MsgboxSaveConfig；保存落盘。
5. 切关 NextMapFlow 不触发关闭，记忆系统保持运行、虚拟时间继续走。

**验收标志**：改 Key 后无需重启 Python，回 Title 再进游戏即用新 Key（架构上是「零系统 → 全新 init」，非热更新）；Title 阶段无任何已初始化系统（含 TimeSystem 归零）；切关保持运行；`UITitle` 无配置读写代码。

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-20 | 方案生成（生命周期重构版），待用户确认。 |
| 2026-08-20 | 用户确认方案并开始开发；Python 侧全部实现（AgentLifecycle 移至 `lifecycle/lifecycle.py`）+ Unity 侧实现；Python 自测通过（reset_llm_cache / TimeSystem.areset / EmbedderService.close / 生命周期端到端 / force 覆盖改 Key 生效）；协议已生成并部署 DLL 到 Unity。Unity 编辑器联调与场景绑定待用户。 |
| 2026-08-21 | Unity 联调修复：① `UISetting.Awake` 关闭 12 个输入框 `restoreOriginalTextOnEscape`，解决「改值 ESC 被 TMP 还原导致不弹保存确认 / 模型名不显示 / 只存第一个」；② `ApiConfig` 12 字段名改为与 Python `API_CONFIG_KEYS` 一致的大写键（`AGENT_API_BASE` 等），`JsonUtility` 序列化即 Python 可读格式，并迁移旧小写 `api_config.json`（备份 `.bak`）。实测 `load_api_config_into_env(force=True)` 注入 11 项成功。详见 `场景绑定指引.md` §9。 |
| 2026-08-21 | 修复 TimeSystem 时序回归：`enter_game` 现 `aset_time(2016,1,1)` 设虚拟时间基准（不启动），`SceneStart` 仅 `aset_speed(1440)` + `astart_time()`。原因是 Unity Flow 中 `CreateAgent`/`LoadAgent` 在 `SceneStart` 之前执行，需先有非 None 时间基准（否则 `EntityNode.created_at=None` 报错）。已同步更新 `DevDocs/Architecture/生命周期架构.md` §2.2。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
