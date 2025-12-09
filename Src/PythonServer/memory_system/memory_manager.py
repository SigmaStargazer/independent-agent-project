import asyncio

from graphiti_core import Graphiti
from graphiti_core.driver.kuzu_driver import KuzuDriver # kuzu配置

from graphiti_core.llm_client.openai_generic_client import OpenAIGenericClient
from graphiti_core.llm_client.config import LLMConfig
from graphiti_core.embedder.openai import OpenAIEmbedder, OpenAIEmbedderConfig
from graphiti_core.cross_encoder.openai_reranker_client import OpenAIRerankerClient

from agent_framwork.base.singleton import singleton
import kuzu

model_api_base = "https://dashscope.aliyuncs.com/compatible-mode/v1"
model_api_key = "sk-7a3958e0fdf840e49a2edd83b25dd228"
model_name = "qwen-max"
embedding_model_name = "text-embedding-v4"
reranker_model_name = "gte-rerank-v2"

# 数据库名
db_name = "db/graphiti.kuzu"

@singleton
class MemoryManager:
    def __init__(self):
        self.graphiti = None # graphiti
        self._initialized = False
        self._init_lock = None
        self.kuzu_driver = None
        self.conn = None

    @property
    def connection(self):
        if self.conn is None:
            raise RuntimeError("MemoryManager 未初始化！请先调用 await initialize()")
        return self.conn

    async def initialize(self):
        if self._initialized:
            return self

        if self._init_lock is None:
            self._init_lock = asyncio.Lock()

        async with self._init_lock:
            if self._initialized:
                return self

            llm_config = LLMConfig(
                api_key=model_api_key,
                model=model_name,
                small_model=model_name,
                base_url=model_api_base,
            )

            self.kuzu_driver = KuzuDriver(
                db=db_name
            )
            self.conn = self.kuzu_driver.client

            self.graphiti = Graphiti(
                graph_driver=self.kuzu_driver,
                llm_client=OpenAIGenericClient(config=llm_config),
                embedder=OpenAIEmbedder(
                    config=OpenAIEmbedderConfig(
                        api_key=model_api_key,
                        embedding_model=embedding_model_name,
                        base_url=model_api_base,
                    )
                ),
                cross_encoder=OpenAIRerankerClient(
                    config=LLMConfig(
                        api_key=model_api_key,
                        model=reranker_model_name,
                        base_url=model_api_base
                    )
                )
            )
            await self._init_graphiti()
            self._initialized = True
        return self

    async def _init_graphiti(self):
        """初始化 Graphiti 的索引和约束"""
        try:
            await self.graphiti.build_indices_and_constraints()
            print("✅ Graphiti_kuzu索引和约束已构建完成")
        except Exception as e:
            print(f"❌ 初始化失败: {e}")
            raise

    async def close(self):
        """显式关闭资源，释放 Kuzu 文件锁"""
        import gc # 引入垃圾回收

        if not self._initialized:
            return

        print("🛑 [1/3] 正在关闭连接池...")
        try:
            # 1. 显式关闭 AsyncConnection (这会停止后台线程池)
            if self.conn:
                if hasattr(self.conn, 'close'):
                    self.conn.close() # 这一步至关重要，它会等待线程结束
            
            # 2. 切断引用链 (让引用计数归零)
            self.conn = None
            
            # 如果 kuzu_driver 持有 db，也要手动切断
            if self.kuzu_driver:
                self.kuzu_driver.db = None # 释放 Database 对象引用
                self.kuzu_driver = None
            
            self.graphiti = None
            self._db = None 

            # 3. 【核心绝招】强制垃圾回收
            # Python 的 GC 是懒惰的，必须手动踢一脚
            # 只有 Database 对象被 GC 销毁，文件锁才会释放
            gc.collect()
            
            self._initialized = False
            print("✅ [2/3] Python 对象引用已清理")
            print("✅ [3/3] 垃圾回收完成，数据库锁应已释放")
            
        except Exception as e:
            print(f"⚠️ 关闭资源时出错: {e}")