# AGENTS.md

本文件是本项目的主导航文档：从整体架构出发，说明 Brain（Python）与 Environment（Unity）如何协作，并深入 `agent_interuptible.py`、`memory_manager.py` 两个核心模块。面向 Cursor Agent 与人类开发者。

---

## 一、总体架构

### 1.1 项目定位

**独立智能体异步通信架构**：在虚拟世界中运行可长期记忆、可被打断、可调用工具的 LangGraph Agent。Python 负责「大脑」，Unity 负责「身体与世界」，二者经 TCP + Protobuf 异步通信。

核心设计目标：

- **异步**：Agent 推理与 Unity 动作并行；长时工具结果通过反馈通道回补，不阻塞世界仿真。
- **可打断**：新消息/环境反馈可中断当前 LLM 或工具链，保留队列与记忆线索后恢复。
- **持久记忆**：对话片段经 Graphiti 写入 Kuzu 图库，支持事实/情景检索与热备份读档。
- **协议驱动**：跨语言边界仅通过 `Tools/message.proto` 定义的 Request/Response 交互。

### 1.2 三层结构

```
┌─────────────────────────────────────────────────────────────────┐
│  Layer 1 — Brain（PythonServer）                                 │
│  main.py · AgentManager · agent_interuptible.Agent               │
│  LangGraph · MemoryManager(Graphiti/Kuzu) · TimeSystem           │
└────────────────────────────┬────────────────────────────────────┘
                             │ TCP，4字节小端长度 + NetMessage
┌────────────────────────────▼────────────────────────────────────┐
│  Layer 2 — Bridge（协议与分发）                                   │
│  message.proto · message_pb2/message.cs · MessageDispatch        │
│  AgentServerNetMessage · AgentService · TOOL_WAITERS             │
└────────────────────────────┬────────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────────┐
│  Layer 3 — Environment（Unity IndependentAgentProject）          │
│  GameFlow · AIPlayer · SceneObjManager · ActionSequence          │
└─────────────────────────────────────────────────────────────────┘
```

### 1.3 三条通信平面

三类消息**不要混用同一套回调机制**：

| 平面 | 方向 | 典型 Proto | Python 入口 | Unity 入口 | 完成信号 |
|------|------|-----------|-------------|------------|----------|
| **生命周期** | Unity → Python → Unity | `AgentCreateRequest`、`SceneStartRequest`、`MemoryBackupRequest`… | `main.py` `@server.on_message` | `GameFlow` Step → `AgentServiceAsyncExtensions` | `NetMessageResponse`（`OnCreateAgent` 等事件） |
| **用户/环境消息** | Unity → Python | `UserSendMessageRequest`、`UserSendFeedbackRequest` | `handle_user_send_*` → `Agent.asend_message/feedback` | `AIPlayer.SendMessageToAgent` / 工具反馈 | 无 Response；入 Agent 双队列 |
| **工具 RPC** | Python → Unity → Python | `AgentObserveRequest`、`AgentMoveRequest`… | `base_tools.*_cmd` + `TOOL_WAITERS` | `AgentService` → `AIPlayer` | `SendToolResultMessageRequest` 唤醒 Future |

```
生命周期：  Unity GameFlow ──Request──► Python main ──Response──► Unity（UniTask 完成）
用户消息：  Unity AIPlayer ──UserSend*──► Python Agent 队列 ──► LangGraph 推理
工具 RPC：  Python tool ──AgentXxx──► Unity AIPlayer ──ToolResult──► Python TOOL_WAITERS
```

### 1.4 数据与状态存储

| 存储 | 位置 | 内容 | 生命周期 |
|------|------|------|----------|
| **Kuzu 图库** | `Src/PythonServer/db/graphiti.kuzu` | Agent 简介实体、Episode、事实边、向量/FTS 索引 | 跨会话；可 backup/restore/delete |
| **记忆备份槽** | `db/backups/slot_{n}/` | `graphiti.kuzu` + `.wal` 文件拷贝 | GameFlow `BackupMemoryStep` / `RestoreMemoryStep` |
| **LangGraph checkpoint** | 进程内 `MemorySaver` | 当前对话 messages、`mem_to_save` 等 State | 打断时 fork 新 thread；`afinish` 清空 |
| **Agent 运行时** | `Agent.message_queue` / `feedback_queue` | 待处理的用户消息与环境反馈 | `afinish` 清空；打断时保留 |
| **Unity 存档元数据** | `SaveManager` | 关卡名等 | 本地；与 Python 记忆备份独立 |
| **共享端口** | `Src/Data/Config/agent_server_port.txt` | Python 监听端口 | 跨进程 |

**Agent 身份隔离**：每个 Agent 名 `name` 对应 `group_id = name.encode('utf-8').hex()`，Graphiti/Kuzu 内所有节点与检索均按 `group_id` 分区。

### 1.5 典型运行时序

**续玩（含记忆恢复 + 启动推理）**：

