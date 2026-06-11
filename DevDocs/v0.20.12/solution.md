# 方案 — v0.20.12 Prompt 信息保存与动态上下文裁剪

> **状态**：已实现  
> **对应 PRD**：`v0.20.12/PRD.md`  
> **最后更新**：2026-06-11

---

## 1. 总体思路

本期涉及两个独立但相关的改动，均位于 `agent_interuptible.py` 及其直接依赖：

| 改动 | 影响范围 | 核心变更 |
|------|----------|----------|
| Prompt 文件保存 | `agent_interuptible.py` 的 `save_memory` 节点 + `chatbot` 节点 + `.env` | `pretty_print()` → `pretty_repr()` + 文件写入 |
| 动态上下文裁剪 | `agent_interuptible.py` 的 `chatbot` 节点 + 新增 `prompt_utils.py` + `.env` | 在 `ainvoke` 前按 token 预算裁剪 messages |

两个改动**不耦合**：Prompt 保存可独立上线，裁剪也可独立上线。但建议同步实现，因为 Prompt 保存的内容正好验证裁剪结果。

---

## 2. 改动一：Prompt 文件保存

### 2.1 数据流

```
chatbot 节点
  → 组装 prompt (prompt_template.ainvoke，使用裁剪后的 messages)
  → [开发模式] 对每条 message 调用 pretty_print()（终端打印）
  → llm_with_tools.ainvoke(prompt)

save_memory 节点
  → memory_manager.save_memory(...)
  → [开发模式] 用 state['messages']（全量，含本轮 AI Message）重新渲染完整 prompt
  → [开发模式] 对每条 message 调用 pretty_repr()，写入文件
```

关键设计：Prompt 保存发生在 `save_memory` 节点而非 `chatbot`，原因是：
- `save_memory` 是最终节点，此时 `state['messages']` 已包含本轮完整的 AI Message 和 ToolMessage。
- 如果在 `chatbot` 中收集 `pretty_repr()`，LLM 尚未返回最终回复，保存的内容缺少最后的 AI Message。
- 在 `save_memory` 中用 `state['messages']` 全量重新渲染，确保保存的是完整的本轮对话快照。

### 2.2 State 无需新增字段

保存逻辑完全在 `save_memory` 节点内部完成，无需通过 State 中转。`state['messages']` 本身已包含全量消息（LangGraph 的 `add_messages` reducer 累积所有历史），`save_memory` 可直接使用。

### 2.3 核心代码变更

#### `chatbot` 节点

```python
async def chatbot(state: State):
    # ... 现有逻辑：裁剪 + 组装 prompt ...

    # [开发模式] 终端打印（保留现有行为）
    if PROMPT_SAVE_ENABLED:
        for msg in prompt.messages:
            msg.pretty_print()

    response = await llm_with_tools.ainvoke(prompt)

    # ... 后续逻辑不变 ...

    return {
        "messages": [response],
        "mem_to_save": mem_to_save,
    }
```

#### `save_memory` 节点

```python
async def save_memory(state: State):
    name = state['name']
    mem_to_save = state['mem_to_save']
    curtime = await TimeSystem().aget_current_time()
    await memory_manager.save_memory(name, mem_to_save, curtime)

    # Prompt 保存：用全量 messages 重新渲染并写入文件
    if PROMPT_SAVE_ENABLED:
        await _save_prompt_log(state)

    return {
        "mem_to_save": "",
        "logged_tool_call_ids": [],
    }
```

#### `_save_prompt_log` 辅助函数

```python
from datetime import datetime

async def _save_prompt_log(state: State):
    """将本轮完整 prompt 写入文件"""
    name = state['name']
    curtime = await TimeSystem().aget_current_time()

    # 用全量 state['messages'] 重新渲染完整 prompt
    prompt = await prompt_template.ainvoke({
        "messages": state['messages'],
        "name": state['name'],
        "curtime": curtime,
        "mem_summary": state['mem_summary'],
        "mem_fact": state['mem_fact'],
        "mem_episode": state['mem_episode']
    })

    # 拼接 pretty_repr 文本
    content = ""
    for msg in prompt.messages:
        content += msg.pretty_repr() + "\n\n"

    # 使用实际时间作为文件名
    now = datetime.now()
    filename = now.strftime("%Y-%m-%d_%H-%M-%S")
    # 同一秒内多次推理时追加毫秒
    if os.path.exists(os.path.join(PROMPT_SAVE_DIR, name, filename + ".log")):
        filename += f"_{now.microsecond // 1000:03d}"

    save_dir = os.path.join(PROMPT_SAVE_DIR, name)
    os.makedirs(save_dir, exist_ok=True)
    filepath = os.path.join(save_dir, filename + ".log")

    with open(filepath, "w", encoding="utf-8") as f:
        f.write(content)
```

