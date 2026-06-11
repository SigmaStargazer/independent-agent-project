# PRD — v0.20.12 Prompt 信息保存与动态上下文裁剪

> **状态**：已确认  
> **对应需求**：`requirements/prompt信息保存.md`  
> **最后更新**：2026-06-11

---

## 1. 背景与目标

`agent_interuptible.py` 的 `chatbot` 节点通过 `message.pretty_print()` 将完整 prompt 打印到终端，供开发者调试。存在两个问题：

| 问题 | 现状 |
|------|------|
| **终端输出丢失** | 编辑器终端有行数上限，长对话中早期 prompt 无法回溯浏览 |
| **上下文裁剪僵化** | `_filter_messages(k=20)` 已废弃，当前 `chatbot` 直接传完整 `state['messages']`；固定裁剪条数容易爆 max_tokens 或浪费 token 容量 |

**本期目标**：

1. 将每轮推理的完整 prompt 写入文件，供人类/AI 开发者持久查阅。
2. 实现基于 token 估算的动态上下文裁剪，替代固定条数方案。

---

## 2. 范围

### 2.1 本期包含

- **Prompt 文件保存**：每轮推理结束后，将完整 prompt（System + 所有 Messages）以 `pretty_repr()` 格式写入文件。
- **存储目录与命名**：`<根目录>/<Agent名>/<虚拟时间>.<后缀>`。
- **开发/生产模式开关**：`.env` 新增配置项，仅开发模式启用保存。
- **动态上下文裁剪**：在 `chatbot` 节点中，调用 LLM 前按 token 预算裁剪 `messages`，保留 system prompt + 最近的 messages，确保不超模型上下文窗口。
- **裁剪预算可配置**：`.env` 中配置模型上下文窗口大小与保留比例。

### 2.2 本期不包含

- 上下文压缩/摘要方案（如用 LLM 压缩历史消息）。
- Unity 侧改动。
- 协议 / `message.proto` 变更。
- Prompt 文件的自动清理/轮转（可后续按需增加）。
- 将保存的 prompt 文件用于 Agent 记忆回溯（本期仅面向开发者查阅）。

---

## 3. 用户与场景

|| 角色 | 场景 | 期望结果 |
||------|------|----------|
|| 开发者 | Agent 运行多轮后需要回看第 3 轮的完整 prompt | 在 `<根目录>/<Agent名>/` 下找到对应时间戳文件，内容完整可读 |
|| 开发者 | 切换到生产环境部署 | `.env` 关闭保存开关，不产生文件 IO |
|| AI 开发者 | 排查 Agent 推理异常 | 文件中包含完整的 system prompt、记忆注入、消息序列，可逐条对照 |
|| Agent | 长对话（20+ 轮含多步工具调用） | 动态裁剪后不超过模型 max_tokens，同时尽量利用可用上下文窗口 |
|| Agent | 短对话（2-3 轮） | 裁剪后保留完整上下文，不丢失任何消息 |

---

## 4. 功能需求

### 4.1 Prompt 文件保存

#### 4.1.1 保存时机

在 `save_memory` 节点中，完成记忆存储后，将本轮 `state['messages']` 的完整 prompt 写入文件。选择 `save_memory` 而非 `chatbot` 的原因：

- `save_memory` 是每轮推理的最终节点，此时 `messages` 包含完整的工具调用与响应链。
- 不影响 LLM 调用链路的性能（文件 IO 在推理完成后执行）。

#### 4.1.2 存储目录与命名

```
<prompt_save_dir>/<Agent名>/<实际时间>.log
```

- `prompt_save_dir`：由 `.env` 中 `PROMPT_SAVE_DIR` 配置，默认为 `logs/prompts`（相对于 `PythonServer/` 目录）。
- `<Agent名>`：`state['name']`，如 `小亮`。
- `<实际时间>`：使用系统实际时间（`datetime.now()`），格式 `YYYY-MM-DD_HH-MM-SS`（精确到秒；若同一秒内多次推理，追加毫秒后缀 `_{ms:03d}`）。
- 后缀：`.log`（详见 4.1.3）。

