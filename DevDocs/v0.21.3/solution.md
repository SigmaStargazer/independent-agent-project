# 技术方案 — v0.21.3 Idle Wakeup 空闲随机唤醒

> **状态**：已实现  
> **依据 PRD**：`PRD.md`  
> **关联功能设计**：`DevDocs/feature-design/IdleWakeup.md`  
> **最后更新**：2026-06-19

---

## 1. 方案概述

在 Python `Agent` 运行时中加入一个轻量 Idle Wakeup 调度器：Agent 启动或完成一轮 LangGraph 推理后，如果没有待处理消息，则创建 2~5 分钟（120~300 秒）随机 timer；timer 到期时再次确认 Agent 未运行且队列为空，再通过轻量 WorldEventLog 摘要 RPC 向 Unity 拉取最多 3 条近期世界事件摘要，并把 `<空闲感知>` 作为普通消息写入 `message_queue` 唤醒 Agent。

方案核心约束：

- **默认开启**，随机等待时间默认 **2~5 分钟（120~300 秒）**；
- 由 **Python 侧** 判断 LangGraph 是否空闲；
- 唤醒消息走 **`message_queue`**，不走 `feedback_queue`；
- 新增轻量 **WorldEventLog 摘要 RPC**，不复用完整 `get_world_event_log_cmd`；
- 第一次 Agent 启动但尚未处理任何消息时，也启动 Idle Wakeup；
- 不做事件广播、显著性评分、注意力系统。

---

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| 文档 | `DevDocs/feature-design/IdleWakeup.md` | 已新增长期功能原则 |
| 文档 | `DevDocs/v0.21.3/PRD.md` | 已确认需求与决策 |
| 协议 | `Tools/message.proto` | 新增轻量 `AgentGetWorldEventSummaryRequest` |
| 协议生成 | `Tools/1.genproto.cmd` → `MessageDispatch.cs` → Rebuild → `2.copyprotocol.cmd` | 执行生成与拷贝 |
| Python | `agent_framwork/agents/agent_interuptible.py` | 增加 idle timer、空闲判断、唤醒消息注入 |
| Python | `agent_framwork/tools/base_tools.py` 或新 helper | 增加内部摘要 RPC 调用函数；不注册为 Agent 工具 |
| Python | `config/idle_wakeup.json` | 新增 Idle Wakeup 独立配置文件 |
| Unity | `AIPlayer.cs` | 新增 `GetWorldEventSummary`，从 `mWorldEventLog` 生成轻量摘要 |
| Unity | `RuntimeInfoRenderer.cs` | 可选：抽出 `RenderWorldEventSummary` 格式化方法 |
| Unity | `AgentService.cs` | 订阅新 request，新增 `OnGetWorldEventSummary` 事件 |
| Unity | `AgentManager.cs` | 路由到目标 `AIPlayer.GetWorldEventSummary` |
| Unity | `MessageDispatch.cs`（Lib/Common） | 生成后包含新 request 分发 |

---

## 3. 配置方案分析

用户进一步确认：考虑默认开启后的 token 持续消耗，Idle Wakeup 默认随机区间改为 **2~5 分钟（120~300 秒）**，配置位置使用独立配置文件：

```text
Src/PythonServer/config/idle_wakeup.json
```

### 3.1 `.env` 方案优劣

优点：

- 与当前 PythonServer 的模型、记忆、embedding 等配置习惯一致。
- 实现最轻，不需要新增 JSON 读取逻辑。
- 适合部署环境差异较大的密钥、URL、模型名、超时等参数。

缺点：

- 可读性不如结构化配置，功能参数多了会散落在 `.env` 中。
- `.env` 通常更像“机器/部署配置”，不适合承载带设计含义的玩法参数。
- 后续若需要 per-Agent、per-scene 或策划可调策略，表达能力不足。

适合推荐给：

- 纯运行环境参数；
- 私密配置；
- 不希望纳入版本管理的机器差异。

### 3.2 独立配置文件方案优劣

使用路径：

