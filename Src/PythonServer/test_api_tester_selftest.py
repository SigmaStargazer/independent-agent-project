# -*- coding: utf-8 -*-
"""v0.23.1 config/api_tester 自测脚本（临时，验收后可删）。

设计意图（用户确认）：rerank 模型是配给 Graphiti 用的，用 OpenAIRerankerClient（LLM chat 二分类）
测试该配置能否用于 Graphiti 重排序。测试结果如实反映运行时可用性。

覆盖：
1. llm / embedding 合法配置 -> (True, "")
2. 非法 Key -> (False, errmsg)
3. 未知类型 -> (False, errmsg)
4. 测试后系统仍为零状态
（rerank 路径此前已单独实测：兼容模型 qwen-turbo -> True；gte-rerank-v2 -> False 如实反映不可用于 Graphiti）
"""
import asyncio
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from config.api_tester import test_api_connectivity


def load_cfg():
    path = os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "Data", "Config", "api_config.json")
    )
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


async def main():
    cfg = load_cfg()
    cases = [
        ("llm",       cfg["AGENT_API_BASE"],     cfg["AGENT_API_KEY"],     cfg["AGENT_MODEL"]),
        ("embedding", cfg["EMBEDDING_API_BASE"], cfg["EMBEDDING_API_KEY"], cfg["EMBEDDING_MODEL"]),
        ("llm",       cfg["AGENT_API_BASE"],     "sk-invalid-key-12345",   cfg["AGENT_MODEL"]),
        ("unknown",   "http://x", "k", "m"),
    ]
    for cat, base, key, model in cases:
        ok, err = await test_api_connectivity(cat, base, key, model)
        print(f"[{cat:9}] success={ok} errormsg={err!r}")

    from memory_system import MemoryManager
    from memory_system.embedder import EmbedderService
    from agent_framwork.managers.agent_manager import AgentManager
    print("--- 零系统状态 ---")
    print("MemoryManager.is_initialized =", MemoryManager().is_initialized)
    print("EmbedderService.is_initialized =", EmbedderService().is_initialized)
    print("Agent 数 =", len(AgentManager().agents) if hasattr(AgentManager(), "agents") else "n/a")


if __name__ == "__main__":
    asyncio.run(main())
