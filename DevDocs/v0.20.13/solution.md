# 技术方案 — v0.20.13 工具 RPC 化统一

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-06-11

---

## 1. 方案概述

将 `move_cmd` 和 `communicate_to_user` 两个未 RPC 化的工具改造为 TOOL_WAITERS RPC 机制，使所有涉及 Unity 的工具都等待 Unity 端返回执行结果后再向 LLM 回报，与已 RPC 化的工具保持一致模式。

改造后，**所有涉及 Unity 通信的工具均已 RPC 化**，无遗留。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| 协议 | `Tools/message.proto` | `AgentMoveRequest` 新增 `request_id`；`AgentSendMessageRequest` 新增 `request_id` |
| Python | `agent_framwork/tools/base_tools.py` | `move_cmd`、`communicate_to_user` 改为 RPC 模式 |
| Python | `network/servers.py` | 无变更——`SendToolResultMessageRequest` 已通用 |
| Unity | `AgentService` / `AIPlayer` | 两个 Request 处理逻辑新增 `SendToolResultMessageRequest` 回传 |
| Unity | `MessageDispatch.cs` | 无新 Request 类型，无需注册新分发 |

## 3. 详细设计

### 3.1 数据与协议

#### 3.1.1 `AgentMoveRequest` — 新增 `request_id`

**现状**：
```protobuf
message AgentMoveRequest {
  string agent = 1;
  bool is_right = 2;
  float distance = 3;
}
```

**改为**：
```protobuf
message AgentMoveRequest {
  string agent = 1;
  string request_id = 2;
  bool is_right = 3;
  float distance = 4;
}
```

> `is_right` 和 `distance` 字段编号从 2→3、3→4 顺延。Proto3 未知字段会被跳过，不存在兼容问题。

#### 3.1.2 `AgentSendMessageRequest` — 新增 `request_id`

**现状**：
```protobuf
message AgentSendMessageRequest {
  string agent = 1;
  string ai_message = 2;
}
```

**改为**：
```protobuf
message AgentSendMessageRequest {
  string agent = 1;
  string request_id = 2;
  string ai_message = 3;
}
```

> `ai_message` 字段编号从 2→3 顺延。

### 3.2 Python（Brain）

#### 3.2.1 `move_cmd` 改造

**现状**：直接 `broadcast_message` 后 `return` 预设文本，不等结果。

**改为**：标准 RPC 模式（与 `follow_target_cmd` 等一致）：

```python
@tool
async def move_cmd(
    agent: Annotated[str, InjectedState("name")],
    tool_call_id: Annotated[str, InjectedToolCallId],
    direction: str, distance: float
) -> str:
    """持续行动类动作-向指定方向移动指定距离
    重要行为规则：
    - 移动是异步执行的。
    - 移动结果不会在本轮对话中返回。
    - 移动完成后，系统会主动发送通知消息。

    当你执行移动后：
    - 不要持续调用 observe 等待移动完成。
    - 应结束本轮对话，等待移动完成通知。

    Args:
        direction(str): 方向，填left或者right
        distance(float): 距离
    Return:
        str: 移动是否已开始。注意：移动完成结果将通过新的消息另行通知。
    """
    if direction not in ["left", "right"]:
        return "方向错误，请填left或者right"

    request_id = tool_call_id
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    TOOL_WAITERS[request_id] = fut

    try:
        request = message_pb2.AgentMoveRequest()
        request.agent = agent
        request.request_id = request_id
        request.is_right = direction == "right"
        request.distance = distance

        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] move_cmd 发起请求 {request_id}")
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]移动未开始，超时"
    except Exception as e:
        return f"[{agent}]移动异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)
```

返回值含义变化：
- **现状**：`"[Agent]尝试向right移动了2.0距离。待移动完成后..."` — Python 自己编的
- **改为**：由 Unity 端通过 `SendToolResultMessageRequest` 回传真实结果

#### 3.2.2 `communicate_to_user` 改造

**现状**：直接 `broadcast_message` 后 `return` 预设文本，不等结果。

**改为**：标准 RPC 模式：

```python
@tool
async def communicate_to_user(
    agent: Annotated[str, InjectedState("name")],
    tool_call_id: Annotated[str, InjectedToolCallId],
    message: str
) -> str:
    """向用户发送一则消息
    Args:
        message(str): 你想要发送的消息
    """
    request_id = tool_call_id
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    TOOL_WAITERS[request_id] = fut

    try:
        request = message_pb2.AgentSendMessageRequest()
        request.agent = agent
        request.request_id = request_id
        request.ai_message = message

        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] communicate_to_user 发起请求 {request_id}")
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]向用户发送消息超时"
    except Exception as e:
        return f"[{agent}]向用户发送消息异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)
```

### 3.3 Unity（Environment）