```text
Src/PythonServer/config/idle_wakeup.json
```

默认内容建议：

```json
{
  "enabled": true,
  "delay_min_seconds": 120,
  "delay_max_seconds": 300,
  "summary_max_events": 3,
  "summary_timeout_seconds": 5,
  "ignore_self_events": true
}
```

优点：

- 结构清晰，能直接表达 Idle Wakeup 的功能参数。
- 可纳入版本管理，团队共享默认行为。
- 后续扩展到 per-Agent / per-scene / 多策略时更自然。
- 玩法参数与 `.env` 中的密钥、模型等部署参数分离，更容易维护。

缺点：

- 比 `.env` 多一点读取、默认值合并、异常处理逻辑。
- 当前 PythonServer 还没有统一业务配置加载器，本期需要新增一个轻量读取 helper。
- 对极少量配置项来说，比 `.env` 稍重。

适合推荐给：

- 玩法 / 行为策略参数；
- 需要提交默认值的功能配置；
- 未来可能扩展结构的功能。

### 3.3 本期决定

本期使用 **独立配置文件 `Src/PythonServer/config/idle_wakeup.json`**。

理由：

1. Idle Wakeup 是 Agent 行为机制，不是模型密钥或部署参数；
2. 默认开启后会持续消耗 token，默认值应明确纳入版本管理，便于审查；
3. 后续可能根据 Agent、场景或测试阶段调不同唤醒策略，JSON 更易扩展；
4. 虽然比 `.env` 稍重，但配置读取可以保持很薄，不违背轻量化原则。

本期默认值：

| 配置项 | 默认值 | 说明 |
|------|------:|------|
| `enabled` | `true` | 默认开启 |
| `delay_min_seconds` | `120` | 最小随机等待秒数，2 分钟 |
| `delay_max_seconds` | `300` | 最大随机等待秒数，5 分钟 |
| `summary_max_events` | `3` | 每次最多摘要事件数 |
| `summary_timeout_seconds` | `5` | 摘要 RPC 超时秒数 |
| `ignore_self_events` | `true` | 默认过滤 Agent 自身事件 |

边界处理：

- 配置文件不存在时，使用上述默认值，并打印提示；
- JSON 解析失败时，使用默认值，并打印错误；
- 若 `delay_min_seconds <= 0`，回退到 120；
- 若 `delay_max_seconds < delay_min_seconds`，令 `delay_max_seconds = delay_min_seconds`；
- 若 `summary_max_events < 0`，回退到 3；
- 若 `summary_timeout_seconds <= 0`，回退到 5。

---

## 4. 详细设计

### 4.1 协议设计

新增轻量摘要 request：

```protobuf
message AgentGetWorldEventSummaryRequest {
    string agent = 1;
    string request_id = 2;
    int32 max_events = 3;
    bool ignore_self_events = 4;
}
```

在 `NetMessageRequest` 追加新字段，字段号使用当前最大字段号之后的下一个可用值。当前 `agentGetWorldEventLogRequest = 30`，且 `agentExportSkillsRequest = 31` 已占用，因此方案使用下一个可用字段号：

```protobuf
AgentGetWorldEventSummaryRequest agentGetWorldEventSummaryRequest = 32;
```

返回仍复用现有工具结果通道：Unity 调用 `SendToolResultMessage(agent, "GetWorldEventSummary", request_id, result)`，Python 侧使用 `TOOL_WAITERS[request_id]` 等待结果。

返回文本格式：

```text
[世界事件摘要]
总摘要数: 3
- 12.5秒前，2. 按钮：Idle -> Pressed
- 9.1秒前，3. 电梯：Idle -> Moving
- 6.8秒前，4. 平台：MovingLeft -> MovingRight
```

无可用事件：

```text
[世界事件摘要]
总摘要数: 0
近期没有明显的新变化。
```

注意：摘要 RPC 是 Python 内部 Idle Wakeup 使用的轻量能力，不注册到 `agent_interuptible.tools`，避免 Agent 把它当作可主动调用工具；Agent 主动回想完整世界事件仍使用 `get_world_event_log_cmd`。