```
Unity ContinueGameFlow
  → SceneStop（Python: 停 Agent、停时间）
  → MemoryRestore(slot=0)（Python: 替换 kuzu 文件）
  → AgentLoad（Python: aload_agent_all，实例化 Agent 对象）
  → LoadScene（Unity 本地）
  → SceneStart(map_id=1)（Python: 启时间、astart_all 启动推理循环）

用户与场景交互
  → AIPlayer.SendMessageToAgent → UserSendMessageRequest
  → Agent.aprocess_message → LangGraph → tools → Unity AIPlayer
  → SendToolResult / SendUserFeedback → 继续推理

切关 NextMapFlow
  → AgentInterrupt → BackupMemory → LoadScene → BroadcastMessage → SceneStart
```

**新游戏**注意：当前 `NewGameFlow` **没有** `StartAgentStep`，创建 Agent 后不会自动 `SceneStart`；需用户发消息或其它 Flow 触发推理。

### 1.6 仓库地图（简）

```
independent-agent-project/
├── AGENTS.md
├── Doc/                          # 工具开发、ActionSequence、存档方案
├── Tools/message.proto           # 协议唯一权威源（禁止改 Src/Lib/proto/）
├── .cursor/skills/
└── Src/
    ├── PythonServer/             # Brain：main.py、agent_framwork、memory_system
    ├── IndependentAgentProject/  # Environment：Unity 2021.3.8f1c1
    ├── Lib/AgentProtocol+Common  # 生成协议 + MessageDispatch
    ├── CSharpClient/             # 无 Unity 协议联调
    └── Data/Config/              # agent_server_port.txt
```

遗留无关：`Src/GameServer/`（极世界教学）、`ShootingEditor2D` 旧场景。

---

## 二、`agent_interuptible.py` — 生产 Agent 核心

**路径**：`Src/PythonServer/agent_framwork/agents/agent_interuptible.py`  
**引用**：`agent_framwork/managers/agent_manager.py` 中 `from agent_interuptible import Agent`（`agent.py` / `agent_with_mem.py` 为旧版/试验）。

本文件同时定义：**LangGraph 图**、**全局 tools 列表**、**Agent 运行时类**（消息循环、打断、恢复）。

### 2.1 LangGraph 图结构

```
START
  → search_memory      # RAG + 缓存本轮输入到 mem_to_save
  → chatbot            # LLM（bind_tools）
  → [有 tool_calls?]
        ├─ tools       # ToolNode 执行 base_tools
        │    → cache_tool_mem   # 把 tool 调用记入 mem_to_save
        │    → chatbot          # 工具结果回到 LLM，可继续调工具
        └─ save_memory          # 无 tool_calls：异步写入 Graphiti
              → END
```

**设计要点**：

- 每轮用户输入**必定**先 `search_memory` 再 `chatbot`，把简介 + 事实 + 情景注入 system prompt。
- 只有「模型不再调工具」时才 `save_memory`；多步 tool 循环在 `tools → cache_tool_mem → chatbot` 中完成。
- `save_memory` 调用 `MemoryManager.save_memory`（入队，**不阻塞**图执行）。

### 2.2 State 字段

| 字段 | 含义 |
|------|------|
| `name` | Agent 名，与 Unity `AIPlayer.Name`、Kuzu `group_id` 对应 |
| `messages` | LangChain 消息列表（`add_messages` 累加） |
| `mem_summary` | 从 Kuzu 加载的 Agent 简介（`Entity name="I"`） |
| `mem_fact` | 本轮 RAG 检索到的事实记忆 |
| `mem_episode` | 本轮 RAG 检索到的情景记忆 |
| `mem_to_save` | 本轮待写入 Graphiti 的文本缓冲（输入、心理活动、工具调用） |
| `logged_tool_call_ids` | 已记入 `mem_to_save` 的 tool_call id，防重试重复 |

### 2.3 Prompt 与行为规则

System 模板注入：`mem_summary`、虚拟时间 `curtime`、`mem_fact`、`mem_episode`。

关键规则（写进 prompt）：

1. **直接回复 = 心理活动**，外界看不到，不产生任何影响。
2. **只有调用工具**才能影响环境（移动、观察、发消息等）。
3. 与他人交流必须用 `communicate_to_user` 等工具。

上下文截断：历史实现有 `_filter_messages(k=20)`，当前 `chatbot` 节点直接传完整 `state['messages']`（`MAX_CONTEXT_SIZE=20` 常量保留但未在 chatbot 路径启用）。

### 2.4 各节点职责

**`search_memory`**：以**最后一条消息内容**为 query，`limit=1` 检索事实与情景；将用户输入写入 `mem_to_save` 并打时间戳「我开始注意到上述信息」。

**`chatbot`**：组装 prompt → `llm_with_tools.ainvoke`；若有文本 content，追加「我心想: …」到 `mem_to_save`。

**`cache_tool_mem`**：遍历本轮 AI `tool_calls`，把「我使用了 xxx，输入为 …」写入 `mem_to_save`（按 `tool_call_id` 去重）。

**`save_memory`**：`memory_manager.save_memory(name, mem_to_save, curtime)` 异步入队；清空 `mem_to_save` 与 `logged_tool_call_ids`。

