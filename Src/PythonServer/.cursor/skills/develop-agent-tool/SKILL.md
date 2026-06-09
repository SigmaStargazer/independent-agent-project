---
name: develop-agent-tool
description: >-
  Develops LangGraph agent tools for this independent-agent project end-to-end:
  Python base_tools, protobuf protocol, Unity AgentService/AgentManager/AIPlayer,
  agent_interuptible registration, and ActionSequence new action types in action.py
  (ActionStep proto, build_pb_action_step, ConditionEvaluator). Use when adding
  a new agent tool, _cmd tool, message.proto request, Unity tool handler,
  SendToolResultMessage RPC flow, or a new action in action_sequence_model.
---

# Agent Tool 开发

## 先判断工具类型

| 类型 | 典型命名 | 是否需要协议/Unity | 结果返回方式 |
|------|----------|-------------------|--------------|
| 本地 Python 工具 | `get_cur_time`, `add_alarm` | 否 | 函数直接 `return` |
| Unity 即发即忘 | `move_cmd` | 是 | 不发 RPC 回调；后续用 `SendUserFeedback` 通知 |
| Unity 同步 RPC | `observe_cmd`, `set_timer_cmd` | 是 | `SendToolResultMessage` 唤醒 `TOOL_WAITERS` |
| Unity 长时任务 | `monitor_target_cmd`, `move_cmd` | 是 | RPC 先返回「已开始」；事件后续 `SendFeedbackToAgent` |

需要 Unity 执行的动作，Python 侧函数名统一用 `{action}_cmd` 后缀。

## 协议修改（禁止手改生成文件）

**永远不要直接编辑** `network/message_pb2.py` 或 `Src/Lib/AgentProtocol/message.cs`。

按顺序执行：

1. 改 `Tools/message.proto`
   - 新增 `AgentXxxRequest` message
   - 在 `NetMessageRequest` 中追加字段（注意 field number 递增）
2. 执行 `Tools/1.genproto.cmd`（须无报错）
3. 改 `Src/Lib/Common/Network/MessageDispatch.cs`，在 `Dispatch(NetMessageRequest)` 中增加分发
4. Visual Studio Rebuild `Src/CSharpClient/CSharpClient.sln`
5. 执行 `Tools/2.copyprotocol.cmd`（须无报错）

### 命名对照

| 层 | 规则 | 示例 |
|----|------|------|
| Proto Message | `Agent{Action}Request` | `AgentSetTimerRequest` |
| NetMessageRequest 字段 | 首字母小写驼峰 | `agentSetTimerRequest = 27` |
| Python pb2 | 同 Message 名 | `message_pb2.AgentSetTimerRequest()` |
| C# 属性 | PascalCase | `request.TimerName` |

## Python：`base_tools.py`

参考 `monitor_target_cmd` / `set_timer_cmd`。

### 同步 RPC 模板

```python
@tool
async def xxx_cmd(
    agent: Annotated[str, InjectedState("name")],
    tool_call_id: Annotated[str, InjectedToolCallId],
    # ...业务参数
) -> str:
    """完整 docstring：用途、场景、Args、Return"""
    request_id = tool_call_id
    loop = asyncio.get_running_loop()
    fut = loop.create_future()
    TOOL_WAITERS[request_id] = fut
    try:
        request = message_pb2.AgentXxxRequest()
        request.agent = agent
        request.request_id = request_id
        # ...填充字段
        await AgentServerNetMessage().broadcast_message(request)
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]xxx超时"
    except Exception as e:
        return f"[{agent}]xxx异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)
```

回调由 Unity 调 `AgentService.SendToolResultMessage`，Python `main.py` 的 `handle_tool_result_request` 会 `fut.set_result(result)`。

### docstring 要求

- 说明使用场景与行为规则（是否异步、是否应结束本轮对话）
- 写清 `Args` / `Return`
- 长时任务要说明「完成后系统主动通知」