### 2.4 配置项

`.env` 新增：

```env
# Prompt 保存开关（开发模式）
PROMPT_SAVE_ENABLED=true

# Prompt 保存目录（相对于 PythonServer/）
PROMPT_SAVE_DIR=logs/prompts
```

`main.py` 或 `agent_interuptible.py` 启动时读取：

```python
PROMPT_SAVE_ENABLED = os.getenv("PROMPT_SAVE_ENABLED", "false").lower() == "true"
PROMPT_SAVE_DIR = os.path.join(os.path.dirname(__file__), "..", os.getenv("PROMPT_SAVE_DIR", "logs/prompts"))
```

### 2.5 `.gitignore` 处理

检查项目根目录 `.gitignore`，若 `logs/` 未被忽略，则追加 `logs/`。Prompt 文件属于本地调试产物，不应提交。

---

## 3. 改动二：动态上下文裁剪

### 3.1 架构决策

| 决策项 | 选择 | 理由 |
|--------|------|------|
| Token 计数器 | `tiktoken`（`cl100k_base`） | 已作为 langchain-openai 依赖安装；估算误差在 5% 以内，配合预留比例可覆盖 |
| 裁剪位置 | `chatbot` 节点内、`prompt_template.ainvoke` 前 | 裁剪仅影响 LLM 输入，不修改 State |
| 裁剪策略 | 从最新向前累加，保留最近的消息 | 最相关上下文通常在最近 |
| AIMessage+ToolMessage 原子性 | 裁剪时绑定处理 | 避免模型报错（缺少 tool response） |

### 3.2 新增文件：`prompt_utils.py`

路径：`Src/PythonServer/agent_framwork/utils/prompt_utils.py`

