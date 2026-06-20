# PRD — v0.21.3 Idle Wakeup 空闲随机唤醒

> **状态**：已确认  
> **对应需求**：用户口述需求（2026-06-18）：在 Python Agent 完成一轮对话后，由 Python 侧随机计时并在 Agent 空闲时低频唤醒；可选拉取 Unity WorldEventLog 摘要；整体保持轻量，不做重型注意力系统。  
> **关联功能设计**：`DevDocs/feature-design/IdleWakeup.md`  
> **最后更新**：2026-06-18

---

## 1. 背景与目标

当前 AI Player / Agent 主要在以下情况下做出反应：

- 接收到他人消息；
- 计时器响起；
- 先前工具 / 任务结束后收到环境反馈；
- 用户或系统主动发送消息。

这会导致一个体验问题：当外界长期没有向 Agent 发送新输入时，Agent 可能进入长期沉默状态。即使世界中已经有 WorldEventLog 记录了近期变化，Agent 也不会主动醒来查看或行动。

本期目标是在 **Python Agent 完成一轮 LangGraph 推理后**，由 Python 侧启动随机空闲计时；如果计时到期时 Agent 仍然空闲，则注入一条轻量的 `<空闲感知>` 消息唤醒 Agent。唤醒消息可携带少量近期世界事件摘要，让 Agent 有机会主动观察、回想世界事件、移动、交流，或者选择继续等待。

本功能的核心目的：

> 在 Agent 不工作时随机唤醒，让 Agent 不会因为长期没接到新信息而卡死在“永远不再主动行动”的状态。

本期必须遵循 `DevDocs/feature-design/IdleWakeup.md` 中的长期原则，尤其是：

1. **空闲唤醒，而非实时事件总线**；
2. **Python 侧判断 LangGraph 是否空闲**；
3. **轻量化，不做复杂注意力系统**；
4. **包含随机变量，例如随机唤醒时间**；
5. **Agent 可忽略唤醒，不强制行动**。

---

## 2. 范围

### 2.1 本期包含

- 在 Python `Agent` 运行时增加 Idle Wakeup 调度能力。
- 在一轮 LangGraph 推理结束后，如果 Agent 队列为空，则启动随机 idle timer。
- timer 到期时再次确认 Agent 仍未运行、未被打断、消息队列 / 反馈队列为空。
- 确认空闲后，向 Agent 注入一条 `<空闲感知>` 消息。
- 唤醒时间使用随机区间，而不是固定周期。
- 唤醒消息允许 Agent 忽略、继续等待、观察周围、回想近期世界事件或主动行动。
- 版本 2 行为：唤醒前尝试从 Unity 拉取少量 WorldEventLog 摘要，并放入 `<空闲感知>`。
- 若拉取 WorldEventLog 摘要失败，不阻断唤醒；降级为无事件摘要的空闲感知。
- 支持有事件 / 无事件两类随机唤醒区间或冷却策略。
- 增加必要日志，便于确认 idle timer 创建、取消、触发、降级原因。

### 2.2 本期不包含

- 不做每个世界事件向 Python 的实时广播。
- 不做 salience / score / 注意力评分系统。
- 不做每个 SceneObj 或 state 的权重表。
- 不让 LLM 判断事件重要性。
- 不做复杂 PerceptionBuffer。
- 不改造 Unity AIPlayer 的身体 Idle 判断为触发依据。
- 不把完整 WorldEventLog 默认注入每次唤醒上下文。
- 不自动把空闲唤醒内容写入长期记忆以外的专用结构。
- 不改变现有用户消息、工具反馈、计时器反馈的优先级语义。
- 不要求 Agent 收到空闲唤醒后必须行动。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| Agent | 完成一轮对话 / 工具链后，长期没有新消息 | Python 随机等待一段时间后唤醒 Agent，Agent 有机会主动观察或行动 |
| Agent | 空闲期间 Unity WorldEventLog 中有新事件 | 唤醒消息中包含少量近期事件摘要，Agent 可选择进一步回想完整事件日志 |
| Agent | 空闲期间没有新世界事件 | 更低频地收到无明显变化的空闲感知，可继续等待或主动观察 |
| Agent | 正在运行 LangGraph 或队列中已有消息 | idle timer 不应注入唤醒消息，避免干扰正常处理 |
| Agent | 收到空闲唤醒但不想行动 | 可以直接保持沉默 / 等待，不应被 prompt 强制行动 |
| 开发者 | 调试 Agent 长期沉默问题 | 日志能看到 idle timer 是否创建、取消、触发，以及是否成功拉取事件摘要 |