### 4.2 Python：Idle Wakeup 配置

新增配置文件：

```text
Src/PythonServer/config/idle_wakeup.json
```

默认内容：

```json
{
  "enabled": true,
  "delay_min_seconds": 120,
  "delay_max_seconds": 300,
  "summary_max_events": 3,
  "summary_timeout_seconds": 5,
  "ignore_self_events": true
}
```

建议在 `agent_interuptible.py` 中新增轻量读取 helper，或抽到后续可复用的配置模块。由于本期目标仍是轻量化，不强制引入完整配置框架。

读取结果可封装为简单 dataclass / dict，例如：

```python
@dataclass(frozen=True)
class IdleWakeupConfig:
    enabled: bool = True
    delay_min_seconds: float = 120
    delay_max_seconds: float = 300
    summary_max_events: int = 3
    summary_timeout_seconds: float = 5
    ignore_self_events: bool = True
```

加载时需要处理：文件不存在、JSON 解析失败、字段类型错误、数值越界。异常时使用默认值并打印日志，不阻止 Agent 启动。

### 4.3 Python：Agent 新增运行时字段

在 `Agent.__init__` 中增加：

```python
self._idle_wakeup_task: asyncio.Task | None = None
self._is_graph_running = False
self._idle_wakeup_seq = 0
```

字段语义：

| 字段 | 作用 |
|------|------|
| `_idle_wakeup_task` | 当前挂起的 idle timer；同一 Agent 只能有一个 |
| `_is_graph_running` | 标记当前是否正在执行 `graph.ainvoke` |
| `_idle_wakeup_seq` | 可选调试序号，便于日志识别 timer 创建 / 取消 / 触发 |

### 4.4 Python：timer 生命周期

新增方法建议：

```python
def _cancel_idle_wakeup(self, reason: str) -> None:
    ...


def _schedule_idle_wakeup(self, reason: str) -> None:
    ...


async def _idle_wakeup_after_delay(self, seq: int, delay: float) -> None:
    ...
```

调度规则：

1. `astart()` 成功启动 `aprocess_message()` 后，如果 `IDLE_WAKEUP_CONFIG.enabled` 为 true，则调用 `_schedule_idle_wakeup("agent_started")`，覆盖“第一次 Agent 启动但尚未处理任何消息”的场景。
2. 外部 `asend_message()` / `asend_feedback()` 进入 `_asend_message(...)` 时，先 `_cancel_idle_wakeup("external_input")`，避免 timer 在新输入到达后继续触发。
3. `aprocess_message()` 从队列取到输入后，也调用 `_cancel_idle_wakeup("queue_input")`，作为内部兜底。
4. `graph.ainvoke(...)` 开始前设置 `_is_graph_running = True`。
5. `graph.ainvoke(...)` 结束或异常退出后设置 `_is_graph_running = False`。
6. 一轮成功结束后，如果 `message_queue.empty()` 且 `feedback_queue.empty()`，调用 `_schedule_idle_wakeup("round_finished")`。
7. `ainterrupt()` / `afinish()` / 主循环退出时取消 timer。

伪代码：

```python
def _schedule_idle_wakeup(self, reason: str):
    if not IDLE_WAKEUP_CONFIG.enabled:
        return
    if self._idle_wakeup_task and not self._idle_wakeup_task.done():
        return
    delay = random.uniform(
        IDLE_WAKEUP_CONFIG.delay_min_seconds,
        IDLE_WAKEUP_CONFIG.delay_max_seconds,
    )
    self._idle_wakeup_seq += 1
    seq = self._idle_wakeup_seq
    self._idle_wakeup_task = asyncio.create_task(
        self._idle_wakeup_after_delay(seq, delay)
    )
    print(f"[{self.name}] idle wakeup scheduled seq={seq} delay={delay:.1f}s reason={reason}")
```