### 2.5 `Agent` 类：运行时模型

每个 Python `Agent` 实例是**长期驻留的异步任务**，与 LangGraph 图编译结果绑定。

#### 核心字段

| 成员 | 作用 |
|------|------|
| `message_queue` | 用户/管理员消息（`asend_message`） |
| `feedback_queue` | 环境反馈（`asend_feedback`；移动完成、定时器到期等） |
| `runtime_state["focus_state"]` | 专注模式；专注时普通消息不打断 |
| `graph` + `memory` (`MemorySaver`) | LangGraph 编译图与 checkpoint |
| `config["configurable"]` | `thread_id`、`message_queue`、`feedback_queue`、`runtime_state` 注入工具 |
| `_interrupt_event` | 打断信号 |
| `_resume_state` | 打断后待恢复的 graph state |

#### `aprocess_message` 主循环

1. 同时 `wait` 两个队列 + `_interrupt_event`。
2. 取出先到的一条，再 **drain 合并** 两队列所有积压，按时间戳排序拼成一条 `HumanMessage`。
3. `graph.ainvoke(input_state, config)` 跑完整图（可能多轮 tool）。
4. 出错时 `input_state = None`，依赖 checkpoint 从断点重试。
5. `_interrupt_event` 置位则退出循环。

#### 消息入口 `asend_message` / `asend_feedback`

```
记录消息时间 → 5秒内≥5条则 force focus_state=True
判断是否打断：
  force_interrupt / is_feedback / 非专注状态 → ainterrupt()
入队（附带虚拟时间戳前缀）
若已打断 → astart() 重启 aprocess_message
```

**反馈消息**（`is_feedback=True`）**总是打断**；用户消息在专注状态下可排队不打断。

#### 打断 `ainterrupt`

1. 置 `_interrupt_event`，cancel `_invoke_task`，等待 `_process_task` 退出。
2. 读 checkpoint，若存在**未完成的 tool_calls**，截断 messages 到最后一个未完成 AI tool 消息之前。
3. 把 `mem_to_save`、messages 等写入 `_resume_state`（附「当前思考被中断：reason」）。
4. **fork 新 lineage**：新建 `MemorySaver` + 新 `session_id`（清空 LangGraph 对话 checkpoint，但保留 `_resume_state` 供下次 `astart` 注入）。

#### 启动 / 结束

- **`astart`**：若 `_resume_state` 非空则 `aupdate_state` 恢复；启动 `aprocess_message` 任务。
- **`afinish`**：打断运行、清空双队列、重置 checkpoint 与 `_resume_state`（`AgentManager.aremove_agent` / `SceneStop` 时调用）。

### 2.6 与 Unity 的衔接

| Unity 行为 | Python 路径 |
|------------|-------------|
| `SendUserMessage` | `main.py` → `AgentManager.asend_message` → `message_queue` |
| `SendUserFeedback` | `main.py` → `AgentManager.asend_feedback` → `feedback_queue` |
| `SendToolResultMessage` | `main.py` → `TOOL_WAITERS[request_id].set_result`（**不进 Agent 队列**） |
| `SceneStart` | `AgentManager.astart_all()` → 各 Agent `astart()` |
| `SceneStop` / `AgentInterrupt` | `ainterrupt_all` 或 `aremove_all` |

Unity 反馈消息通常带完整环境快照（`<你的状态>` `<环境>` 等），由 `AIPlayer.CreateMessageText` 拼接。

### 2.7 生产工具列表

定义于本文件 `tools = [...]`，经 `llm_with_tools = model.bind_tools(tools)` 绑定。

当前启用：`communicate_to_user`、`get_cur_time`、`set_focus_state`、`search_fact_memories`、`search_episode_memories`、全套 `*_cmd`（观察/移动/交互/动作序列/定时器等）。  
本地工具（不经 Unity）：`get_cur_time`、`set_focus_state`、记忆检索；闹钟 `add_alarm` 等默认注释。

**改工具**：实现放 `base_tools.py`，注册改本文件 `tools` 列表。

---

## 三、`memory_manager.py` — 长期记忆子系统

**路径**：`Src/PythonServer/memory_system/memory_manager.py`  
**单例**：`MemoryManager()`，在 `main.py` 启动时 `await initialize()`。

基于 **Graphiti**（图记忆框架）+ **Kuzu**（嵌入式图数据库），独立配置 LLM（`.env` 中 `MEMORY_*`）。Embedding/Reranker 与 Kuzu 连接均**不再由 MM 直接持有**，分别从 `EmbedderService` 与 `DBConnectionService` 取（详见 §3.0）。

### 3.0 底层基础设施（v0.21.0 hotfix 引入）

| 单例 | 文件 | 职责 |
|------|------|------|
| `DBConnectionService` | `db_conn/db_connection_service.py` | Kuzu Database / AsyncConnection 生命周期、FTS 加载、冻结门（`access()` / `freeze()` / `wait_idle()`）、文件路径 |
| `EmbedderService` | `embedder/embedder_service.py` | `SafeBatchOpenAIEmbedder` / `SafeBatchOpenAIReranker`（基于 `.env` 中 `EMBEDDING_*` / `RERANKER_*` 配置） |

