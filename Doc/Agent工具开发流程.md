# Agent 工具开发流程

本文档描述本项目中为 LangGraph Agent 新增工具的端到端流程。Cursor Agent 可配合项目 Skill `.cursor/skills/develop-agent-tool/` 使用。

## 一、工具分类

开发前先确认工具属于哪一类：

### 1. 本地 Python 工具

- **示例**：`get_cur_time`、`add_alarm`、`search_fact_memories`
- **特点**：只在 Python 进程内执行，不经过 Unity
- **改动范围**：`base_tools.py` + `agent_interuptible.py` 注册

### 2. Unity 动作工具（需协议）

- **示例**：`observe_cmd`、`move_cmd`、`set_timer_cmd`
- **特点**：Python 通过 protobuf 通知 Unity，由 `AIPlayer` 执行
- **改动范围**：协议 + Python + Unity 全链路

Unity 工具在 Python 侧建议统一使用 `{动作}_cmd` 命名。

### 3. 返回模式

| 模式 | 示例 | Python 等待 | Unity 回调 |
|------|------|-------------|------------|
| 同步 RPC | `observe_cmd`, `set_timer_cmd` | `TOOL_WAITERS` + `await` | `SendToolResultMessage` |
| 即发即忘 | `move_cmd` | 不等待 | 无；完成后 `SendUserFeedback` |
| 长时 + 初始确认 | `monitor_target_cmd` | RPC 等「已开始」 | 后续状态变化 `SendFeedbackToAgent` |

## 二、协议修改流程（重要）

**禁止**直接编辑以下生成文件：

- `Src/PythonServer/network/message_pb2.py`
- `Src/Lib/AgentProtocol/message.cs`

### 标准步骤

```
1. 编辑 Tools/message.proto
2. 执行 Tools/1.genproto.cmd（必须无报错）
3. 编辑 Src/Lib/Common/Network/MessageDispatch.cs
4. Visual Studio Rebuild Src/CSharpClient/CSharpClient.sln
5. 执行 Tools/2.copyprotocol.cmd（必须无报错）
```

### 命名规范

| 层级 | 格式 | 示例 |
|------|------|------|
| Proto Message | `Agent{Action}Request` | `AgentSetTimerRequest` |
| NetMessageRequest 字段 | camelCase | `agentSetTimerRequest = 27` |
| C# 请求属性 | PascalCase | `TimerName`, `RequestId` |
| Python 工具函数 | snake_case + `_cmd` | `set_timer_cmd` |

## 三、Python 侧

### 文件

- 工具实现：`Src/PythonServer/agent_framwork/tools/base_tools.py`
- 工具注册：`Src/PythonServer/agent_framwork/agents/agent_interuptible.py` 的 `tools` 列表
- RPC 回调：`Src/PythonServer/main.py` → `handle_tool_result_request`

### 同步 RPC 工具模式

1. 用 `tool_call_id` 作为 `request_id`
2. 注册 `TOOL_WAITERS[request_id] = fut`
3. `broadcast_message(AgentXxxRequest)`
4. `await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)`
5. `finally` 中 `TOOL_WAITERS.pop(request_id, None)`

Unity 必须通过 `SendToolResultMessageRequest` 回传相同 `request_id`，Python 才会结束等待。

### docstring

每个工具需写清：

- 功能与推荐使用场景
- 异步行为规则（是否应结束本轮对话、是否等待系统通知）
- `Args` / `Return`

## 四、Unity 侧

### 调用链

```
AgentService（收包、发事件）
    → AgentManager（按 agent 名路由）
        → AIPlayer（执行业务、回调结果）
```

### 各层职责

| 文件 | 职责 |
|------|------|
| `MessageDispatch.cs` | 将 `NetMessageRequest` 中新字段分发到 `MessageDistributer` |
| `AgentService.cs` | Subscribe 请求、定义 event、转发给上层 |
| `AgentManager.cs` | OnEnable 注册事件，查 `mAgents` 调用 `AIPlayer` |
| `AIPlayer.cs` | 具体逻辑；`SendToolResultMessage` 或 `SendFeedbackToAgent` |

### 注意

- `UnityAction` 最多 4 个泛型参数；更多参数请用 `Action<...>`
- 长时状态可抽 `XxxRuntime.cs`（参考 `ObserveRuntime`、`TimerRuntime`）
- 在 `Update` 驱动周期逻辑，`OnDisable` 清理资源
- 可在 `GetSelfStateInfo` 中展示进行中任务，便于 Agent 感知

## 五、注册与验证

### 注册

在 `agent_interuptible.py`：

```python
tools = [
    # ...
    base_tools.xxx_cmd,
]
```

### 验证清单

- [ ] `1.genproto.cmd` / `2.copyprotocol.cmd` 均无报错
- [ ] Python 工具能 import，docstring 完整
- [ ] Unity Rebuild 通过，DLL 已复制到 `Assets/References`
- [ ] 调用工具后 Python 日志有「发起请求」
- [ ] Unity 日志有对应 `OnAgentXxx`
- [ ] 同步工具能在超时前收到 `SendToolResultMessage`
- [ ] 长时工具能在之后收到 `SendUserFeedback` / `SendFeedbackToAgent`

## 六、定时器工具实例

本次已落地的定时器三件套可作为模板：

| Python 工具 | Proto Request | AIPlayer 方法 |
|-------------|---------------|---------------|
| `set_timer_cmd` | `AgentSetTimerRequest` | `SetTimer` |
| `get_timer_list_cmd` | `AgentGetTimerListRequest` | `GetTimerList` |
| `remove_timer_cmd` | `AgentRemoveTimerRequest` | `RemoveTimer` |

- 设置/查询/删除：同步 RPC
- 到期通知：`SendFeedbackToAgent("[定时器到期] ...")`
- 重复定时：`timer_repeat=true` 时自动重置

详细字段与文件列表见 `.cursor/skills/develop-agent-tool/reference.md`。

## 七、ActionSequence 类工具（延伸阅读）

动作序列相关分两层：

1. **动作序列工具**（`plan/start/continue/stop_action_sequence_cmd`）：走本文「Unity 动作工具」流程，协议为 `AgentPlanActionSequenceRequest` 等。
2. **动作序列内的 Action 类型**（`action.py` 里的 wait/move/interact…）：扩展 `ActionStep` 结构，**不必新增 tool**。

在 `action.py` 新增一种 action 时的完整改动清单、ConditionEvaluator 判断表，见 **[ActionSequence开发流程.md](./ActionSequence开发流程.md)**。