```python
"""Prompt 工具函数：动态上下文裁剪与 token 估算"""

import os
import tiktoken
from langchain_core.messages import (
    BaseMessage, HumanMessage, AIMessage, ToolMessage, SystemMessage, RemoveMessage
)

# --- 配置读取 ---

def _get_max_context_tokens() -> int:
    return int(os.getenv("AGENT_MAX_CONTEXT_TOKENS", "128000"))

def _get_context_reserve_ratio() -> float:
    return float(os.getenv("CONTEXT_RESERVE_RATIO", "0.85"))

def _get_output_reserve_tokens() -> int:
    return int(os.getenv("OUTPUT_RESERVE_TOKENS", "4096"))

# --- Token 估算 ---

_enc = None

def _get_encoder():
    global _enc
    if _enc is None:
        _enc = tiktoken.get_encoding("cl100k_base")
    return _enc

def estimate_tokens(text: str) -> int:
    """估算文本的 token 数"""
    if not text:
        return 0
    return len(_get_encoder().encode(text))

def estimate_message_tokens(message: BaseMessage) -> int:
    """估算单条消息的 token 数（content + tool_calls）"""
    tokens = estimate_tokens(message.content) if isinstance(message.content, str) else 0

    if isinstance(message, AIMessage) and message.tool_calls:
        for tc in message.tool_calls:
            tokens += estimate_tokens(tc.get("name", ""))
            tokens += estimate_tokens(str(tc.get("args", "")))

    # 每条消息的基础开销（角色标记、分隔符等）
    tokens += 4
    return tokens

# --- 工具定义 token 缓存 ---

_tools_token_cache = None

def get_tools_token_count(tools) -> int:
    """估算工具定义的 token 数（首次调用后缓存）"""
    global _tools_token_cache
    if _tools_token_cache is not None:
        return _tools_token_cache

    # 将工具 schema 序列化为 JSON 字符串后计数
    import json
    schema_text = json.dumps(
        [tool.get_input_schema().schema() for tool in tools],
        ensure_ascii=False
    )
    _tools_token_cache = estimate_tokens(schema_text)
    return _tools_token_cache

# --- 裁剪核心 ---

def trim_messages_by_token(
    messages: list[BaseMessage],
    system_prompt_tokens: int,
    tools_token_count: int,
) -> list[BaseMessage]:
    """
    按照token预算裁剪消息列表。

    参数:
        messages: 原始消息列表（不含 system prompt）
        system_prompt_tokens: system prompt 的 token 数
        tools_token_count: 工具定义的 token 数

    返回:
        裁剪后的消息列表
    """
    max_context = _get_max_context_tokens()
    reserve_ratio = _get_context_reserve_ratio()
    output_reserve = _get_output_reserve_tokens()

    # 可用于 messages 的 token 预算
    budget = int(max_context * reserve_ratio) - system_prompt_tokens - tools_token_count - output_reserve
    budget = max(budget, 0)

    # 标记消息组：AIMessage(tool_calls) + 对应 ToolMessage 为一组
    groups = _group_messages(messages)

    # 从最新组向前累加
    kept_groups = []
    total_tokens = 0
    last_human_idx = -1  # 最近一条 HumanMessage 所在组索引

    for i in range(len(groups) - 1, -1, -1):
        group_tokens = sum(estimate_message_tokens(m) for m in groups[i])
        if total_tokens + group_tokens > budget and len(kept_groups) >= 1:
            # 预算不足且至少保留了一组，停止
            break
        total_tokens += group_tokens
        kept_groups.insert(0, groups[i])

    # 确保至少保留最近一条 HumanMessage
    for i, group in enumerate(kept_groups):
        if any(isinstance(m, HumanMessage) for m in group):
            last_human_idx = i

    if last_human_idx == -1 and len(groups) > 0:
        # 找到原始 messages 中最近一条 HumanMessage 所在组
        for i in range(len(groups) - 1, -1, -1):
            if any(isinstance(m, HumanMessage) for m in groups[i]):
                kept_groups = groups[i:]
                break

    # 展平
    result = []
    for group in kept_groups:
        result.extend(group)

    # 过滤 RemoveMessage
    result = [m for m in result if not isinstance(m, RemoveMessage)]

    return result


def _group_messages(messages: list[BaseMessage]) -> list[list[BaseMessage]]:
    """
    将消息列表按组划分：AIMessage(tool_calls) + 紧随其后的 ToolMessage 为一组；
    其他消息各自为一组。
    """
    groups = []
    i = 0
    while i < len(messages):
        msg = messages[i]
        if isinstance(msg, AIMessage) and msg.tool_calls:
            group = [msg]
            # 收集紧随其后的 ToolMessage（按 tool_call_id 匹配）
            tool_call_ids = {tc["id"] for tc in msg.tool_calls}
            j = i + 1
            while j < len(messages) and isinstance(messages[j], ToolMessage):
                if messages[j].tool_call_id in tool_call_ids:
                    group.append(messages[j])
                j += 1
            groups.append(group)
            i = j
        else:
            groups.append([msg])
            i += 1
    return groups
```

### 3.3 `chatbot` 节点集成

```python
async def chatbot(state: State):
    name = state['name']
    mem_to_save = state['mem_to_save']
    cur_time = await TimeSystem().aget_current_time()

    # === 动态裁剪 ===
    # 先组装 system prompt 以估算其 token 数
    system_vars = {
        "name": state['name'],
        "curtime": cur_time,
        "mem_summary": state['mem_summary'],
        "mem_fact": state['mem_fact'],
        "mem_episode": state['mem_episode']
    }
    system_prompt_text = await _render_system_prompt(system_vars)
    system_tokens = estimate_tokens(system_prompt_text)
    tools_tokens = get_tools_token_count(tools)

    trimmed_messages = trim_messages_by_token(
        messages=state['messages'],
        system_prompt_tokens=system_tokens,
        tools_token_count=tools_tokens,
    )

    # 用裁剪后的 messages 组装 prompt
    prompt = await prompt_template.ainvoke({
        "messages": trimmed_messages,
        **system_vars
    })

    # ... 后续 LLM 调用逻辑不变 ...
```

### 3.4 System Prompt 估算辅助

需要在 `prompt_utils.py` 中增加一个函数，对 `prompt_template` 的 system 部分（不含 `{messages}`）做一次渲染以估算 token：

```python
async def estimate_system_prompt_tokens(prompt_template, system_vars: dict) -> int:
    """估算 system prompt（不含 messages）的 token 数"""
    # 用空 messages 列表渲染模板，取第一条 SystemMessage
    test_prompt = await prompt_template.ainvoke({"messages": [], **system_vars})
    if test_prompt.messages and isinstance(test_prompt.messages[0], SystemMessage):
        return estimate_message_tokens(test_prompt.messages[0])
    return 0
```