---

## 4. 功能需求

### 4.1 Python 侧空闲判断

**FR-1.1 触发点**

- 每次 `Agent.aprocess_message` 中一轮 `graph.ainvoke(...)` 正常结束后，进入 idle wakeup 调度判断。
- 如果本轮结束后 `message_queue` 与 `feedback_queue` 均为空，且 Agent 未处于中断退出状态，则允许启动新的 idle timer。

**FR-1.2 忙碌状态**

Python Agent 需要维护轻量运行状态，用于判断 LangGraph 是否正在运行：

- `graph.ainvoke(...)` 开始前标记为运行中；
- `graph.ainvoke(...)` 结束或异常退出后清除运行中标记；
- timer 到期时若仍处于运行中，不发送唤醒。

**FR-1.3 队列保护**

timer 到期时必须再次检查：

- `message_queue` 是否为空；
- `feedback_queue` 是否为空；
- `_interrupt_event` 是否已置位；
- Agent 主循环是否仍有效。

任一条件不满足时，取消本次唤醒。

### 4.2 随机 idle timer

**FR-2.1 随机时间区间**

Idle Wakeup 不使用完全固定周期。应提供可配置随机区间，例如：

- 有近期事件时：`IdleWakeupDelayWithEventsMin` ~ `IdleWakeupDelayWithEventsMax`；
- 无近期事件时：`IdleWakeupDelayNoEventsMin` ~ `IdleWakeupDelayNoEventsMax`；
- 如第一版难以在 timer 创建前判断是否有事件，也可先统一使用 `IdleWakeupDelayMin` ~ `IdleWakeupDelayMax`。

默认值已确认：

- 通用随机唤醒区间：2~5 分钟（120~300 秒）；
- 后续若区分有事件 / 无事件区间，也不得让默认唤醒频率明显高于当前区间，避免默认开启后造成持续 token 消耗。

**FR-2.2 timer 唯一性**

每个 Agent 同一时间最多只能有一个 idle wakeup timer。

当发生以下情况时，必须取消已有 timer：

- Agent 收到新的用户消息；
- Agent 收到新的环境反馈；
- Agent 被打断；
- Agent 开始新一轮 LangGraph 推理；
- Agent finish / remove。

**FR-2.3 避免自激循环**

空闲唤醒消息本身不应导致立即再次创建零间隔 timer。每次 idle wakeup 后必须重新走完整随机等待区间。

### 4.3 空闲唤醒消息

**FR-3.1 消息类型**

空闲唤醒应作为环境 / 时间类消息注入 Agent 的 `message_queue`，而不是走 `feedback_queue`。

`feedback_queue` 保持用于长期工具结果反馈；Idle Wakeup 只在 Agent 确认空闲后写入 `message_queue`，避免无意义打断正在运行的 LangGraph。

**FR-3.2 文本格式**

唤醒消息使用 `<空闲感知>` 标签，文本应轻量、非命令式。

有事件摘要时示例：

```text
<空闲感知>
你已经有一段时间没有新的消息或任务反馈。

近期世界中发生了一些变化：
- 12.5秒前，2. 按钮：Idle -> Pressed
- 9.1秒前，3. 电梯：Idle -> Moving
- 6.8秒前，4. 平台：MovingLeft -> MovingRight

你可以选择忽略这些变化，继续等待，观察周围，回想近期世界事件，或主动采取行动。
</空闲感知>
```

无事件摘要时示例：

```text
<空闲感知>
你已经有一段时间没有新的消息或任务反馈。
周围暂时没有明显的新变化。

你可以选择继续等待、观察周围，或主动做些什么。
</空闲感知>
```

**FR-3.3 不强迫行动**

唤醒消息必须包含“可以忽略 / 继续等待”的选项，不得要求 Agent 必须行动。

### 4.4 WorldEventLog 摘要拉取

**FR-4.1 拉取时机**

仅在 idle timer 到期且 Python 确认 Agent 仍空闲后，才尝试拉取 Unity WorldEventLog 摘要。

不得在每个世界事件发生时向 Python 广播完整事件。

**FR-4.2 摘要内容**

摘要只包含少量近期事件，默认最多 3 条。每条事件只需要轻量字段：

- 相对时间；
- 对象名；
- 旧状态；
- 新状态。

不应默认包含每条 WorldEventLog 的完整 `EventText` 环境快照。

