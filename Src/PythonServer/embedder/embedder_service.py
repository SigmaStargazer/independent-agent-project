# -*- coding: utf-8 -*-
"""EmbedderService: 共享的 Embedding / Reranker 客户端单例。

职责：
    - 从 .env 读取 EMBEDDING_* / RERANKER_* 配置
    - 实例化 SafeBatchOpenAIEmbedder 与 SafeBatchOpenAIReranker
    - 让所有业务模块（MemoryManager、ActionSkillManager 等）共享同一组实例

不管：
    - 业务调用（embed 单条 / batch / rerank 由各业务自行决定）
    - 模型选择策略（一个进程一组模型）
"""
from __future__ import annotations

import asyncio
import os
from typing import Optional

from dotenv import load_dotenv
from graphiti_core.embedder.openai import OpenAIEmbedderConfig
from graphiti_core.llm_client.config import LLMConfig

from agent_framwork.base.singleton import singleton
from embedder.safe_batch_embedder import SafeBatchOpenAIEmbedder
from embedder.safe_batch_reranker import SafeBatchOpenAIReranker

load_dotenv()


@singleton
class EmbedderService:
    def __init__(self):
        self._embedder: Optional[SafeBatchOpenAIEmbedder] = None
        self._reranker: Optional[SafeBatchOpenAIReranker] = None
        self._initialized: bool = False
        self._init_lock: Optional[asyncio.Lock] = None

    @property
    def is_initialized(self) -> bool:
        return self._initialized

    def get_embedder(self) -> SafeBatchOpenAIEmbedder:
        if self._embedder is None:
            raise RuntimeError("EmbedderService 尚未初始化，先调用 await initialize()")
        return self._embedder

    def get_reranker(self) -> SafeBatchOpenAIReranker:
        if self._reranker is None:
            raise RuntimeError("EmbedderService 尚未初始化，先调用 await initialize()")
        return self._reranker

    async def initialize(self) -> "EmbedderService":
        if self._initialized:
            return self
        if self._init_lock is None:
            self._init_lock = asyncio.Lock()

        async with self._init_lock:
            if self._initialized:
                return self

            embedding_api_base = os.getenv("EMBEDDING_API_BASE")
            embedding_api_key = os.getenv("EMBEDDING_API_KEY")
            embedding_model = os.getenv("EMBEDDING_MODEL")

            reranker_api_base = os.getenv("RERANKER_API_BASE")
            reranker_api_key = os.getenv("RERANKER_API_KEY")
            reranker_model = os.getenv("RERANKER_MODEL")

            self._embedder = SafeBatchOpenAIEmbedder(
                config=OpenAIEmbedderConfig(
                    api_key=embedding_api_key,
                    embedding_model=embedding_model,
                    base_url=embedding_api_base,
                ),
                max_batch_size=10,
            )

            self._reranker = SafeBatchOpenAIReranker(
                config=LLMConfig(
                    api_key=reranker_api_key,
                    model=reranker_model,
                    base_url=reranker_api_base,
                ),
                max_batch_size=10,
            )

            self._initialized = True
            print("✅ [EmbedderService] initialized")
        return self