启动顺序由 `main.py` 编排：`DBConnectionService` → `EmbedderService` → `asyncio.gather(MemoryManager.initialize, ActionSkillManager.initialize)`。`MemoryManager` 与 `ActionSkillManager` 是**平级业务模块**，不再有相互的初始化依赖。所有 Cypher 都通过 `DBConnectionService().get_conn().execute(...)` 取连接，不在业务模块内部存 `self.conn` / `self._conn` 引用，从根本上避免 close 时引用残留导致的文件锁不释放问题。

### 3.1 记忆模型

| 类型 | 存储形式 | 写入时机 | 读取方式 |
|------|----------|----------|----------|
| **Agent 简介** | Kuzu `Entity(name="I", group_id=…)` 的 `summary` | `CreateAgent` → `init_agent_summary` | 每轮 `load_agent_summary` → prompt |
| **对话 Episode** | Graphiti `add_episode` | 每轮图结束 `save_memory` | `search_episode_memory`（Cypher 查 `Episodic`） |
| **事实边** | Graphiti 自动从 Episode 抽取的 `edges` | 同上 | `search_fact_memory`（`COMBINED_HYBRID_SEARCH_RRF`） |

`mem_to_save` 是一轮推理内累积的**原始文本**（用户输入 + 心理活动 + 工具记录），作为单个 Episode 正文写入；Graphiti 再异步抽取实体与事实边。

### 3.2 初始化 `initialize()`

1. 打开 `db/graphiti.kuzu`（失败则尝试删 `.wal` 重开）。
2. 加载 Kuzu **FTS 扩展**；`setup_schema` + `build_indices_and_constraints`。
3. 组装 `Graphiti`（共享 `_SharedKuzuDriver` 避免重复打开库）。
4. 启动 **`_memory_worker`** 后台协程，消费 `_memory_queue`。

### 3.3 写入路径

```
save_memory(name, memory, curtime)        # 图节点 save_memory 调用
  → _wait_if_frozen()                     # backup 期间拒绝入队
  → _memory_queue.put((name, memory, curtime))
  → _memory_worker 取出
  → _save_memory → graphiti.add_episode(...)
```

- 单 Episode 上限 **8000** 字符，超出截断。
- `_graph_write_lock` 串行化写图；主键冲突自动重试 3 次。
- 写入**不阻塞** LangGraph；Agent 侧 `save_memory` 节点立即返回。

`init_agent_summary`：创建 `EntityNode(I)` + 用 summary 文本跑一次 `add_episode` 再**删除 episode 节点**（只保留实体与衍生关系）。

### 3.4 检索路径

**事实** `search_fact_memory`：`graphiti._search` + `COMBINED_HYBRID_SEARCH_RRF`，输出「事物」实体摘要 + 「事实」边列表（含 valid/invalid 时间）。

**情景** `search_episode_memory`：先用 query 搜 edge 关联的 episode uuid，再 Cypher 查 `Episodic`（支持 `start_time` / `end_time`）；也可被 `search_episode_memories` **工具**在对话中按需调用（与每轮自动 RAG 独立）。

每轮 `search_memory` 节点默认 `limit=1`，只取最相关的一条事实/情景注入 prompt。

### 3.5 并发与备份控制

| 机制 | 作用 |
|------|------|
| `memory_access()` 上下文 | 普通读/写前 `_begin_memory_op`；backup 时 `_freeze=True` 阻塞新操作 |
| `_active_ops` + `_active_cond` | 等待进行中的 memory 操作结束再 checkpoint |
| `_backup_lock` | 串行化 backup / restore / delete |
| `_freeze` | backup/close 期间禁止新 `memory_access` 与入队 |

### 3.6 存档 API（与 GameFlow 对应）

| 方法 | GameFlow Step | 行为 |
|------|---------------|------|
| `backup_memory(slot_id)` | `BackupMemoryStep` | freeze → flush 队列 → checkpoint → 关闭库 → 复制 kuzu+wal 到 `db/backups/slot_n` → 重新 initialize |
| `restore_memory(slot_id)` | `RestoreMemoryStep` | 关闭库 → 用备份覆盖当前 db → initialize |
| `delete_current_memory()` | `DeleteMemoryStep` | 关闭库 → 删 db+wal → initialize（**新游戏清空记忆**） |
| `delete_backup_memory(slot)` | （无对应 Step） | 删指定槽位目录 |
| `wait_memory_flush(timeout)` | backup 前内部调用 | 等待 worker 队列清空 |

槽位范围：`[0, MAX_BACKUP_SLOTS-1]`，默认 `MAX_BACKUP_SLOTS=10`。当前 Flow **硬编码 slot=0**。

### 3.7 改记忆系统时注意

