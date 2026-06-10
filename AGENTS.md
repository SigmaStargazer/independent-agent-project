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

基于 **Graphiti**（图记忆框架）+ **Kuzu**（嵌入式图数据库），独立配置 LLM / Embedding / Reranker（`.env` 中 `MEMORY_*`、`EMBEDDING_*`、`RERANKER_*`）。

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
| 记忆 | `memory_system/memory_manager.py` | Graphiti/Kuzu、RAG、备份 |
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

---

## 八、文档、Skills 与约定

| 资源 | 路径 |
|------|------|
| **版本开发文档（需求→PRD→方案）** | **`DevDocs/`**（说明见 `DevDocs/README.md`） |
| 工具开发 | `Doc/Agent工具开发流程.md` |
| ActionSequence | `Doc/ActionSequence开发流程.md` |
| 存档方案 | `Doc/存档方案.md` |
| Cursor Skill | `.cursor/skills/develop-agent-tool/`（PythonServer 工作区副本：`Src/PythonServer/.cursor/skills/`） |

### DevDocs 协作约定

1. 每个小版本在 `DevDocs/vX.Y/` 建目录；**你**把需求放在 `requirements/`。
2. Agent **先读 requirements**，在同目录生成 `PRD.md`、`solution.md`，**等你确认后再开发**。
3. `Doc/` 放跨版本技术指南；`DevDocs/` 放按版本归档的需求与方案。
4. 模板与流程细节：`.cursor/rules/dev-docs-workflow.mdc`、`DevDocs/_template/`。

**Skill 用法**：Agent 聊天输入 `/develop-agent-tool` 或 `@develop-agent-tool`；找不到时检查工作区根目录并重载窗口。

**已确认约定**：

1. 生产 Agent 仅 `agent_interuptible.py`
2. `Src/GameServer/` 与 Agent 链路无关
3. 协议只认 `Tools/message.proto`；`Src/Lib/proto/` 完全不用管
4. 与用户交流默认简体中文
5. 不主动 git commit / push，除非用户明确要求

### 开发纪律

**事件过滤 / 状态判断类逻辑必须先做完整场景枚举**：

当需要实现「哪些事件应该写入、哪些应该跳过」等过滤逻辑时，**禁止**直接写代码。必须：

1. **枚举所有触发路径**，逐一标注期望行为，形成表格（触发场景 → 期望 → 理由）。
2. **验证过滤条件对每条路径的判定结果**，确认无遗漏。
3. **方案经用户确认后才写代码**。

违反此纪律曾导致 v0.20.10 开发事故：用「是否曾消失过」过滤首次 Appearance，遗漏了「新角色动态入场」也是首次 Appearance 的场景，导致新入场事件被错误跳过。

---

*扩展架构、新增 Flow Step 或改动记忆/打断语义时，请同步更新本文件。*
