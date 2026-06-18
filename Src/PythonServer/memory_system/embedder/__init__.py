# -*- coding: utf-8 -*-
"""embedder 包：共享的 OpenAI Embedding / Reranker 客户端。

向外暴露：
    - SafeBatchOpenAIEmbedder：带 batch-size 限制的 Graphiti OpenAIEmbedder 子类
    - SafeBatchOpenAIReranker：带 batch-size 限制的 Graphiti reranker 子类
    - EmbedderService：单例，持有共享的 embedder/reranker 实例

业务模块（MemoryManager、ActionSkillManager 等）通过 EmbedderService 取共享实例，
避免在各模块内重复实例化、配置漂移。
"""
from .safe_batch_embedder import SafeBatchOpenAIEmbedder
from .safe_batch_reranker import SafeBatchOpenAIReranker
from .embedder_service import EmbedderService

__all__ = [
    "SafeBatchOpenAIEmbedder",
    "SafeBatchOpenAIReranker",
    "EmbedderService",
]