```python
async def _idle_wakeup_after_delay(self, seq: int, delay: float):
    try:
        await asyncio.sleep(delay)
        if self._is_graph_running:
            return
        if self._interrupt_event.is_set():
            return
        if not self.message_queue.empty() or not self.feedback_queue.empty():
            return
        summary = await fetch_world_event_summary_for_idle_wakeup(...)
        msg = self._build_idle_wakeup_message(summary)
        await self._enqueue_idle_wakeup_message(msg)
    except asyncio.CancelledError:
        return
```

### 4.5 Python：写入 `message_queue`

用户已确认：Idle Wakeup 走 `message_queue`，`feedback_queue` 保持用于长期工具结果反馈。

这里不直接调用现有 `asend_message()`，主要不是性能原因，而是语义原因：当前 `asend_message()` 会统一执行“记录消息频率 → 判断是否打断 → 必要时 `ainterrupt()` → 入队 → 必要时 `astart()`”这一套外部消息入口逻辑。Idle Wakeup 触发前已经确认 Agent 空闲，不需要再走打断 / 重启流程；否则代码语义上会让“空闲唤醒”看起来像一次外部打断。

因此本期推荐新增内部方法，只负责按现有消息格式写入 `message_queue`：

```python
async def _enqueue_idle_wakeup_message(self, content: str) -> None:
    real_time = time.time()
    virtual_time = await TimeSystem().aget_current_time(to_str=True)
    text = content if virtual_time == "未启动" else f"[{virtual_time}]" + content
    await self.message_queue.put(TimedMessage(timestamp=real_time, content=text))
```

具体队列元素使用现有 `TimedMessage(timestamp=real_time, content=text)`，并与 `asend_message` 的虚拟时间前缀格式保持一致。这样 `aprocess_message()` 正在等待 `message_queue.get()` 时会自然被唤醒，且不会把 Idle Wakeup 混入外部消息打断语义。

### 4.6 Python：拉取 WorldEventLog 摘要

新增内部 helper，不注册为工具：

```python
async def fetch_world_event_summary_for_idle_wakeup(
    agent: str,
    max_events: int,
    ignore_self_events: bool,
    timeout: float,
) -> str:
    request_id = f"idle_wakeup:{agent}:{uuid.uuid4()}"
    loop = asyncio.get_running_loop()
    fut = loop.create_future()
    TOOL_WAITERS[request_id] = fut
    try:
        request = message_pb2.AgentGetWorldEventSummaryRequest()
        request.agent = agent
        request.request_id = request_id
        request.max_events = max_events
        request.ignore_self_events = ignore_self_events
        await AgentServerNetMessage().broadcast_message(request)
        return await asyncio.wait_for(fut, timeout=timeout)
    finally:
        TOOL_WAITERS.pop(request_id, None)
```

降级策略：

- 超时、Unity 未连接、异常：返回空字符串或 `[世界事件摘要]\n总摘要数: 0\n近期没有明显的新变化。`；
- `_build_idle_wakeup_message()` 根据摘要是否为空生成有事件 / 无事件文案；
- 日志打印降级原因，不抛出影响 timer。

### 4.7 Python：空闲感知文案

有摘要：

```text
<空闲感知>
你已经有一段时间没有新的消息或任务反馈。

近期世界中发生了一些变化：
{summary_lines}

你可以选择忽略这些变化，继续等待，观察周围，回想近期世界事件，或主动采取行动。
</空闲感知>
```

无摘要 / 拉取失败：

```text
<空闲感知>
你已经有一段时间没有新的消息或任务反馈。
周围暂时没有明显的新变化。

你可以选择继续等待、观察周围，或主动做些什么。
</空闲感知>
```

注意：文案不应要求 Agent 必须行动，也不应直接指令调用某个工具。

### 4.8 Unity：WorldEventLog 摘要生成

在 `AIPlayer.cs` 中新增：

```csharp
public void GetWorldEventSummary(string requestId, int maxEvents, bool ignoreSelfEvents)
{
    var renderer = new RuntimeInfoRenderer();
    string text = renderer.RenderWorldEventSummary(
        mWorldEventLog,
        Name,
        maxEvents,
        ignoreSelfEvents
    );

    AgentService.Instance.SendToolResultMessage(
        Name,
        "GetWorldEventSummary",
        requestId,
        text
    );
}
```

