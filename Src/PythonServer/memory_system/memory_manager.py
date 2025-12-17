import asyncio
import kuzu
from agent_framwork.base.singleton import singleton

# Graphiti 核心组件
from graphiti_core import Graphiti
from graphiti_core.driver.kuzu_driver import KuzuDriver
from graphiti_core.llm_client.openai_generic_client import OpenAIGenericClient
from graphiti_core.llm_client.config import LLMConfig
from graphiti_core.embedder.openai import OpenAIEmbedder, OpenAIEmbedderConfig
from graphiti_core.cross_encoder.openai_reranker_client import OpenAIRerankerClient

# 配置参数
model_api_base = "https://dashscope.aliyuncs.com/compatible-mode/v1"
model_api_key = "sk-7a3958e0fdf840e49a2edd83b25dd228"
model_name = "qwen-max"
embedding_model_name = "text-embedding-v4"
reranker_model_name = "gte-rerank-v2"
db_name = "db/graphiti.kuzu"

class _SharedKuzuDriver(KuzuDriver):
    def __init__(self, db_instance, conn_instance):
        # 1. 屏蔽父类初始化，防止重复打开文件
        # 2. 注入全局 DB 和 局部 Conn
        self.db = db_instance
        self.client = conn_instance

@singleton
class MemorySystem:
    def __init__(self):
        self._llm_config = LLMConfig(
                api_key=model_api_key, model=model_name,
                small_model=model_name, base_url=model_api_base
            )
        self._kuzu_db = None       # 全局 DB
        self._embedder = None
        self._reranker = None
        self._initialized = False
        self._init_lock = None

    async def initialize(self):
        if self._initialized: return self
        if self._init_lock is None: self._init_lock = asyncio.Lock()

        async with self._init_lock:
            if self._initialized: return self
            
            # 1. 初始化所有依赖组件
            self._embedder = OpenAIEmbedder(
                config=OpenAIEmbedderConfig(
                    api_key=model_api_key, embedding_model=embedding_model_name,
                    base_url=model_api_base
                )
            )
            self._reranker = OpenAIRerankerClient(
                config=LLMConfig(
                    api_key=model_api_key, model=reranker_model_name,
                    base_url=model_api_base
                )
            )

            # 2. 初始化全局 DB
            print("💾 [MemorySystem] 正在挂载全局数据库...")
            self._kuzu_db = kuzu.Database(db_name)

            # 必须在使用 Graphiti 之前加载 FTS 扩展
            print("🔌 [MemorySystem] 正在加载 FTS 扩展...")
            _init_conn = kuzu.AsyncConnection(self._kuzu_db)
            try:
                # 安装 FTS (只需运行一次，但多次运行无害)
                await _init_conn.execute("INSTALL FTS")
                # 加载 FTS (每次启动 DB 都需要)
                await _init_conn.execute("LOAD EXTENSION FTS")
                print("✅ [MemorySystem] FTS 扩展加载成功")
            except Exception as e:
                print(f"❌ [MemorySystem] FTS 扩展加载失败: {e}")
                raise e
            
            # 3. 首次建索引 (内部封装，外部无感)
            # 强制限制最大并发查询数为 1，确保串行执行写操作
            temp_conn = kuzu.AsyncConnection(self._kuzu_db, max_concurrent_queries=1)
            temp_driver = _SharedKuzuDriver(self._kuzu_db, temp_conn)
            print("🏗️ [MemorySystem] 正在检查/创建表结构 (Schema)...")
            try:
                # 这一步会执行 CREATE NODE TABLE IF NOT EXISTS ...
                temp_driver.setup_schema() 
            except Exception as e:
                print(f"⚠️ Schema setup warning (可忽略): {e}")
            temp_graphiti = Graphiti(
                graph_driver=temp_driver,
                llm_client=OpenAIGenericClient(config=self._llm_config),
                embedder=self._embedder,
                cross_encoder=self._reranker
            )
            print("⏳ [MemorySystem] 检查数据库索引...")
            await temp_graphiti.build_indices_and_constraints()
            print("✅ [MemorySystem] 数据库完全就绪")
            
            self._initialized = True
        return self

    # 【核心修改】直接返回组装好的一对对象
    def create_session(self):
        """
        工厂方法：为 Agent 创建一个新的会话。
        返回: (kuzu.Connection, Graphiti)
        """
        if not self._initialized:
            raise RuntimeError("MemoryManager 未初始化")

        # 1. 创建属于这个会话的独立连接
        # 限制并发数为 1，降低多 Agent 写入冲突的概率
        conn = kuzu.AsyncConnection(self._kuzu_db, max_concurrent_queries=1)
        # 2. 内部组装 Graphiti
        driver = _SharedKuzuDriver(self._kuzu_db, conn)

        graphiti = Graphiti(
            graph_driver=driver,
            llm_client=OpenAIGenericClient(config=self._llm_config),
            embedder=self._embedder,
            cross_encoder=self._reranker
        )
        
        # 3. 同时返回两者
        return conn, graphiti