1. **group_id** 必须与 Agent `name` 的 utf-8 hex 一致，否则检索不到。
2. backup 期间不要假设 `graphiti` 连接可用；GameFlow 应先 `StopAgent` 再 restore。
3. Kuzu 文件锁敏感：`close()` 必须走完 worker 取消 + `gc.collect()` 链（见 `Doc/kuzu被文件锁时处理办法.md`）。
4. 记忆 LLM 与 Agent LLM **可分离配置**（不同 API / 模型）。

---

## 四、Unity GameFlow（生命周期 → Python）

编排器：`GameFlowManager` → `FlowExecutor` → `IFlowStep`；与 Python 通信用 `AgentServiceAsyncExtensions`（Request → 等 Response 事件）。

**命名区分**：Unity `Chara/AgentManager` 路由**工具**到 `AIPlayer`；Python `agent_manager.py` 管理 **LangGraph Agent 实例**。GameFlow 打的是后者。

### 4.1 会请求 Python 的 Step

| Step | Proto Request | Python 处理 | 效果 |
|------|---------------|-------------|------|
| `StopAgentStep` | `SceneStopRequest` | `handle_scene_stop_request` | 停时间；`aremove_all()` |
| `DeleteMemoryStep` | `MemoryDeleteCurrentRequest` | `handle_memory_delete_request` | `delete_current_memory()` |
| `CreateAgentStep` | `AgentCreateRequest` | `handle_agent_create_request` | `acreate_agent` + `init_agent_summary` |
| `BackupMemoryStep` | `MemoryBackupRequest` | `handle_memory_backup_request` | `backup_memory(slot)` |
| `RestoreMemoryStep` | `MemoryRestoreRequest` | `handle_memory_restore_request` | `restore_memory(slot)` |
| `LoadAgentStep` | `AgentLoadRequest` | `handle_agent_load_request` | `aload_agent_all()` |
| `StartAgentStep` | `SceneStartRequest` | `handle_scene_start_request` | 启时间；`astart_all()` |
| `InterruptAgentStep` | `AgentInterruptRequest` | `handle_agent_interrupt_request` | `ainterrupt_all(reason)` |

**纯 Unity**：`LoadSceneStep`、`SaveDataStep`。  
**间接 Python**：`BroadcastMessageToAgentsStep` → 各 `AIPlayer.SendMessageToAgent` → `UserSendMessageRequest`。

### 4.2 四条 Flow

| Flow | 步骤概要 | 备注 |
|------|----------|------|
| **NewGameFlow** | Stop → DeleteMemory → CreateAgent → Backup(0) → SaveData → LoadAgent → LoadScene | **无 StartAgent** |
| **ContinueGameFlow** | Stop → Restore(0) → LoadAgent → LoadScene → **Start(1)** | 续玩标准路径 |
| **NextMapFlow** | Interrupt → Backup(0) → SaveData → LoadScene → SaveData → Broadcast → **Start(0)** | 切关 |
| **ReturnToTitleFlow** | Stop → LoadScene(title) | 回标题 |

生命周期 Response 在 `MessageDispatch.cs` 的 **`NetMessageResponse`** 分支分发。

---

## 五、工具与协议（速查）

### 5.1 协议修改（强制）

只改 `Tools/message.proto` → `1.genproto.cmd` → `MessageDispatch.cs` → Rebuild `CSharpClient.sln` → `2.copyprotocol.cmd`。  
**禁止**手改 `message_pb2.py` / `message.cs`；**禁止**参考 `Src/Lib/proto/`。

### 5.2 新增 `_cmd` 工具

`message.proto` → `MessageDispatch` → `base_tools.py` → `agent_interuptible.tools` → `AgentService` → Unity `AgentManager` → `AIPlayer`。  
详见 `Doc/Agent工具开发流程.md`、Skill `.cursor/skills/develop-agent-tool/`。

### 5.3 新增 ActionSequence Action 类型

改 `ActionStep.oneof` + `action.py` + `build_pb_action_step` + `AIPlayer.ExecuteXxxAction`；通常**不需**新 Request。  
详见 `Doc/ActionSequence开发流程.md`。

---

## 六、关键文件索引

| 模块 | 文件 | 职责 |
|------|------|------|
| 入口 | `main.py` | TCP 服务、生命周期/记忆/用户消息/工具回调 |
| Agent 图 | `agent_framwork/agents/agent_interuptible.py` | LangGraph、Agent 类、tools 列表 |
| Agent 池 | `agent_framwork/managers/agent_manager.py` | 创建/加载/启停/广播消息 |
| 记忆 | `memory_system/memory_manager.py` | Graphiti、记忆 RAG、备份/恢复编排 |
| 技能 | `action_skill_system/action_skill_manager.py` | ActionSkill / ActionSequenceTemplate CRUD + RAG |
| **DB 连接** | `db_conn/db_connection_service.py` | Kuzu Database / Conn / FTS / 冻结门（v0.21.0 引入） |
| **Embedder** | `embedder/embedder_service.py` | 共享 OpenAI Embedder / Reranker（v0.21.0 引入） |
| 工具 | `agent_framwork/tools/base_tools.py` | LangChain 工具实现 |
| 网络 | `network/servers.py` | `AgentServerNetMessage`、`TOOL_WAITERS` |
| Unity 身体 | `AIPlayer.cs` | 工具执行、环境反馈 |
| Unity 流程 | `GameFlow/` | 生命周期 Step |
| 协议 | `Tools/message.proto` | 全部消息定义 |

