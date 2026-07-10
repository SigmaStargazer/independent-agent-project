# 技术方案 - v0.22.2 idle wakeup 无信息量心理活动抑制写入长期记忆

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-07-10

---

## 1. 方案概述

采用纯方案 C（入口侧标记，不改动 prompt）：

1. **入口侧标记**：`Agent._enqueue_idle_wakeup_message` 在入队 idle wakeup 消息时附加 `skip_memory=True` 标记；`aprocess_message` 将该标记传入 LangGraph `input_state`；`save_memory` 节点读到 `skip_memory=True` 时跳过写入。
2. **工具调用解除 skip**：`cache_tool_mem` 节点检测到本轮有工具调用时将 `skip_memory` 置 `False`，确保 idle 触发真实行动时记忆正常写入。

---

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | `agent_framwork/base/timed_message.py` | 新增 `skip_memory` 字段 |
| Python | `agent_framwork/agents/agent_interuptible.py` - `State` | 新增 `skip_memory` 字段 |
| Python | `agent_framwork/agents/agent_interuptible.py` - `aprocess_message` | 从 `TimedMessage.skip_memory` 传入 `input_state` |
| Python | `agent_framwork/agents/agent_interuptible.py` - `save_memory` | 读取 `skip_memory`，为 True 时跳过写入 |
| Python | `agent_framwork/agents/agent_interuptible.py` - `cache_tool_mem` | 检测到工具调用时将 `skip_memory` 置 False |
| Python | `agent_framwork/agents/agent_interuptible.py` - `_enqueue_idle_wakeup_message` | 入队时设置 `skip_memory=True` |
| Unity | 无 | 不改动 |
| 协议 | `Tools/message.proto` | 无 |

---

## 3. 详细设计

### 3.1 数据：`TimedMessage` 扩展

```python
# agent_framwork/base/timed_message.py
@dataclass(order=True)
class TimedMessage:
    timestamp: float
    content: str
    skip_memory: bool = False  # 新增，默认 False
```

`skip_memory` 不参与排序（`order=True` 只按声明顺序的前两个字段排序），不影响现有 `items.sort()` 逻辑。

> **注意**：`dataclass(order=True)` 的排序行为是按字段声明顺序逐个比较。当前 `TimedMessage` 有 `timestamp` + `content` 两个字段参与排序。新增 `skip_memory` 作为第三个字段，在 `timestamp` 和 `content` 都相等时才会参与比较，实际不会影响排序结果。但为保险起见，实现时需实测验证 `items.sort()` 行为不变。

### 3.2 数据：`State` 扩展

```python
class State(TypedDict):
    # ... 现有字段 ...
    skip_memory: bool  # 新增：本轮是否跳过记忆写入
```

### 3.3 Python（Brain）- 各节点改动

#### 3.3.1 `_enqueue_idle_wakeup_message`（入口标记）

```python
await self.message_queue.put(TimedMessage(
    timestamp=real_time,
    content=message,
    skip_memory=True  # 新增
))
```

#### 3.3.2 `aprocess_message`（传递标记）

在 drain 合并消息后，从 `items` 中判断 `skip_memory`：

```python
# 任一消息携带 skip_memory=True，则本轮 skip_memory=True
skip_memory = any(getattr(item, 'skip_memory', False) for item in items)

input_state = {
    "messages": [human_msg],
    "name": self.name,
    "skip_memory": skip_memory  # 新增
}
```

**混合消息场景处理**：如果 idle wakeup 和用户消息/反馈同时积压被合并：
- `skip_memory` 初始为 `True`（因为 idle wakeup 携带了标记）。
- 但用户消息/反馈通常触发打断，Agent 大概率会调用工具响应 -> `cache_tool_mem` 会将 `skip_memory` 置 `False`。
- 即使 Agent 未调工具，用户消息本身有信息量，`skip_memory=True` 可能误跳过。**但这只在「idle wakeup 和用户消息在同一 drain 窗口」时发生，且 Agent 未调工具」时才误判**，概率极低，且该轮心理活动信息量本身不高。可接受。

#### 3.3.3 `save_memory`（旁路写入）

```python
async def save_memory(state: State):
    name = state['name']
    mem_to_save = state['mem_to_save']

    # 新增：idle wakeup 且无工具调用时跳过写入
    if state.get('skip_memory', False):
        print(f"[{name}] skip memory (idle wakeup, no action)")
        return {
            "mem_to_save": "",
            "logged_tool_call_ids": []
        }

    # 以下逻辑不变
    await aperf_print(f"[{name}]存储记忆开始")
    curtime = await TimeSystem().aget_current_time()
    await memory_manager.save_memory(name=state['name'], memory=mem_to_save, curtime=curtime)
    await aperf_print(f"[{name}]存储记忆任务启动，后台进行中")

    if PROMPT_SAVE_ENABLED:
        await _save_prompt_log(state, curtime)

    return {
        "mem_to_save": "",
        "logged_tool_call_ids": []
    }
```

#### 3.3.4 `cache_tool_mem`（工具调用解除 skip）

在 `cache_tool_mem` 节点末尾返回值中，若 `new_entries` 非空（有新工具调用），则将 `skip_memory` 置 `False`：

```python
async def cache_tool_mem(state: State):
    # ... 现有逻辑 ...

    # 先确定 skip_memory：有工具调用时解除 skip
    skip_memory = state.get('skip_memory', False)
    if new_entries:
        skip_memory = False

    return {
        "mem_to_save": mem_to_save,
        "logged_tool_call_ids": list(logged_ids),
        "skip_memory": skip_memory
    }
```