此方法有额外一次模板渲染开销，但仅在 `chatbot` 节点调用一次，可接受。如需优化，可在首次调用后缓存 system prompt token 数（假设 `mem_summary` / `mem_fact` / `mem_episode` 每轮变化不大，可取最近 N 轮最大值）。

### 3.5 配置项

`.env` 新增：

```env
# 模型上下文窗口大小（tokens）
AGENT_MAX_CONTEXT_TOKENS=128000

# 上下文保留比例（0.0~1.0）
CONTEXT_RESERVE_RATIO=0.85

# 输出预留 token 数
OUTPUT_RESERVE_TOKENS=4096
```

---

## 4. 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `agent_interuptible.py` | 修改 | `chatbot` 节点增加裁剪 + 开关控制终端打印；`save_memory` 节点增加 prompt 文件保存 |
| `agent_framwork/utils/prompt_utils.py` | 新增 | token 估算、消息分组、裁剪逻辑 |
| `.env` | 修改 | 新增 5 个配置项 |
| `.gitignore` | 可能修改 | 确保 `logs/` 被忽略 |
| `agent_framwork/utils/__init__.py` | 可能新增 | 包初始化（若 utils 包不存在） |

---

## 5. 不受影响的部分

- `memory_manager.py`：不改动（Prompt 保存独立于记忆系统）
- `base_tools.py`：不改动
- `main.py`：不改动（配置在 `agent_interuptible.py` 启动时读取）
- 协议 / `message.proto`：不改动
- Unity 侧：不改动

---

## 6. 风险与缓解

| 风险 | 影响 | 缓解 |
|------|------|------|
| tiktoken 估算与模型实际 tokenizer 有差异 | 裁剪后仍可能超限 | `CONTEXT_RESERVE_RATIO=0.85` 预留 15% 余量；极端情况下模型 API 会返回长度超限错误，Agent 会按现有错误处理重试 |
| `_render_system_prompt` 每轮额外调用一次模板 | 微小性能开销 | 一次 `ainvoke` 仅组装字符串，无 LLM 调用；如后续优化可缓存 |
| 同步文件写入阻塞事件循环 | `save_memory` 节点延迟增加 <1ms | 写入量在 KB~百 KB 级，影响极小；如需可改 `aiofiles` 异步写入 |
| 实际时间文件名冲突（同一秒内两次推理） | 极端情况下文件覆盖 | 追加毫秒后缀 `_ms`；实际场景极少 |
| `_save_prompt_log` 中额外调用 `prompt_template.ainvoke` | 每轮多一次模板渲染开销 | 仅组装字符串，无 LLM 调用；可接受 |

---

## 7. 测试策略

| 测试项 | 方法 |
|--------|------|
| Prompt 文件生成 | 运行 Agent 1 轮推理 → 检查 `logs/prompts/<Agent名>/` 下存在 `.log` 文件 |
| Prompt 文件内容完整性 | 对比 `.log` 文件与终端 `pretty_print()` 输出 |
| 生产模式关闭 | `PROMPT_SAVE_ENABLED=false` → 确认无文件生成、无终端打印 |
| 裁剪后不超限 | 构造 50 轮对话 → 检查 LLM 调用不报 token 超限错误 |
| 短对话不裁剪 | 3 轮对话 → 确认 `trimmed_messages == state['messages']` |
| AIMessage+ToolMessage 原子性 | 含工具调用的对话 → 确认裁剪后无孤立 AIMessage |
| State 完整性 | 裁剪后检查 `state['messages']` 仍为完整历史 |

---

## 8. 实现记录

| 日期 | 内容 |
|------|------|
| 2026-06-10 | 初始方案，待用户确认 |
| 2026-06-11 | 完成开发：新增 `prompt_utils.py`、修改 `agent_interuptible.py`（chatbot 裁剪 + 开关控制打印、save_memory prompt 保存）、`.env` 新增 5 个配置项、`.gitignore` 已有 `**/[Ll]ogs/` 覆盖 |
| 2026-06-11 | 修复：`communicate_to_user` 补齐 `@tool` 装饰器；回滚 `prompt_utils.py` 中 `get_tools_token_count` 的 BaseTool 兼容逻辑 |
| 2026-06-11 | 用户验收通过 |

---

*本文档由 Cursor Agent 根据 PRD 与代码分析生成，确认前请勿直接据此改代码。*
