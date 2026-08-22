# -*- coding: utf-8 -*-
"""API 连通性测试模块（v0.23.1）：零系统探测。

Title 阶段「测试后保存」后，Unity 携带当前面板未落盘的 base/key/model，
Python 据此**临时构造**轻量探测客户端发一次最小请求，验证该配置是否可用。

设计要点：
    - 零系统：不初始化 MemoryManager / EmbedderService / Agent，不发 InitRequest，
      仅在本模块内构造一次性客户端，用完即弃。
    - 与 api_config_loader.py 同域（config/）：本模块属于「API 配置验证」而非生命周期编排。
    - 超时独立 30s（比运行时 120s 短），超时即判不可用。
"""
from __future__ import annotations

import asyncio

from langchain_core.messages import HumanMessage
from langchain_openai import ChatOpenAI
from graphiti_core.embedder.openai import OpenAIEmbedder, OpenAIEmbedderConfig
from graphiti_core.llm_client.config import LLMConfig

from memory_system.embedder.safe_batch_reranker import SafeBatchOpenAIReranker

TEST_TIMEOUT = 30.0  # 秒


async def test_api_connectivity(category: str, api_base: str, api_key: str, model: str) -> tuple[bool, str]:
    """零系统探测：临时构造客户端发最小请求，验证 api 连通性。

    参数：
        category: llm | embedding | rerank（Unity 面板对应测试类型）
        api_base / api_key / model: 当前面板文本框里**未落盘**的配置

    返回：
        (success, errormsg)；success=False 时 errormsg 为给用户看的失败原因。
    """
    try:
        if category == "llm":
            llm = ChatOpenAI(
                model_name=model,
                openai_api_base=api_base,
                openai_api_key=api_key,
                streaming=False,
                request_timeout=TEST_TIMEOUT,
                max_retries=0,
            )
            resp = await asyncio.wait_for(
                llm.ainvoke([HumanMessage(content="ping")]),
                timeout=TEST_TIMEOUT,
            )
            content = getattr(resp, "content", None)
            return (True, "") if content else (False, "模型未返回内容")
        elif category == "embedding":
            emb = OpenAIEmbedder(config=OpenAIEmbedderConfig(
                api_key=api_key,
                embedding_model=model,
                base_url=api_base,
            ))
            vecs = await asyncio.wait_for(emb.create(["ping"]), timeout=TEST_TIMEOUT)
            return (True, "") if vecs else (False, "embedding 未返回向量")
        elif category == "rerank":
            # 用与 Graphiti 运行时一致的 SafeBatchOpenAIReranker（LLM chat 二分类，
            # 已关闭 thinking 使 Reasoning 模型可用）。测试结果如实反映运行时可用性。
            rer = SafeBatchOpenAIReranker(config=LLMConfig(
                api_key=api_key,
                model=model,
                base_url=api_base,
            ))
            out = await asyncio.wait_for(rer.rank("ping", ["ping"]), timeout=TEST_TIMEOUT)
            return (True, "") if out is not None else (False, "rerank 未返回结果")
        else:
            return (False, f"未知测试类型: {category}")
    except asyncio.TimeoutError:
        return (False, f"测试超时（>{TEST_TIMEOUT}s）")
    except Exception as e:
        return (False, f"测试失败: {e}")
