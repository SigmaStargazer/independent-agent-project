# PRD - v0.22.2 idle wakeup 无信息量心理活动抑制写入长期记忆

> **状态**：已实现
> **对应需求**：`requirements/idle_wakeup_skip_memory.md`
> **最后更新**：2026-07-10

---

## 1. 背景与目标

### 1.1 现状

Agent 在完成当前任务后进入 idle 等待期，系统每隔几十秒推送一次 `idle wakeup` 消息（「你已经空闲了一段时间，可以稍微留意一下周围」+ 世界事件摘要）。idle wakeup 的设计意图是让 Agent 留意周围是否有值得行动的变化。

但当前实现存在两个问题：

1. **idle wakeup 被当作普通用户消息处理**：Agent 的心理活动（「一切如常，继续待命」类）会被 `save_memory` 节点无条件入队写入 Graphiti 图库。
2. **Agent 在 idle 期反复产出几乎相同的心理活动**：连续 30+ 条同义 Episode 入库，每条都触发一次 LLM 抽取（费 token）、3 次 retry（费时间），且因 Graphiti 事实去重把这些同义句判为同一条事实边复用 uuid，触发 Kuzu `MERGE` 主键冲突 -> Episode 被静默丢弃（详见需求池条目 8）。

直接后果：
- **数据丢失**：idle 期每轮 retry 全失败的 Episode 被静默丢弃。
- **token 与时间浪费**：每条 idle 响应都跑 LLM 抽取 + 3 次 retry。
- **记忆图谱噪声**：即使 retry 成功，「一切如常」类无信息量 Episode 大量堆积，挤压有用 Episode 的语义权重。

### 1.2 目标

- idle wakeup 触发的纯心理活动（无工具调用）**不写入长期记忆**。
- idle wakeup 若触发了真正的行动（Agent 主动调用工具），则该轮记忆**正常写入**（因为说明 idle 触发了有意义的行为）。
- 不影响正常用户消息、反馈消息的记忆写入逻辑。

---

## 2. 范围

### 2.1 本期包含

- **入口标记**：`Agent._enqueue_idle_wakeup_message` 识别 idle wakeup 消息，在入队的 `TimedMessage` 上附加 `skip_memory` 标记。
- **State 扩展**：`State` 新增 `skip_memory: bool` 字段，由 `aprocess_message` 从 `TimedMessage.skip_memory` 传入 `input_state`。
- **节点旁路**：`save_memory` 节点读取 `state['skip_memory']`，为 `True` 时直接返回、不入队。
- **工具调用解除 skip**：`cache_tool_mem` 节点检测到本轮有工具调用时，将 `skip_memory` 置为 `False`（idle 触发了真正行动，应正常写入）。

### 2.2 本期不包含

- 不改动 `memory_manager.py` 的 `save_memory` 入队逻辑与 `_memory_worker`（后台写入链路不变）。
- 不处理条目 8（Kuzu 主键冲突）的下游 retry 兼容性——本版本通过减少上游触发来大幅降低其发生概率，条目 8 仍作为独立候选问题。
- 不改动 Unity 侧任何逻辑（idle wakeup 消息完全由 Python 侧 `Agent` 类生成和入队）。
- 不改动协议（`message.proto`）。
- 不对非 idle wakeup 的消息做相似度去重（方案 B 不在本期范围）。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| Agent（小明） | 任务完成后 idle 等待，收到 idle wakeup 但周围无变化 | Agent 产出简短心理活动，本轮记忆不写入图库，不触发 LLM 抽取、不刷 worker 日志 |
| Agent（小明） | idle 等待期间，idle wakeup 携带的世界事件摘要中出现新变化（如敌人移动） | Agent 决定调用 `observe_cmd` 等工具查看情况，本轮记忆正常写入（因为触发了真正行动） |
| Agent（小明） | idle 期间收到真实用户消息或环境反馈 | 记忆正常写入，不受 skip_memory 影响 |
| 开发者 | 查看训练日志 | idle 期不再出现连续 30+ 条同义 Episode 入库的噪声；worker 日志不再被主键冲突 retry 刷满 |

---

## 4. 功能需求

### 4.1 idle wakeup 消息标记

- `Agent._enqueue_idle_wakeup_message` 在构造 `TimedMessage` 时，将 `skip_memory` 标记为 `True`。
- 该标记仅由 idle wakeup 路径设置，其他消息入口（`asend_message` / `asend_feedback`）不设置此标记（默认 `False`）。

### 4.2 State 扩展

- `State` 新增 `skip_memory: bool` 字段，默认 `False`。
- `aprocess_message` 在构造 `input_state` 时，从本轮 `TimedMessage` 列表中读取 `skip_memory` 标记（任一消息携带则为 `True`）。

### 4.3 save_memory 节点旁路

- `save_memory` 节点在执行写入前检查 `state.get('skip_memory', False)`：
  - 为 `True`：跳过 `memory_manager.save_memory` 调用，直接返回清空 `mem_to_save` 与 `logged_tool_call_ids`（与正常路径返回结构一致）。
  - 为 `False`：维持现有逻辑不变。

### 4.4 工具调用解除 skip

- `cache_tool_mem` 节点在检测到本轮有新的 `tool_calls`（`new_entries` 非空）时，将 `skip_memory` 置为 `False`。
- 语义：idle wakeup 触发了 Agent 的真实行动，该轮经历有信息价值，应正常写入。

---

## 5. 非功能需求

- **性能**：idle 期每轮省去一次 LLM 抽取（约数百~数千 token）+ 3 次 retry（数秒），显著降低 idle 期的 CPU/IO/token 开销。
- **兼容性**：`skip_memory` 是新增字段，旧 checkpoint 不含此字段时 `state.get('skip_memory', False)` 默认为 `False`，行为与当前一致。
- **可观测性**：`save_memory` 节点跳过写入时打印一条 INFO 日志（`[name] skip memory (idle wakeup, no action)`），便于开发者确认行为。

---

## 6. 验收标准

- [ ] idle wakeup 且 Agent 未调用工具时，`memory_manager.save_memory` 不被调用（可通过日志或 mock 验证）。
- [ ] idle wakeup 且 Agent 调用了工具（如 `observe_cmd`）时，`memory_manager.save_memory` 正常被调用。
- [ ] 非 idle wakeup 的普通用户消息 / 反馈消息的记忆写入不受影响。
- [ ] idle wakeup 期间 worker 日志不再出现主键冲突 retry 噪声。
- [ ] `pytest` 自测通过（不依赖 Unity 联调）。

---

## 7. 待确认问题

- [x] `TimedMessage` 是否需要新增 `skip_memory` 字段？-> 是，直接扩展 `TimedMessage`，新增 `skip_memory: bool = False`。
- [x] `aprocess_message` 在 drain 合并多条消息时，如果同时有 idle wakeup 和用户消息，`skip_memory` 应如何取值？-> 任一消息携带则为 `True`；若 Agent 随后调用工具，`cache_tool_mem` 会解除 skip。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