---

## 七、本地运行

```bash
cd Src/PythonServer && uv sync
cd Src/PythonServer && uv run python main.py   # 先启动
# Unity Play；读取 Src/Data/Config/agent_server_port.txt
```

`.env`：`AGENT_API_*`（对话）、`MEMORY_*` / `EMBEDDING_*` / `RERANKER_*`（记忆）。

### Unity MCP（Cursor ↔ Unity 编辑器）

Cursor 可通过 **MCP** 直接读取/操作 Unity 编辑器（基于 CoplayDev/unity-mcp，HTTP 传输），**不走**运行期游戏内 TCP+Protobuf 链路，是编辑器/开发期工具。

- **配置**：全局 `~/.cursor/mcp.json` 已注册：
  ```json
  { "mcpServers": { "unityMCP": { "url": "http://127.0.0.1:8080/mcp", "type": "http" } } }
  ```
- **前提**：Unity 内 `Window > MCP for Unity` 的 **Start Bridge** 必须在运行。重新打开 Cursor 前先启动 Bridge，否则认证超时、工具不可用（连接失败时命名空间状态为 `error`，可用 `mcp_auth` 认证重试）。
- **连接名**：`user-unityMCP`（认证成功后可用）。当前 Unity 实例：`IndependentAgentProject`（2021.3.8f1c1）。
- **用法**：
  - `GetDynamicTools` 列出工具；`FetchMcpResource` 读 `mcpforunity://...` 资源（`editor/state`、`project/info`、`instances` 等）；
  - `CallDynamicTool` 调用工具：`manage_scene`、`manage_gameobject`、`manage_asset`、`apply_text_edits`、`batch_execute`、`read_console`、`unity_reflect`、`unity_docs` 等（改脚本后先 `read_console` 检查编译错误）。
- **定位**：MCP 用于**编辑器侧**开发（查场景层级、检查编译、增删 GameObject、验证 Unity API），与运行期 Agent 工具链路（§1.3 工具 RPC）无关。

---

## 八、文档、Skills 与约定

| 资源 | 路径 |
|------|------|
| **版本开发文档（需求→PRD→方案）** | **`DevDocs/`**（说明见 `DevDocs/README.md`） |
| **架构梳理文档（跨版本，生命周期等）** | **`DevDocs/Architecture/`** |
| 工具开发 | `Doc/Agent工具开发流程.md` |
| ActionSequence | `Doc/ActionSequence开发流程.md` |
| 存档方案 | `Doc/存档方案.md` |
| **项目编码基线（v0.22.0 起）** | **`DevDocs/feature-design/项目编码基线.md`** |
| Cursor Skill | `.cursor/skills/develop-agent-tool/`（PythonServer 工作区副本：`Src/PythonServer/.cursor/skills/`） |

### DevDocs 协作约定

1. 每个小版本在 `DevDocs/vX.Y/` 建目录；**你**把需求放在 `requirements/`。
2. Agent **先读 requirements**，在同目录生成 `PRD.md`、`solution.md`，**等你确认后再开发**。
3. `Doc/` 放跨版本技术指南；`DevDocs/` 放按版本归档的需求与方案。
4. 模板与流程细节：`.cursor/rules/dev-docs-workflow.mdc`、`DevDocs/_template/`。
5. **文档状态必须随开发环节同步更新**：
   - PRD：`待确认` → `已确认`（用户确认后）
   - 方案：`待确认` → `已确认`（用户确认后） → `已实现`（验收通过后）
   - 每次更新状态时同步更新「最后更新」日期
   - 完整规则见 `DevDocs/README.md`

**Skill 用法**：Agent 聊天输入 `/develop-agent-tool` 或 `@develop-agent-tool`；找不到时检查工作区根目录并重载窗口。

**已确认约定**：

0. **架构优先原则（最高优先级）**：每个版本**不以「最小改动」作为目标/约束**，永远以**架构最干净、最符合项目长期发展**的实现方式为目标。哪怕是彻底重构也要避免架构腐化。当「改得少」与「架构好」冲突时，**无条件选择架构好**。

1. 生产 Agent 仅 `agent_interuptible.py`
2. `Src/GameServer/` 与 Agent 链路无关
3. 协议只认 `Tools/message.proto`；`Src/Lib/proto/` 完全不用管
4. 与用户交流默认简体中文
5. 不主动 git commit / push，除非用户明确要求
6. **`agent_framwork/` 不放业务代码**：`agent_framwork` 的设计目标是可复用到不同项目的通用框架。业务逻辑（如特定技能系统、特定工具实现等）应放在独立目录（如 `action_skill_system/`）中，通过接口与框架对接。后续会逐步将 `agent_framwork` 中已有的业务代码解耦分出。

### 开发纪律

**事件过滤 / 状态判断类逻辑必须先做完整场景枚举**：