选择实际时间而非虚拟时间的原因：文件名的目的是让开发者按时间顺序查找日志，实际时间更直观；虚拟时间格式不统一、可能包含非法字符，且调试时开发者关心的是真实的运行时序。

#### 4.1.3 存储格式：`.log`

选择 `.log` 而非 `.txt`：

| 对比项 | `.log` | `.txt` |
|--------|--------|--------|
| 语义 | 明确表示「运行时日志/调试输出」 | 通用文本，语义模糊 |
| 工具生态 | 编辑器/日志查看器默认高亮、过滤、tail | 仅纯文本查看 |
| `.gitignore` | 通常已忽略 `logs/` 或 `*.log` | 可能误提交 |
| 本项目已有 | `logs/` 目录已存在 | 无约定 |

内容格式：复用 `chatbot` 中 prompt 拼装逻辑，对每个 message 调用 `pretty_repr()` 生成格式化文本。

#### 4.1.4 存储内容

保存的是 `save_memory` 节点执行时 `state['messages']` 的**全量消息**（含本轮新产生的 AI Message 和 ToolMessage），重新通过 `prompt_template.ainvoke` 渲染为完整 prompt 后写入文件：

```python
# 在 save_memory 节点中
prompt = await prompt_template.ainvoke({
    "messages": state['messages'],  # 全量 messages，含本轮 AI 回复
    "name": state['name'],
    "curtime": cur_time,
    "mem_summary": state['mem_summary'],
    "mem_fact": state['mem_fact'],
    "mem_episode": state['mem_episode']
})
for message in prompt.messages:
    text = message.pretty_repr()
    file.write(text + "\n\n")
```

这样保存的是**本轮推理结束后的完整 prompt 快照**（含 system prompt、记忆注入、全量 messages），而非 `chatbot` 节点调用 LLM 前的中间状态。

#### 4.1.5 开发/生产模式开关

`.env` 新增：

```
# Prompt 保存开关（开发模式）
PROMPT_SAVE_ENABLED=true
```

- `true`：启用保存。
- `false` 或未配置：不保存，不产生文件 IO。

`chatbot` 中的调试 `pretty_print()` 也受此开关控制：启用时打印 + 保存，关闭时均不执行。

### 4.2 动态上下文裁剪

#### 4.2.1 核心思路

在 `chatbot` 节点调用 `prompt_template.ainvoke` **之前**，根据 token 预算裁剪 `state['messages']`，保留最近的消息直到总 token 数不超过预算。

裁剪流程：

1. 计算可用 token 预算 = `模型上下文窗口大小` × `保留比例` - `system prompt 估算 token` - `工具定义估算 token` - `输出预留 token`。
2. 从最新消息向前累加 token 数，直到达到预算。
3. 丢弃超出预算的早期消息，保留最近的消息。

#### 4.2.2 配置项

`.env` 新增：

```
# 模型上下文窗口大小（tokens）
AGENT_MAX_CONTEXT_TOKENS=128000
# 上下文保留比例（0.0~1.0，扣除 system prompt / 工具定义 / 输出预留后可用部分的比例）
CONTEXT_RESERVE_RATIO=0.85
# 输出预留 token 数
OUTPUT_RESERVE_TOKENS=4096
```

- `AGENT_MAX_CONTEXT_TOKENS`：模型的上下文窗口大小，如 GLM-5.1 为 128K。
- `CONTEXT_RESERVE_RATIO`：预留给输入的比例（扣除输出和可能的网络开销）。
- `OUTPUT_RESERVE_TOKENS`：为 LLM 输出预留的 token 数。

#### 4.2.3 Token 估算方式

使用 `tiktoken`（已作为 langchain-openai 的依赖安装）按 `cl100k_base` 编码器估算 token 数：

- 对每条 `message`，将其 `content`（文本部分）+ `tool_calls`（名称 + 参数 JSON）序列化为字符串后计数。
- `system_template` 填充后的完整文本作为 system prompt token 估算。
- 工具定义的 token 数可通过首次启动时 `bind_tools` 后的 schema 估算一次，缓存结果。

#### 4.2.4 裁剪规则

