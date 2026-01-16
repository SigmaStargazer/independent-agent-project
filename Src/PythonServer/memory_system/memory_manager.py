import os
import shutil
import asyncio
from datetime import datetime
from uuid import uuid4

from graphiti_core import Graphiti
from graphiti_core.driver.kuzu_driver import KuzuDriver # kuzu配置

from graphiti_core.nodes import EntityNode, EpisodeType
from graphiti_core.search import search_config_recipes

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
# db_name = "db/graphiti.kuzu"
db_path = "db"
db_name = "graphiti.kuzu"
db_backup_path = "db/backup"

class _SharedKuzuDriver(KuzuDriver):
    def __init__(self, db_instance, conn_instance):
        # 1. 屏蔽父类初始化，防止重复打开文件
        # 2. 注入全局 DB 和 局部 Conn
        self.db = db_instance
        self.client = conn_instance

@singleton
class MemoryManager:
    def __init__(self):
        self._llm_config = LLMConfig(
                api_key=model_api_key, model=model_name,
                small_model=model_name, base_url=model_api_base
            )
        self._initialized = False
        self._init_lock = None
        self._kuzu_db = None       # 全局
        self._kuzu_driver = None
        self.conn = None
        self.graphiti = None # graphiti
        self._embedder = None

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
            self._kuzu_db = kuzu.Database(os.path.join(db_path, db_name))
            
            # 3. 首次建索引 (内部封装，外部无感)
            # self.conn = kuzu.AsyncConnection(self._kuzu_db)
            self.conn = kuzu.AsyncConnection(self._kuzu_db, max_concurrent_queries=1)
            
            # 4. 【核心修正4】加载 FTS 扩展 (绝对不能删，否则无法建索引)
            try:
                # 第一次运行需要联网下载，之后本地加载
                await self.conn.execute("INSTALL FTS")
                await self.conn.execute("LOAD EXTENSION FTS")
                print("✅ [MemorySystem] FTS 扩展加载/验证成功")
            except Exception as e:
                print(f"❌ FTS 加载失败: {e}")
                raise e

            # 5. 组装 Graphiti
            self._kuzu_driver = _SharedKuzuDriver(self._kuzu_db, self.conn)
            print("🏗️ [MemorySystem] 正在检查/创建表结构 (Schema)...")
            try:
                # 这一步会执行 CREATE NODE TABLE IF NOT EXISTS ...
                self._kuzu_driver.setup_schema() 
            except Exception as e:
                print(f"⚠️ Schema setup warning (可忽略): {e}")
            self.graphiti = Graphiti(
                graph_driver=self._kuzu_driver,
                llm_client=OpenAIGenericClient(config=self._llm_config),
                embedder=self._embedder,
                cross_encoder=self._reranker
            )
            print("⏳ [MemorySystem] 检查数据库索引...")
            await self.graphiti.build_indices_and_constraints()
            print("✅ [MemorySystem] 数据库完全就绪")
            
            self._initialized = True
        return self


    async def init_agent_summary(self, name: str, summary: str, create_time: datetime):
        """
        初始化agent的简介
        Args:
            name(str): agent名称
            summary(str): agent的简介
            create_time(datetime): 创建时间
        """

        # 实例化实体节点
        uuid = str(uuid4())
        group_id = name.encode('utf-8').hex()
        my_entity = EntityNode(
            uuid=uuid,
            name="I",
            group_id=group_id,
            created_at=create_time,  # type: ignore
            summary=summary,
        )
        await my_entity.generate_name_embedding(self._embedder)
        # 保存实体节点
        await my_entity.save(self.graphiti.driver)

        # (临时)将summary给add_episode，生成初始图谱
        result = await self._save_memory(name=name, memory=summary, curtime=create_time, wait_result=True)
        # 删除summary所对应episode节点，只保留其生成的实体关系
        episode = result.episode
        await episode.delete(self.graphiti.driver)

    async def load_agent_summary(self, name: str) -> str:
        """
        获取agent的简介
        Args:
            name(str): 实体名称
        Return:
            str: agent的简介
        """
        summary = ""

        group_id = name.encode('utf-8').hex()
        cypher = f"""
        MATCH (n:Entity{{name: "I", group_id: "{group_id}"}})
        RETURN n
        """
        try:
            response = await self.conn.execute(cypher)
            for row in response.rows_as_dict():
                summary = row['n']['summary']
                break
        except Exception as e:
            print(f"加载智能体{name}简介失败: {e}")

        return summary

    async def _save_memory(self, name: str, memory: str, curtime: datetime, wait_result: bool = False):
        group_id = name.encode('utf-8').hex()
        try:
            if wait_result:
                result = await MemoryManager().graphiti.add_episode(
                    name=f"{name}_mem_{curtime}",
                    episode_body=memory,
                    source=EpisodeType.text,
                    source_description=f"{name}_mem_{curtime}", 
                    reference_time=curtime,
                    group_id=group_id
                )
                return result
            else:
                await MemoryManager().graphiti.add_episode(
                    name=f"{name}_mem_{curtime}",
                    episode_body=memory,
                    source=EpisodeType.text,
                    source_description=f"{name}_mem_{curtime}", 
                    reference_time=curtime,
                    group_id=group_id
                )
        except Exception as e:
            print(f"存储记忆失败: {e}")

    async def save_memory(self, name: str, memory: str, curtime: datetime,):
        """
        存储记忆
        Args:
            name(str): agent名称
            memory(str): 记忆
            curtime(datetime): 记忆产生时间
        """
        await self._save_memory(name=name, memory=memory, curtime=curtime, wait_result=False)

    async def search_fact_memory(self, name: str, query: str, limit: int = 1):
        """
        根据用户问题，检索事实记忆
        Args:
            name(str): agent名称
            query(str): 用户问题
            limit(int): 检索的记忆数量
        Return:
            str: 检索到的记忆
        """
        # from memory_system.memory_manager import MemoryManager
        # # graphiti = await init_graphiti()
        # memory_manager = MemoryManager()
        # # await memory_manager.initialize()  # 确保初始化完成
        group_id = name.encode('utf-8').hex()
        search_config = search_config_recipes.COMBINED_HYBRID_SEARCH_RRF
        search_config.limit = limit+1
        memories = await self.graphiti._search(query, 
            config=search_config, 
            group_ids=[group_id])

        # print(f"memories: {memories}")

        mem_fact = ""
        summary = ""
        fact = ""
        # 1.获取实体
        for mode in memories.nodes:
            if mode.name != "I":
                summary += f"- {mode.name}: {mode.summary}\n"
        if summary:
            mem_fact  += "# 事物\n" + summary + "\n"
        # 2.获取事实
        for edge in memories.edges[:limit]:
            fact += f"- {edge.fact}\n"
            if hasattr(edge, 'valid_at') and edge.valid_at:
                valid_at_time_str = edge.valid_at.strftime('%Y-%m-%d %H:%M:%S')
                fact += f"事实生效时间: {valid_at_time_str}\n"
            if hasattr(edge, 'invalid_at') and edge.invalid_at:
                invalid_at_time_str = edge.invalid_at.strftime('%Y-%m-%d %H:%M:%S')
                fact += f"事实失效时间: {invalid_at_time_str}\n"
        if fact:
            mem_fact  += "# 事实\n" + fact + "\n"
        # mem_fact = ""

        # group_id = name.encode('utf-8').hex()
        # memories = await self.graphiti.search(
        #     query, 
        #     # COMBINED 检索能一次性获取事实（edges）、实体（nodes）和主题（communities）。
        #     # RRF速度快
        #     # config=search_config_recipes.COMBINED_HYBRID_SEARCH_RRF, 
        #     num_results = limit,
        #     group_ids=[group_id]
        # )

        # for memory in memories:
        #     mem_fact += f"- {memory.fact}\n"
        #     if hasattr(memory, 'valid_at') and memory.valid_at:
        #         mem_fact += f'事实产生时间: {memory.valid_at}\n'
        #     if hasattr(memory, 'invalid_at') and memory.invalid_at:
        #         mem_fact += f'事实失效时间: {memory.invalid_at}\n'
        # print(f'{mem_fact}')
        return mem_fact

    async def search_episode_memory(self, 
    name: str, 
    query: str,
    start_time: str = "",
    end_time: str = "",
    limit: int = 1):
        """
        根据用户问题，检索情景记忆
        Args:
            name(str): agent名称
            query(str): 用户问题
            limit(int): 检索的记忆数量
        Return:
            str: 检索到的记忆
        """
        if not (query or start_time or end_time):
            print("(query, start_time, end_time)均为空！请至少提供一条线索以检索记忆")
            return "(query, start_time, end_time)均为空！请至少提供一条线索以检索记忆"
        
        time_key = "valid_at" # 表里的时间key
        group_id = name.encode('utf-8').hex()

        # 设置事件筛选条件:
        condition = "" # cypher语句中的筛选条件
        mem_desc = "" # 待存储到记忆的描述
        # 1. 根据episode_desc筛选uuid
        if query:
            memories = await self.graphiti._search(query, 
            config=search_config_recipes.EDGE_HYBRID_SEARCH_RRF, 
            group_ids=[group_id])
            # 向量匹配，寻找episodes的uuid
            episodes_uuid_list = []
            for edge in memories.edges:
                if hasattr(edge, 'episodes') and edge.episodes:
                    episodes_uuid_list += edge.episodes
            condition += f"n.uuid in {episodes_uuid_list}" if episodes_uuid_list else ""
            mem_desc += f"有关\"{query}\""
        # 2. 根据start_time筛选
        if start_time:
            condition += f" AND " if condition else ""
            condition += f"n.{time_key} >= TIMESTAMP('{start_time}')"
            mem_desc += f"，" if mem_desc else ""
            mem_desc += f"从{start_time}之后"
        # 3. 根据end_time筛选
        if end_time:
            condition += f" AND " if condition else ""
            condition += f"n.{time_key} <= TIMESTAMP('{end_time}')"
            mem_desc += f"，" if mem_desc else ""
            mem_desc += f"到{end_time}之前"

        condition = f"WHERE {condition}" if condition else ""
        mem_desc = f"{name}回想了" + mem_desc + "的情景" if mem_desc else ""

        query = f"""
            MATCH (n: Episodic) {condition}
            RETURN n
            ORDER BY n.{time_key} ASC
            LIMIT {max(1, min(limit,20))};
            """ 

        # print(query)# 测试
        
        # 检索episodes的实际内容
        mem_episode = ""
        try:
            response = await self.conn.execute(query)
            for row in response.rows_as_dict():
                memory = row['n']
                mem_episode += f"情景: \"{memory['content']}\"\n"
                # if 'valid_at' in memory:
                #     valid_at_time_str = memory[time_key].strftime('%Y-%m-%d %H:%M:%S')
                #     mem_longtime += f"发生时间: {valid_at_time_str}\n"
                mem_episode += "---\n"
        except RuntimeError as e: # 一般为刚刚建立库，检索失败
            print(f"情景记忆检索失败: {e}")
        if mem_episode:
            mem_episode = "# 情景\n" + mem_episode


        # time_key = "valid_at" # 表里的时间key

        # mem_episode = ""

        # group_id = name.encode('utf-8').hex()
        # episodes_uuid_list = []
        # memories = await self.graphiti._search(
        #     query, 
        #     config=search_config_recipes.EDGE_HYBRID_SEARCH_RRF, 
        #     group_ids=[group_id]
        # )
        # for memory in memories.edges:
        #     if hasattr(memory, 'episodes') and memory.episodes:
        #         episodes_uuid_list += memory.episodes

        # # 构造参数字典
        # params = {}
        # condition = ""
        # if episodes_uuid_list:
        #     condition = "WHERE n.uuid IN $uuids"
        #     params["uuids"] = episodes_uuid_list
        # params["limit"] = int(limit) 
        # cypher = f"""
        #     MATCH (n:Episodic) {condition}
        #     RETURN n
        #     ORDER BY n.{time_key} ASC
        #     LIMIT $limit
        # """
        
        # # condition = f"WHERE n.uuid in {episodes_uuid_list}" if episodes_uuid_list else ""
        # # cypher = f"""
        # #     MATCH (n: Episodic) {condition}
        # #     RETURN n
        # #     ORDER BY n.{time_key} ASC
        # #     LIMIT {limit};
        # #     """ 
        # # print(cypher)
        # # 检索episodes的实际内容
        # try:
        #     # response = await memory_manager.conn.execute(cypher)
        #     response = await self.conn.execute(cypher, parameters=params)
        #     for row in response.rows_as_dict():
        #         memory = row['n']
        #         mem_episode += f"情景: \"{memory['content']}\"\n"
        #         # if 'valid_at' in memory:
        #         #     valid_at_time_str = memory[time_key].strftime('%Y-%m-%d %H:%M:%S')
        #         #     mem_episode += f"发生时间: {valid_at_time_str}\n"
        #         mem_episode += "---\n"
        # except RuntimeError as e: # 一般为刚刚建立库，检索失败
        #     print(f"情景记忆检索失败: {e}")
        # print(f'{mem_episode}')

        return mem_episode

    async def backup_memory(self, db_backup_name: str):
        """
        将db文件备份
        """
        shutil.copy(os.path.join(db_path, db_name), os.path.join(db_backup_path, db_backup_name))

    async def restore_memory(self, db_backup_name: str):
        """
        将备份的文件恢复到db
        """
        shutil.copy(os.path.join(db_backup_path, db_backup_name), os.path.join(db_path, db_name))

    async def delete_memory(self, name: str, start_time: datetime, end_time: datetime):
        """
        删除记忆
        Args:
            name(str): agent名称
            start_time(datetime): 开始时间
            end_time(datetime): 结束时间
        """
        group_id = name.encode('utf-8').hex()
        condition = f"WHERE n.group_id = '{group_id}'"
        if start_time:
            condition += f" AND n.valid_at >= TIMESTAMP('{start_time}')"
        if end_time:
            condition += f" AND n.valid_at <= TIMESTAMP('{end_time}')"
        
        query = f"""
            MATCH (n) 
            {condition}
            DETACH DELETE n
        """
        await self.conn.execute(query)

    async def delete_all_memory(self):
        """
        高性能清空：关闭连接 -> 物理删除数据库文件夹 -> 重新初始化
        """
        print("⚠️ [MemorySystem] 正在执行全库物理删除...")
        
        # 1. 必须先关闭资源，释放 Kuzu 的文件锁
        # 复用你写好的 close 方法，它包含了 gc.collect()，这非常关键
        await self.close()

        # 2. 物理删除数据库文件夹
        full_db_path = os.path.join(db_path, db_name)
        if os.path.exists(full_db_path):
            try:
                shutil.rmtree(full_db_path)
                print(f"🗑️ [MemorySystem] 数据库文件夹已物理移除: {full_db_path}")
            except Exception as e:
                print(f"❌ 删除数据库文件失败 (文件可能仍被占用): {e}")
                raise e
        else:
            print("ℹ️ 数据库路径不存在，跳过删除。")

        # 3. 重新初始化
        # 你的 initialize 方法里包含了 setup_schema()，会自动重建表结构和索引
        print("🔄 [MemorySystem] 正在重建数据库环境...")
        await self.initialize()
        print("✅ [MemorySystem] 记忆库已重置完成")

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
            if self._kuzu_driver:
                self._kuzu_driver.db = None # 释放 Database 对象引用
                self._kuzu_driver = None
            
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