当需要实现「哪些事件应该写入、哪些应该跳过」等过滤逻辑时，**禁止**直接写代码。必须：

1. **枚举所有触发路径**，逐一标注期望行为，形成表格（触发场景 → 期望 → 理由）。
2. **验证过滤条件对每条路径的判定结果**，确认无遗漏。
3. **方案经用户确认后才写代码**。

违反此纪律曾导致 v0.20.10 开发事故：用「是否曾消失过」过滤首次 Appearance，遗漏了「新角色动态入场」也是首次 Appearance 的场景，导致新入场事件被错误跳过。

**修复 / 变更方案必须先确认再执行**：

运行时发现 bug 或需要修复时，**禁止**未经用户确认直接修改代码。必须：

1. **分析根因**，提出修复方案（含回滚方案）。
2. **等待用户确认**后再执行。
3. 修复完成后更新相关文档状态。

违反此纪律曾导致 v0.20.12 开发事故：发现 `get_input_schema` 报错后，未与用户确认即引入 `from langchain_core.tools import BaseTool` 依赖，影响后续文件拆分需求；正确修复方式应为给 `communicate_to_user` 补齐 `@tool` 装饰器。

**第三方库参数 / API 必须先实测再写入代码**：

调用、扩展或调整任何第三方库（LangChain、Graphiti、Kuzu、protobuf、openai 客户端等）的接口、构造函数参数、字段名时，**禁止**凭记忆或类比直接写代码。必须：

1. **实测确认**参数名、字段名、返回类型——通过 `inspect.signature` / `model_fields` / `dir()` / 官方文档 / 直接实例化 一种以上手段。
2. **改动前 + 改动后**至少各跑一次最小可验证片段（如能 `import` 并实例化、字段值符合预期），通过后再交付。
3. 把验证片段及其输出贴在回复里，使用户可复核。

违反此纪律曾导致 v0.21.0 hotfix 开发事故：为 `ChatOpenAI` 加超时时按 OpenAI Python SDK 习惯写成 `timeout=`，但 `langchain_openai` 实际暴露的字段是 `request_timeout=`，运行时构造直接报参数不存在，需二次修复。

**可自测的功能必须自测完成后再提交验收**：

当开发的功能不依赖 Unity 客户端联调即可测试时（如纯 Python 侧的增删改查、数据处理、记忆系统等），**必须在开发完成后自行编写并运行测试**，确认核心功能正常后再提交给用户验收。禁止未经自测直接交付。

1. 开发前评估：该功能是否可以在不启动 Unity 的情况下测试？
2. 如果可以：开发完成后编写测试脚本并执行，确保通过后再告知用户可验收。
3. 如果必须联调：明确告知用户哪些功能需要联调才能验证。

**Agent 是游戏世界中的角色，不是聊天机器人**：

Agent 生活在虚拟游戏世界中，以角色身份行动和思考，不是在执行聊天机器人的工具调用。开发与 Agent 相关的功能时（工具函数、prompt、记忆系统等），必须遵循：

1. **工具描述文风角色化**：不要写得像冰冷的 API 文档。工具是角色大脑内的基础功能——"回想技能"而非"查询数据库"、"遗忘"而非"删除"、"总结为技能"而非"保存记录"。
2. **不暴露内部实现细节**：Agent 不需要知道 UUID、数据库主键、内部数据结构。用人类可读的名称标识事物（如 `template_name` 而非 `template_id`）。
3. **参数设计直觉化**：参数命名和含义应贴近角色的思维方式，而非程序员的思维。

---

*扩展架构、新增 Flow Step 或改动记忆/打断语义时，请同步更新本文件。*

---

## 九、开发事故记录

### 2026-06-11 v0.20.12 `get_input_schema` 报错

**严重程度**：中（运行时报错，未影响数据）

**事故描述**：`prompt_utils.py` 的 `get_tools_token_count` 对 `tools` 列表中每个元素调用 `.get_input_schema()`，但 `base_tools.py` 中 `communicate_to_user` 缺少 `@tool` 装饰器，是普通 `async function` 而非 `BaseTool` 实例，导致运行时报 `'function' object has no attribute 'get_input_schema'`。

**流程违规**：发现问题后，Agent 未与用户确认修复方案即直接修改代码，引入了 `from langchain_core.tools import BaseTool` 依赖和 inspect 签名解析逻辑。用户指出应遵守"确认后再改"的流程，且该依赖会影响后续文件拆分；正确修复方式应为给 `communicate_to_user` 补齐 `@tool` 装饰器。

**教训**：
1. **修复方案必须与用户确认后再执行**，尤其涉及新增依赖或架构决策时。
2. 工具列表中的所有元素应为同一类型（`BaseTool`），不一致时应修复源头而非下游兼容。
3. 发现遗漏的装饰器时，应补齐装饰器而非绕过。

### 2026-06-20 v0.21.4 无效测试与未先写测试用例

**严重程度**：中（流程违规，测试无效；未造成数据破坏）