## Unity 链路（四层）

```
AgentService → AgentManager → AIPlayer → SendToolResultMessage / SendFeedbackToAgent
```

### 1. `AgentService.cs`

- 声明 `event`（参数 >4 个时用 `Action<...>`，不要用 `UnityAction`）
- 构造函数 `Subscribe<AgentXxxRequest>`
- `Dispose` 中 `Unsubscribe`
- 处理函数打日志并 `OnXxx?.Invoke(...)`

### 2. `AgentManager.cs`

- `OnEnable` / `OnDisable` 注册事件
- 按 agent 名查 `mAgents`，转发到 `AIPlayer`

### 3. `AIPlayer.cs`

- 实现业务方法（如 `SetTimer`）
- **同步 RPC**：`AgentService.Instance.SendToolResultMessage(Name, "ToolName", requestId, result)`
- **异步通知**：`SendFeedbackToAgent(msg)`（定时器到期、移动完成等）
- 若有运行时状态，可新增 `XxxRuntime.cs`（参考 `ObserveRuntime` / `TimerRuntime`）
- 在 `Update` 中驱动长时逻辑；`OnDisable` 清理状态
- 可选：在 `GetSelfStateInfo` 中展示进行中任务摘要

### 4. `MessageDispatch.cs`

每个新 Request 增加一行 `RaiseEvent`。

## 注册工具

在 `agent_framwork/agents/agent_interuptible.py` 的 `tools` 列表追加：

```python
base_tools.xxx_cmd,
```

重启 Python 服务后生效。

## 完成检查清单

```
- [ ] message.proto 已更新且 field number 无冲突
- [ ] 1.genproto.cmd 成功（未手改 pb2/cs）
- [ ] MessageDispatch.cs 已补分发
- [ ] CSharpClient.sln Rebuild 成功
- [ ] 2.copyprotocol.cmd 成功
- [ ] base_tools.py 工具函数 + docstring
- [ ] AgentService / AgentManager / AIPlayer 已接通
- [ ] agent_interuptible.py tools 已注册
- [ ] Unity 侧 SendToolResultMessage 的 requestId 与 tool_call_id 一致
```

## ActionSequence 新增 Action 类型

在 `action.py` 新增动作（如 jump）与新增 `_cmd` 工具是**不同流程**：

- **不用**新增 `AgentXxxRequest` / `MessageDispatch`（除非同时加新工具）
- **要改** `message.proto` 的 `ActionStep.oneof` 及具体 `XxxAction` message
- **要改** Python `action.py` → `action_sequence.py` → `build_pb_action_step`
- **要改** Unity `ActionSequenceRuntime` + `AIPlayer.ExecuteCurAction/ExecuteXxxAction`

### 先判断 Action 基类

- **持续型** → 继承 `StateChangeAction`，Unity 参考 `ExecuteMoveAction`（每帧 `ConditionEvaluator.Evaluate`）
- **瞬发型** → 继承 `BaseAction`，Unity 参考 `ExecuteInteractAction`（一次完成）

### ConditionEvaluator 何时要改

| 情况 | 改不改 |
|------|--------|
| 复用 displacement / objects[i].State 等现有变量 | 不改 |
| 新增 condition 根变量 | 改 Python `types.py` + Unity `ConditionContext` + `SetVariables` |
| 新增 objects 可访问属性 | 改 `ExprViewFactory` + Python `members` |
| 新 condition 需语义校验 | 改 `ConditionEvaluator.ValidateAll` |

详细清单与对照表：[action-sequence-reference.md](action-sequence-reference.md)

## 参考

- 通用 tool 与定时器示例：[reference.md](reference.md)
- ActionSequence 新增 Action：[action-sequence-reference.md](action-sequence-reference.md)
- 项目文档：`Doc/Agent工具开发流程.md`、`Doc/ActionSequence开发流程.md`