- **System prompt**（含 `mem_summary`、`mem_fact`、`mem_episode`、`curtime`）：**始终保留**，不参与裁剪。
- **Messages**：从最新向最早遍历，按 token 累加，超出预算时截断。
- **AIMessage + ToolMessage 对**：如果一条 AIMessage 包含 `tool_calls`，则对应的 `ToolMessage` 必须与 AIMessage 一起保留或一起丢弃（不能保留 AIMessage 但丢弃其 ToolMessage，否则 LLM 会报错）。
- **最少保留**：裁剪后至少保留最近 1 条 HumanMessage（确保 LLM 有输入可响应）。

#### 4.2.5 裁剪位置

在 `chatbot` 节点中，`prompt_template.ainvoke` 之前执行裁剪：

```python
async def chatbot(state: State):
    name = state['name']
    mem_to_save = state['mem_to_save']
    cur_time = await TimeSystem().aget_current_time()

    # 动态裁剪 messages
    trimmed_messages = trim_messages_by_token(
        state['messages'],
        system_prompt_state={
            "name": state['name'],
            "curtime": cur_time,
            "mem_summary": state['mem_summary'],
            "mem_fact": state['mem_fact'],
            "mem_episode": state['mem_episode']
        }
    )

    prompt = await prompt_template.ainvoke({
        "messages": trimmed_messages,  # 使用裁剪后的消息
        "name": state['name'],
        "curtime": cur_time,
        "mem_summary": state['mem_summary'],
        "mem_fact": state['mem_fact'],
        "mem_episode": state['mem_episode']
    })
    # ... 后续逻辑不变
```

注意：裁剪仅影响传给 LLM 的输入，**不修改 `state['messages']`**（LangGraph 的 `add_messages` reducer 仍保留完整历史）。

---

## 5. 非功能需求

- **文件编码**：所有保存文件 UTF-8。
- **IO 性能**：prompt 保存为同步文件写入（单次写入，量级在 KB~百 KB），不阻塞 Agent 推理循环。
- **磁盘占用**：每轮 prompt 文件约 5~50KB；开发者可手动清理，后续可增加自动轮转。
- **裁剪准确性**：token 估算为近似值（tiktoken 与模型实际 tokenizer 存在差异），预留比例确保实际不超限。
- **向后兼容**：`_filter_messages` 和 `MAX_CONTEXT_SIZE` 标记为废弃，本期不删除但不再使用。

---

## 6. 验收标准

- [ ] 开发模式下，Agent 每轮推理完成后在 `<prompt_save_dir>/<Agent名>/` 下生成 `.log` 文件，文件名为实际时间（`YYYY-MM-DD_HH-MM-SS`）。
- [ ] `.log` 文件内容包含完整 system prompt 和全量 messages（含本轮最后一条 AI Message）。
- [ ] 生产模式（`PROMPT_SAVE_ENABLED=false`）下不产生文件 IO，`chatbot` 中不打印 prompt。
- [ ] 长对话（30+ 轮）推理时，传给 LLM 的 messages 不超过 `AGENT_MAX_CONTEXT_TOKENS × CONTEXT_RESERVE_RATIO` tokens。
- [ ] 短对话（3 轮以内）不被裁剪，保留完整上下文。
- [ ] AIMessage + ToolMessage 原子性：裁剪后不存在「有 AIMessage 但无对应 ToolMessage」的情况。
- [ ] 裁剪后至少保留 1 条 HumanMessage。
- [ ] `state['messages']` 不受裁剪影响（LangGraph checkpoint 仍保留完整历史）。
- [ ] 切换不同模型（如从 GLM-5.1 改为 qwen-max）时，调整 `.env` 中 `AGENT_MAX_CONTEXT_TOKENS` 即可适配。

---

## 7. 待确认问题

- [x] 虚拟时间格式中包含 `:` 等文件名非法字符，需确认替换方案（当前建议 `:` → `-`）。→ **已改用实际时间，此问题不再存在**
- [x] 是否需要为 prompt 保存目录增加 `.gitignore`（`logs/` 可能已在项目级忽略中）。→ **必须检查，确保 logs/ 被忽略，防止上传远端**
- [x] 裁剪后是否需要在日志中记录裁剪信息（如 `裁剪前 N 条 messages / M tokens → 裁剪后 K 条 / L tokens`）。→ **不需要**

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