摘要选择规则：

- 按时间从新到旧筛选，最多取 `maxEvents` 条；
- 若 `ignoreSelfEvents == true`，过滤 `record.ObjectName == Name`；
- 输出时可按旧→新排列，便于阅读短时间线；
- 每条只输出 `Time.time - record.Time`、`ObjectName`、`OldState`、`NewState`；
- 不输出 `EventText`。

推荐格式化方法放到 `RuntimeInfoRenderer.cs`：

```csharp
public string RenderWorldEventSummary(
    IEnumerable<WorldEventRecord> records,
    string selfName,
    int maxEvents,
    bool ignoreSelfEvents)
```

如果希望改动更少，也可以直接在 `AIPlayer.GetWorldEventSummary` 中构造字符串；但从职责上看，渲染逻辑放 `RuntimeInfoRenderer` 更一致。

### 4.9 Unity：服务路由

`AgentService.cs`：

- 新增事件：

```csharp
public event UnityAction<string, string, int, bool> OnGetWorldEventSummary;
```

- `OnEnable` 订阅 `AgentGetWorldEventSummaryRequest`；
- `OnDisable` 取消订阅；
- 新增 handler：

```csharp
void OnAgentGetWorldEventSummary(object sender, AgentGetWorldEventSummaryRequest request)
{
    Debug.LogFormat($"OnAgentGetWorldEventSummary::Agent:{request.Agent} RequestId:{request.RequestId} MaxEvents:{request.MaxEvents} IgnoreSelf:{request.IgnoreSelfEvents}");
    this.OnGetWorldEventSummary?.Invoke(request.Agent, request.RequestId, request.MaxEvents, request.IgnoreSelfEvents);
}
```

`AgentManager.cs`：

- 订阅 / 取消订阅 `OnGetWorldEventSummary`；
- 路由到 `AIPlayer.GetWorldEventSummary(requestId, maxEvents, ignoreSelfEvents)`。

### 4.10 与现有工具的关系

| 能力 | 使用方 | 返回内容 | 是否注册给 Agent |
|------|--------|----------|------------------|
| `get_world_event_log_cmd` | Agent 主动调用 | 完整世界事件日志，含环境快照 | 是 |
| `AgentGetWorldEventSummaryRequest` | Idle Wakeup 内部调用 | 近期少量轻量摘要 | 否 |

这样能保持：

- Agent 想深入调查时仍有完整日志工具；
- Idle Wakeup 默认上下文保持轻量；
- 不把内部唤醒摘要能力暴露成新的 Agent 工具，避免工具列表膨胀。

---

## 5. 实现步骤

1. **协议**：修改 `Tools/message.proto`，新增 `AgentGetWorldEventSummaryRequest` 与 `NetMessageRequest` 字段 32。
2. **协议生成**：运行 `Tools/1.genproto.cmd`，更新 `MessageDispatch.cs`，Rebuild `CSharpClient.sln`，运行 `Tools/2.copyprotocol.cmd`。
3. **Unity 服务接线**：更新 `AgentService.cs` 与 `AgentManager.cs`，完成 request 分发。
4. **Unity 摘要生成**：在 `AIPlayer.cs` 增加 `GetWorldEventSummary`；在 `RuntimeInfoRenderer.cs` 增加或内联摘要格式化。
5. **Python 内部 RPC**：新增 `fetch_world_event_summary_for_idle_wakeup` helper，使用 `TOOL_WAITERS` 等待结果。
6. **Python 配置**：新增 `Src/PythonServer/config/idle_wakeup.json`，并在 Python 侧读取、校验、合并默认值。
7. **Python Agent 调度**：在 `Agent` 中新增 timer 字段、调度 / 取消 / 触发方法。
8. **Python 主循环接入**：`astart()` 后调度首次 idle wakeup；取到新输入时取消；`graph.ainvoke` 前后维护 `_is_graph_running`；一轮结束后重新调度。
9. **生命周期清理**：`ainterrupt()`、`afinish()`、主循环退出时取消 idle timer，防止残留任务。
10. **日志与调试**：补充 timer 创建、取消、触发、摘要拉取失败日志。

