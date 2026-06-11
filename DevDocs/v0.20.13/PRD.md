# PRD — v0.20.13 工具 RPC 化

> **状态**：已实现
> **对应需求**：用户口述
> **最后更新**：2026-06-11

---

## 1. 背景与目标

当前项目中，部分 Agent 工具（`*_cmd`）已采用 **Tool RPC 机制**：Python 发送 Request → Unity 执行 → Unity 回调 `SendToolResultMessageRequest` → Python `TOOL_WAITERS` 中 Future 被唤醒 → 工具获得真实结果返回 LLM。

但 `move_cmd` 和 `communicate_to_user` 是在 RPC 机制引入之前写的，目前仍采用**旧模式**：直接 `broadcast_message` 发出去就假定操作已完成，**不等待 Unity 端的确认结果**。这意味着：
- Agent 无法知道移动是否真正开始（可能因碰撞、状态异常等原因未能启动）
- Agent 无法知道消息是否真正送达（`broadcast_message` 可能失败）
- 返回给 LLM 的信息是 Python 自己伪造的，而非 Unity 的真实反馈
- 与其他已 RPC 化的工具行为不一致，增加 LLM 认知混乱

目标：**将 `move_cmd` 和 `communicate_to_user` 改为 RPC 化**，使所有涉及 Unity 的工具统一采用 RPC 机制——等待 Unity 端返回真实结果。

同时整理其余工具的 RPC 化现状，形成完整审计记录。

## 2. 范围

### 2.1 本期包含

- `move_cmd` RPC 化（主要改动）
- `communicate_to_user` RPC 化
- 整理并记录所有工具的 RPC 化现状分类

### 2.2 本期不包含

- `communicate_to_agent`（已弃用，不改造）
- 其他本地工具（不涉及 Unity，无需 RPC）
- 工具行为逻辑变更（只改通信模式，不改语义）

## 3. 工具 RPC 化现状审计

### 3.1 分类标准

| 分类 | 定义 | 通信模式 |
|------|------|----------|
| **已 RPC 化** | proto 有 `request_id` 字段 + Python 用 `TOOL_WAITERS` 等 Future | Request → Unity → `SendToolResultMessageRequest` → Future 唤醒 |
| **未 RPC 化** | proto 无 `request_id` + Python 只 `broadcast_message` 不等结果 | Request 发出去即返回预设文本 |
| **本地工具** | 不经 Unity，Python 本地完成 | 无网络通信 |

### 3.2 现状详表

| 工具 | proto 消息 | 有 request_id? | Python 等结果? | 分类 | 需 RPC 化? |
|------|-----------|---------------|--------------|------|-----------|
| `move_cmd` | `AgentMoveRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | — 已完成 |
| `communicate_to_user` | `AgentSendMessageRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | — 已完成 |
| `communicate_to_agent` | 无 proto（本地路由） | — | — | 已弃用 | ❌ 不改 |
| `observe_cmd` | `AgentObserveRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `monitor_target_cmd` | `AgentMonitorTargetRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `get_monitor_records_cmd` | `AgentGetMonitorRecordsRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `get_world_event_log_cmd` | `AgentGetWorldEventLogRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `follow_target_cmd` | `AgentFollowTargetRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `interact_cmd` | `AgentInteractRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `select_cmd` | `AgentSelectRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `input_cmd` | `AgentInputRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `stop_action_cmd` | `AgentStopActionRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `plan_action_sequence_cmd` | `AgentPlanActionSequenceRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `start_action_sequence_cmd` | `AgentStartActionSequenceRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `continue_action_sequence_cmd` | `AgentContinueActionSequenceRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `stop_action_sequence_cmd` | `AgentStopActionSequenceRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `set_timer_cmd` | `AgentSetTimerRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `get_timer_list_cmd` | `AgentGetTimerListRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `remove_timer_cmd` | `AgentRemoveTimerRequest` | ✅ 有 | ✅ 等 | 已 RPC 化 | ❌ |
| `get_cur_time` | 无 proto（本地） | — | — | 本地工具 | ❌ |
| `set_focus_state` | 无 proto（本地） | — | — | 本地工具 | ❌ |
| `search_fact_memories` | 无 proto（本地） | — | — | 本地工具 | ❌ |
| `search_episode_memories` | 无 proto（本地） | — | — | 本地工具 | ❌ |
| `get_agent_list` | 无 proto（本地） | — | — | 本地工具 | ❌ |
| `add_alarm` | 无 proto（本地） | — | — | 本地工具 | ❌ |
| `get_alarm_list` | 无 proto（本地） | — | — | 本地工具 | ❌ |

### 3.3 需 RPC 化的工具分析

**`move_cmd`**：
1. 移动可能失败（碰撞阻挡、角色状态异常、位置不合法等）
2. 当前返回信息是 Python 自己编造的"尝试向{direction}移动了{distance}距离"，Unity 端可能移动根本没开始
3. 与同类持续动作工具 `follow_target_cmd`（已 RPC 化）不一致

**`communicate_to_user`**：
1. 消息可能送达失败（连接断开、Unity 异常等）
2. 当前发送后直接返回"你向用户发送了一则消息"，无法确认消息是否真正到达
3. RPC 化后 Agent 可以知道消息是否送达，做出相应决策

## 4. 功能需求

### 4.1 move_cmd RPC 化

1. **proto 侧**：给 `AgentMoveRequest` 增加 `request_id` 字段
2. **Python 侧**：`move_cmd` 改为注册 `TOOL_WAITERS[request_id]`，`await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)` 等待 Unity 回调结果
3. **Unity 侧**：`AIPlayer` / `AgentService` 处理 `AgentMoveRequest` 后，通过 `SendToolResultMessageRequest` 回调结果（包含移动是否成功开始的信息）
4. **返回语义变更**：工具返回内容从 Python 自己编的"尝试移动了"变为 Unity 端实际返回的"移动是否已开始"结果

### 4.2 communicate_to_user RPC 化

1. **proto 侧**：给 `AgentSendMessageRequest` 增加 `request_id` 字段
2. **Python 侧**：`communicate_to_user` 改为注册 `TOOL_WAITERS[request_id]`，等待 Unity 回调
3. **Unity 侧**：处理 `AgentSendMessageRequest` 后，回传 `SendToolResultMessageRequest` 确认消息是否送达

### 4.3 工具审计记录

在 PRD 和 solution 中记录完整的工具 RPC 化现状审计表，作为项目文档资产。

## 5. 验收标准

- [x] `AgentMoveRequest` proto 有 `request_id` 字段
- [x] `AgentSendMessageRequest` proto 有 `request_id` 字段
- [x] `move_cmd` Python 代码使用 `TOOL_WAITERS` + `wait_for` 等待 Unity 结果
- [x] `communicate_to_user` Python 代码使用 `TOOL_WAITERS` + `wait_for` 等待 Unity 结果
- [x] Unity 端处理两个 Request 后回调 `SendToolResultMessageRequest`
- [x] Agent 调用 `move_cmd` 时能收到 Unity 端返回的真实结果（成功/失败/原因）
- [x] Agent 调用 `communicate_to_user` 时能收到 Unity 端返回的真实结果
- [x] 超时场景正确处理（30s 超时返回提示信息）
- [x] 所有涉及 Unity 的工具行为一致（全部 RPC 化），无遗漏

---

*本文档由 Cursor Agent 生成，确认前请勿直接据此改代码。*