**事故描述**：v0.21.4 开发范围物体 `LeftPosition` / `RightPosition` 与 Unity 规划校验时，Agent 在未先把测试用例写入 `DevDocs/v0.21.4/solution.md` 的情况下，临时创建并运行了 `Src/PythonServer/test_v021_4_action_sequence_range_condition.py`。该测试只验证 Python Pydantic schema 能接受新增字段名，未覆盖本次修复的核心链路：Unity `SceneObjExprView` 是否从 `RangeCollider.bounds` 正确生成边界、`ConditionEvaluator` 是否拒绝范围物体 `Position`、DynamicExpresso 是否能真实求值边界字段、环境渲染是否输出左/右边界格式。因此该测试对开发几乎没有帮助，属于无效测试。

**流程违规**：
- 违反“可自测的功能必须自测完成后再提交验收”的前置要求：开发前没有把测试用例、输入、期望输出和覆盖风险写清楚。
- 用“有一个测试脚本能通过”替代“测试覆盖了核心风险”。
- 在用户指出问题前，没有主动识别该测试只覆盖 Python 表层 schema，不能证明 Unity 侧核心行为正确。

**教训**：
1. 开发前必须先在版本 `solution.md` 写清楚测试用例矩阵，包含测试目标、前置条件、输入、步骤、期望输出、覆盖风险。
2. 测试必须覆盖本次改动的真实风险链路；仅验证字段名/schema 的测试不能作为涉及 Unity 行为、表达式求值或渲染语义的验收依据。
3. 临时命令行片段或临时低价值脚本不得替代方案中的测试设计。
4. 用户要求中止开发后，只能补文档和事故记录，不得继续业务开发或继续运行测试。

### 2026-06-16 v0.21.0 hotfix `ChatOpenAI` 超时参数名错误

**严重程度**：低（构造期立即可见，未影响线上数据）

**事故描述**：为定位 LLM 调用 hang 的问题，需要给 `ChatOpenAI` 加超时与重试。Agent 凭 OpenAI Python SDK 的常见写法直接写成 `timeout=120, max_retries=1`，未实测验证 `langchain_openai` 当前版本的实际字段名。用户随即指出"`ChatOpenAI` 没有 `timeout` 这个参数"，Agent 才用 `model_fields` 实测，确认字段为 `request_timeout`，二次修复后通过。

**流程违规**：
- 改动**前**未跑 `inspect`/`model_fields` 验证参数名。
- 改动**后**未实例化跑通即交付。
- 既往"修复方案先确认再执行"纪律虽然要求过，但仍未覆盖"第三方库参数实测"这一具体动作。

**教训**：
1. 调第三方库的构造函数/方法签名时，**默认不可信记忆**——必须 `inspect.signature` / `model_fields` / 实例化 至少一种实测。
2. 改动前后都要有"最小可验证片段"，并在回复里贴出验证输出。
3. 已新增「第三方库参数 / API 必须先实测再写入代码」纪律条款，参见上文 §开发纪律。

### 2026-07-03 v0.22.1 同源冷却范围偏离需求 + 未先确认即改代码

**严重程度**：中（需求语义偏离，需返工；未造成数据破坏）

**事故描述**：v0.22.1 PRD 对 `mSameSourceCooldown`（同源异常事件冷却）的原始需求是："避免一群 EnemyBase 路过 BrokenGlass 时，每个人踩上去都会集体停下来"。语义上**仅针对"其他敌人触发的同源异常事件"**才有冷却，玩家/装置自身触发的完整调查不应被冷却限制。但 Agent 在实现 `OnEnemyAnomalyEventFired` 与 `OnHearAnomaly` 时，把同源冷却写成了**对所有来源（含玩家触发）统一生效**，导致玩家短时间内反复踩同一块玻璃时，EnemyBase 在冷却期内不响应--偏离了"玩家行为应始终能吸引敌人"的预期。用户在 2026-07-03 指出此偏差。

更严重的是，在同一次联调中，Agent 针对"Enemy A 莫名转头"问题，**未经用户确认方案即直接修改代码**（加 `other.isTrigger` 过滤、`mArrivedFromPatrol` 朝向恢复、`IsInBattle` 归属迁移、`mPatrolPoints` null 清洗等多处变更），违反 §开发纪律「修复/变更方案必须先确认再执行」。

**流程违规**：
- 同源冷却实现前未回溯需求原文逐条核对，凭"逻辑对称"臆断为对所有来源生效。
- 联调中发现行为异常后，未先写修复方案让用户确认，直接连续多轮改代码。
- 违反"修复 / 变更方案必须先确认再执行"纪律（与 2026-06-11 v0.20.12 同类违规）。

**教训**：
1. 实现需求时必须回溯 PRD/需求原文逐条核对语义，尤其涉及"针对哪类触发源"的限定词，不能凭逻辑对称臆断。
2. 联调中发现的任何行为偏差，**一律先写方案让用户确认再改代码**，无论改动看起来多小。已经因为"看起来是小修复"直接改而多次违规，必须建立"任何变更都先方案"的肌肉记忆。
3. 同一次联调里连续多轮直接改代码，会掩盖需求语义偏离，应在第一处修改前就停下确认。