#### 3.3.1 `AgentMoveRequest` 处理

**现状**：`AIPlayer` 收到后执行移动，无回传结果。

**改为**：
1. 收到 `AgentMoveRequest`（含 `request_id`）
2. 尝试启动移动（检查是否可移动、是否有阻挡等）
3. 无论成功/失败，回传 `SendToolResultMessageRequest`：
   - 成功：`result = "移动已开始，方向：右，距离：2.0。移动完成后你将收到通知。"`
   - 失败：`result = "移动失败：前方被障碍阻挡"` 或 `"移动失败：当前已在移动中"`
4. 移动完成后，仍通过 `UserSendFeedbackRequest` 发送移动完成通知（保持现有反馈机制不变）

#### 3.3.2 `AgentSendMessageRequest` 处理

**现状**：收到后展示消息，无回传结果。

**改为**：
1. 收到 `AgentSendMessageRequest`（含 `request_id`）
2. 展示消息
3. 回传 `SendToolResultMessageRequest`：
   - 成功：`result = "消息已发送: {message_content}"` 或类似
   - 失败：`result = "消息发送失败"`

**实际实现**：
- `AgentService` 通过 `OnGetAgentMessage` 事件分发（签名 `UnityAction<string agent, string requestId, string message>`）
- `AgentManager.OnGetAgentMessage` 转发到对应 `AIPlayer.OnGetAgentMessage(requestId, message)`
- `AIPlayer.OnGetAgentMessage` 回传 `SendToolResultMessage`
- `UIAgentGame`、`UIChat` 也订阅了 `OnGetAgentMessage`，签名补齐了 `requestId` 参数

### 3.4 不改动的工具确认

以下工具**已是 RPC 模式**，无需改动（16个）：

`observe_cmd`、`monitor_target_cmd`、`get_monitor_records_cmd`、`follow_target_cmd`、`interact_cmd`、`select_cmd`、`input_cmd`、`stop_action_cmd`、`plan_action_sequence_cmd`、`start_action_sequence_cmd`、`continue_action_sequence_cmd`、`stop_action_sequence_cmd`、`set_timer_cmd`、`get_timer_list_cmd`、`remove_timer_cmd`、`get_world_event_log_cmd`

以下工具**不涉及 Unity**，无需 RPC（8个）：

`get_cur_time`、`set_focus_state`、`search_fact_memories`、`search_episode_memories`、`get_agent_list`、`add_alarm`、`get_alarm_list`、`remove_alarm`

**改造完成后，所有涉及 Unity 的工具均已 RPC 化，无遗留未 RPC 化的工具。**

## 4. 实现步骤

1. **修改 `message.proto`**：`AgentMoveRequest` 新增 `request_id`（编号2，`is_right`→3、`distance`→4）；`AgentSendMessageRequest` 新增 `request_id`（编号2，`ai_message`→3）
2. **生成协议**：执行 `1.genproto.cmd`
3. **Python `base_tools.py`**：改造 `move_cmd` 和 `communicate_to_user` 为 RPC 模式
4. **Unity `AgentService` / `AIPlayer`**：两个 Request 处理逻辑增加 `SendToolResultMessageRequest` 回传
5. **Rebuild `CSharpClient.sln`** → 执行 `2.copyprotocol.cmd`
6. **联调测试**：验证两个工具的 RPC 回调正常

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| Proto 字段编号变更可能导致旧 Unity 客户端解析异常 | Proto3 未知字段会被跳过，不会崩溃；当前项目不需要旧版兼容 |
| `move_cmd` 返回值语义变化可能影响 Agent 行为 | 返回值更真实，Agent 可根据「移动失败」做决策调整，属于正面改进 |
| `communicate_to_user` RPC 化后，若 Unity 回调延迟可能导致 Agent 等待 | 超时机制（30s）兜底；正常场景 Unity 回调应很快 |

## 6. 测试建议

- 联调测试：
  - `move_cmd`：正常移动 → 收到「移动已开始」；被阻挡 → 收到「移动失败」
  - `communicate_to_user`：正常发送 → 收到「消息已发送」
- 超时测试：两个工具在 Unity 不回调时的超时返回
- 回归测试：已 RPC 化的工具（observe、interact 等）行为不变

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-11 | v0.20.13 开发完成：move_cmd、communicate_to_user 两个工具 RPC 化。Proto 新增 request_id 字段，Python 改为 TOOL_WAITERS 模式，Unity 侧 AgentService/AgentManager/AIPlayer 接通 SendToolResultMessage 回调 |
| 2026-06-11 | 用户手动统一 Unity 侧回调函数名为 `OnGetAgentMessage`（AgentManager、AIPlayer），并为 `UIAgentGame`、`UIChat` 补齐 `string requestId` 参数。验收通过 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*