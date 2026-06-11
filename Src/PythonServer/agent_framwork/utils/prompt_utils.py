"""Prompt 工具函数：动态上下文裁剪与 token 估算"""

import os
import json
import tiktoken
from langchain_core.messages import (
    BaseMessage, HumanMessage, AIMessage, ToolMessage, SystemMessage, RemoveMessage
)


# ---------------------------------------------------------------------------
# 配置读取
# ---------------------------------------------------------------------------

def _get_max_context_tokens() -> int:
    return int(os.getenv("AGENT_MAX_CONTEXT_TOKENS", "128000"))


def _get_context_reserve_ratio() -> float:
    return float(os.getenv("CONTEXT_RESERVE_RATIO", "0.85"))


def _get_output_reserve_tokens() -> int:
    return int(os.getenv("OUTPUT_RESERVE_TOKENS", "4096"))


# ---------------------------------------------------------------------------
# Token 估算
# ---------------------------------------------------------------------------

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
    tokens = 0

    # content
    if isinstance(message.content, str):
        tokens += estimate_tokens(message.content)
    elif isinstance(message.content, list):
        for block in message.content:
            if isinstance(block, str):
                tokens += estimate_tokens(block)
            elif isinstance(block, dict):
                text = block.get("text", "")
                if text:
                    tokens += estimate_tokens(text)

    # tool_calls
    if isinstance(message, AIMessage) and message.tool_calls:
        for tc in message.tool_calls:
            tokens += estimate_tokens(tc.get("name", ""))
            tokens += estimate_tokens(json.dumps(tc.get("args", {}), ensure_ascii=False))

    # 每条消息的基础开销（角色标记、分隔符等）
    tokens += 4
    return tokens


# ---------------------------------------------------------------------------
# 工具定义 token 缓存
# ---------------------------------------------------------------------------

_tools_token_cache = None


def get_tools_token_count(tools) -> int:
    """估算工具定义的 token 数（首次调用后缓存）"""
    global _tools_token_cache
    if _tools_token_cache is not None:
        return _tools_token_cache

    schema_text = json.dumps(
        [tool.get_input_schema().schema() for tool in tools],
        ensure_ascii=False
    )
    _tools_token_cache = estimate_tokens(schema_text)
    return _tools_token_cache


# ---------------------------------------------------------------------------
# System prompt token 估算
# ---------------------------------------------------------------------------

async def estimate_system_prompt_tokens(prompt_template, system_vars: dict) -> int:
    """估算 system prompt（不含 messages）的 token 数"""
    test_prompt = await prompt_template.ainvoke({"messages": [], **system_vars})
    if test_prompt.messages and isinstance(test_prompt.messages[0], SystemMessage):
        return estimate_message_tokens(test_prompt.messages[0])
    return 0


# ---------------------------------------------------------------------------
# 消息分组
# ---------------------------------------------------------------------------

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


# ---------------------------------------------------------------------------
# 裁剪核心
# ---------------------------------------------------------------------------

def trim_messages_by_token(
    messages: list[BaseMessage],
    system_prompt_tokens: int,
    tools_token_count: int,
) -> list[BaseMessage]:
    """
    按照 token 预算裁剪消息列表。

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

    # 标记消息组
    groups = _group_messages(messages)

    # 从最新组向前累加
    kept_groups = []
    total_tokens = 0

    for i in range(len(groups) - 1, -1, -1):
        group_tokens = sum(estimate_message_tokens(m) for m in groups[i])
        if total_tokens + group_tokens > budget and len(kept_groups) >= 1:
            break
        total_tokens += group_tokens
        kept_groups.insert(0, groups[i])

    # 确保至少保留最近一条 HumanMessage
    has_human = any(
        isinstance(m, HumanMessage)
        for group in kept_groups
        for m in group
    )
    if not has_human and len(groups) > 0:
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