---

## 6. 风险与回退

| 风险 | 缓解 |
|------|------|
| Idle Wakeup 过于频繁导致 Agent 反复自激或 token 持续消耗过高 | 默认 2~5 分钟随机区间；同一 Agent 只允许一个 timer；唤醒后重新走完整随机等待 |
| 唤醒时 LangGraph 实际仍在运行 | 用 `_is_graph_running`、队列空检查、`_interrupt_event` 进行二次确认 |
| 直接写 `message_queue` 绕过现有 `asend_message` 逻辑 | 仅内部空闲注入使用；统一封装 `_enqueue_idle_wakeup_message`，保留时间戳格式 |
| Unity 摘要 RPC 超时导致 timer 卡住 | `asyncio.wait_for` 使用配置项 `summary_timeout_seconds`，超时降级无事件唤醒 |
| 协议新增字段与现有字段冲突 | 使用当前最大字段号之后的 32；生成后检查 diff |
| WorldEventLog 为空时 Agent 仍被唤醒 | 属设计预期；文本明确“可以继续等待”，且频率由随机区间控制 |
| 独立配置读取失败导致 Agent 启动异常 | 配置文件不存在、JSON 解析失败、字段错误时全部回退默认值并打印日志 |
| Agent finish 后残留 timer | `afinish()` / 主循环 finally 中取消 `_idle_wakeup_task` |

**回退方案**：

- 将 `Src/PythonServer/config/idle_wakeup.json` 中 `enabled` 设为 `false` 即可关闭 Python 侧唤醒；
- 若需要代码级回退，移除 timer 调度逻辑与新摘要 RPC；现有 `get_world_event_log_cmd` 不受影响；
- Unity 侧新增摘要 RPC 可安全保留，即使 Python 不调用也不会影响现有流程。

---

## 7. 测试建议

### 7.1 Python 单侧可测

- 临时把 `config/idle_wakeup.json` 中 `delay_min_seconds=1`、`delay_max_seconds=2`，启动 Agent 后不发送消息，确认首次 idle wakeup 会进入 `message_queue`。
- Agent 正在 `graph.ainvoke` 时，确认 timer 到期不会注入消息。
- timer 创建后手动向 `message_queue` 放入用户消息，确认 idle wakeup 不重复插入。
- 调用 `afinish()` 后等待超过最大 delay，确认没有残留 timer 继续唤醒。

### 7.2 Unity / Python 联调

- 场景中触发按钮、电梯、平台等 WorldEventLog 事件，等待 Agent 空闲唤醒，确认 `<空闲感知>` 中最多出现 3 条摘要。
- 确认摘要不包含完整 `<你的状态>`、`<当前场景>`、`<环境>` 快照。
- 确认默认过滤 Agent 自身 `Idle -> Move`、`Move -> Idle` 事件。
- Unity 不启动或断开时，确认 Python 仍生成无事件版本空闲感知，不崩溃。

### 7.3 回归测试

- `get_world_event_log_cmd` 原完整日志工具仍可由 Agent 主动调用。
- `feedback_queue` 中长期工具结果反馈不受 Idle Wakeup 影响。
- 用户消息、计时器、工具反馈仍能正常唤醒 / 打断 Agent。

---

## 8. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-19 | 已完成协议新增与生成、Unity WorldEventSummary RPC 链路、Python Idle Wakeup 配置读取与随机调度；已执行 Python 语法检查、Python 协议消息构造验证、MSBuild Rebuild 与 DLL 拷贝。 |
| 2026-06-20 | Unity 联调验收通过；确认 `StartAgentStep` 后可正常调度 Idle Wakeup，`enabled=false` 可用于关闭唤醒计时。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