**FR-4.3 自身事件过滤**

默认过滤 Agent 自身状态变化事件，避免唤醒消息反复出现 `小明：Idle -> Move`、`小明：Move -> Idle` 之类低价值回声。

如后续发现自身事件对特定场景有价值，应在技术方案中单独说明例外条件。

**FR-4.4 拉取失败降级**

如果 Unity 未连接、RPC 超时、摘要接口不可用或返回异常：

- 本次 idle wakeup 不失败；
- 生成无事件摘要版本的 `<空闲感知>`；
- 日志记录降级原因。

**FR-4.5 接口选择**

技术方案阶段需要确定以下实现方式之一：

- 新增轻量 WorldEventLog 摘要 RPC；
- 或在不新增协议的前提下复用现有 `get_world_event_log_cmd` 能力并做严格截断 / 摘要化。

PRD 倾向新增轻量摘要能力，因为完整 `get_world_event_log_cmd` 返回体较大，不适合作为 idle wakeup 默认路径。

### 4.5 配置与默认值

**FR-5.1 可配置项**

至少应支持以下配置项，具体位置由技术方案确定：

- 是否启用 Idle Wakeup；
- 随机唤醒最小时间；
- 随机唤醒最大时间；
- 无事件时的更长随机时间区间；
- 每次摘要最大事件数；
- WorldEventLog 摘要拉取超时时间；
- 是否过滤自身事件。

**FR-5.2 默认关闭或默认开启**

技术方案阶段需明确默认是否开启。若默认开启，应给出保守间隔，避免开发 / 联调期间频繁触发。

---

## 5. 非功能需求

- **轻量化**：本期实现不引入重型事件感知系统，不增加每事件 Python 广播压力。
- **可解释性**：日志应能解释为什么本次 timer 被创建、取消、触发或降级。
- **低侵入**：不改变现有工具 RPC、用户消息、环境反馈的基本语义。
- **可控随机**：随机时间应位于明确配置区间内，避免过短频繁唤醒或过长看不出效果。
- **容错**：Unity 事件摘要拉取失败时，Agent 仍可收到基础空闲感知。
- **成本控制**：默认只注入摘要，不注入完整 WorldEventLog。
- **UTF-8**：所有文档与代码改动均保持 UTF-8 编码。

---

## 6. 验收标准

- [ ] Agent 完成一轮 LangGraph 推理后，如队列为空，会创建一个随机 idle wakeup timer。
- [ ] timer 延迟位于配置的随机区间内，不是固定周期。
- [ ] timer 期间若收到新 message / feedback / interrupt，会取消或跳过本次唤醒。
- [ ] timer 到期时若 Agent 正在运行 LangGraph，不发送空闲唤醒。
- [ ] timer 到期且 Agent 仍空闲时，会向 Agent 注入 `<空闲感知>`。
- [ ] 空闲唤醒文本允许 Agent 忽略 / 继续等待，不强制行动。
- [ ] 若 Unity 可提供 WorldEventLog 摘要，唤醒消息最多包含配置数量的近期事件摘要。
- [ ] 摘要默认不包含完整 `EventText` 环境快照。
- [ ] 摘要默认过滤 Agent 自身状态变化事件。
- [ ] Unity 摘要拉取失败时，仍发送无事件版本的空闲感知，并记录日志。
- [ ] 不存在每个世界事件实时广播到 Python 的行为。
- [ ] Agent finish / remove 后不存在残留 idle timer 继续唤醒已结束 Agent。

---

## 7. 确认记录

| 议题 | 结论 |
|------|------|
| 版本号 | 确认为 `v0.21.3` |
| 默认启用 | Idle Wakeup 默认开启 |
| 随机唤醒时间 | 考虑默认开启后的 token 持续消耗，默认随机区间改为 2~5 分钟（120~300 秒） |
| WorldEventLog 摘要 | 新增轻量 WorldEventLog 摘要 RPC，不复用完整 `get_world_event_log_cmd` 作为默认路径 |
| 队列类型 | 空闲唤醒注入 `message_queue`；`feedback_queue` 保持用于长期工具结果反馈 |
| 首次启动 | Agent 第一次启动但尚未处理任何消息时，也启动 Idle Wakeup |
| 配置位置 | 使用独立配置文件 `Src/PythonServer/config/idle_wakeup.json` |

---

*本文档由 Cursor Agent 根据用户口述需求生成；PRD 已确认，技术方案确认前请勿直接改业务代码。*