#### 3.3.5 `_initialize_resume_state`（打断恢复兼容）

`_resume_state` 需要补上 `skip_memory` 字段，打断恢复时默认置 `False`（恢复后的轮次按正常逻辑处理）：

```python
self._resume_state = {
    # ... 现有字段 ...
    "skip_memory": False,  # 新增
}
```

---

## 4. 实现步骤

1. 扩展 `TimedMessage`，新增 `skip_memory: bool = False`。
2. 扩展 `State`，新增 `skip_memory: bool`。
3. 修改 `_enqueue_idle_wakeup_message`，入队时 `skip_memory=True`。
4. 修改 `aprocess_message`，从 `items` 读取 `skip_memory` 传入 `input_state`。
5. 修改 `save_memory` 节点，`skip_memory=True` 时跳过写入。
6. 修改 `cache_tool_mem` 节点，有工具调用时 `skip_memory=False`。
7. 修改 `_initialize_resume_state`，补上 `skip_memory` 字段。
8. 编写 `pytest` 测试并运行通过。

---

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `TimedMessage` 新增字段影响 `items.sort()` | `skip_memory` 作为第三字段，仅在 timestamp+content 都相等时参与比较；实现时实测验证 |
| 混合消息（idle+用户）误跳过记忆 | 概率极低（需同 drain 窗口且 Agent 不调工具）；用户消息通常触发打断+工具调用，`cache_tool_mem` 会解除 skip |
| 旧 checkpoint 无 `skip_memory` 字段 | 全部使用 `state.get('skip_memory', False)` 读取，默认 False，行为与当前一致 |
| 回退 | 全部改动集中在 `timed_message.py` + `agent_inteructible.py`，git revert 即可回退 |

---

## 6. 测试用例矩阵

### 6.1 测试目标

验证 `skip_memory` 标记在 idle wakeup 场景下正确抑制记忆写入，且不误伤正常场景。

### 6.2 前置条件

- Python 环境可用，`agent_framwork` 模块可 import。
- 不需要 Unity 联调；mock `MemoryManager.save_memory` 和 LLM 调用。

### 6.3 测试用例

| # | 测试场景 | 输入 | 期望输出 | 覆盖风险 |
|---|----------|------|----------|----------|
| T1 | idle wakeup 无工具调用 | `TimedMessage(skip_memory=True)`，Agent 返回纯文本无 tool_calls | `save_memory` 节点跳过 `memory_manager.save_memory` 调用；打印 `skip memory` 日志 | 核心场景：idle 无行动时不写记忆 |
| T2 | idle wakeup 有工具调用 | `TimedMessage(skip_memory=True)`，Agent 返回 `tool_calls`（如 observe_cmd） | `cache_tool_mem` 将 `skip_memory` 置 False；`save_memory` 正常调用 `memory_manager.save_memory` | idle 触发行动时记忆正常写入 |
| T3 | 普通用户消息无工具调用 | `TimedMessage(skip_memory=False)`，Agent 返回纯文本 | `save_memory` 正常调用 | 非 idle 消息不受影响 |
| T4 | 普通用户消息有工具调用 | `TimedMessage(skip_memory=False)`，Agent 返回 tool_calls | `save_memory` 正常调用 | 非 idle 消息不受影响 |
| T5 | 反馈消息无工具调用 | `TimedMessage(skip_memory=False)`（feedback），Agent 返回纯文本 | `save_memory` 正常调用 | 反馈消息不受影响 |
| T6 | 混合消息（idle+用户）Agent 调工具 | items 同时含 `skip_memory=True` 和 `False`，Agent 返回 tool_calls | `skip_memory` 初始 True，`cache_tool_mem` 置 False，`save_memory` 正常调用 | 混合窗口下工具调用解除 skip |
| T7 | 混合消息（idle+用户）Agent 不调工具 | items 同时含 `skip_memory=True` 和 `False`，Agent 返回纯文本 | `skip_memory` 为 True，`save_memory` 跳过 | 混合窗口下无行动时跳过（可接受行为） |
| T8 | TimedMessage 排序不变 | 构造多条 `TimedMessage`（含 `skip_memory=True/False`），执行 `items.sort()` | 排序结果仅按 timestamp+content，不受 skip_memory 影响 | dataclass 排序兼容性 |
| T9 | 打断恢复后 skip_memory 重置 | 触发 `ainterrupt` -> `astart` 恢复 | `_resume_state` 中 `skip_memory=False`，恢复后正常写入 | 打断恢复兼容性 |

### 6.4 测试方式

- T1~T7：直接调用 LangGraph 图节点函数（`search_memory` / `chatbot` / `cache_tool_mem` / `save_memory`），mock `memory_manager.save_memory` 为 `AsyncMock`，验证调用次数与参数。
- T8：构造 `TimedMessage` 列表，`items.sort()`，验证顺序。
- T9：构造 `Agent` 实例，mock LLM，触发 `ainterrupt` + `astart`，检查 `_resume_state`。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-07-10 | 完成纯方案 C 实现：TimedMessage 新增 skip_memory 字段；_enqueue_idle_wakeup_message 入队标记 True；aprocess_message 聚合标记传入 input_state；save_memory 节点 skip 旁路；cache_tool_mem 工具调用解除 skip；_initialize_resume_state 补 skip_memory=False。16 个 pytest 全部通过。联调验证：idle wakeup 纯文本轮次跳过记忆写入（无 prompt 日志文件生成），有工具调用轮次正常写入；终端无主键冲突报错。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
