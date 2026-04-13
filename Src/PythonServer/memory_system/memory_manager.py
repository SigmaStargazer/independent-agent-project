import os
import asyncio
from datetime import datetime
from uuid import uuid4
import gc # 引入垃圾回收
import copy
import shutil

from graphiti_core import Graphiti
from graphiti_core.driver.kuzu_driver import KuzuDriver # kuzu配置

from graphiti_core.nodes import EntityNode, EpisodeType
from graphiti_core.search import search_config_recipes

from graphiti_core.llm_client.openai_generic_client import OpenAIGenericClient
from graphiti_core.llm_client.config import LLMConfig
from graphiti_core.embedder.openai import OpenAIEmbedder, OpenAIEmbedderConfig
from graphiti_core.cross_encoder.openai_reranker_client import OpenAIRerankerClient

from memory_system.safe_batch_embedder import SafeBatchOpenAIEmbedder
from memory_system.safe_batch_reranker import SafeBatchOpenAIReranker

from agent_framwork.base.singleton import singleton
import kuzu

model_api_base = "https://dashscope.aliyuncs.com/compatible-mode/v1"
model_api_key = "sk-7a3958e0fdf840e49a2edd83b25dd228"
model_name = "qwen-max"
embedding_model_name = "text-embedding-v4"
reranker_model_name = "gte-rerank-v2"

# 数据库名
db_root = "db"
db_name = "graphiti"

QUEUE_SIZE = int(os.getenv("MEMORY_QUEUE_SIZE", 1000))

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
        self._reranker = None

        # 异步队列与 Worker 控制 
        self._memory_queue = asyncio.Queue(maxsize=QUEUE_SIZE)
        self._worker_task = None

        # 记忆备份相关
        self._backup_root = "backups"
        self._max_backup_slots = int(os.getenv("MAX_BACKUP_SLOTS", 10))
        self._backup_lock = asyncio.Lock()

    async def initialize(self):
        if self._initialized: return self
        if self._init_lock is None: self._init_lock = asyncio.Lock()

        async with self._init_lock:
            if self._initialized: return self

            # # 0. 清理 WAL 文件（必须在 Database() 之前）
            # wal_path = os.path.join(db_root, f"{db_name}.wal")
            # try:
            #     if os.path.exists(wal_path):
            #         os.remove(wal_path)
            #         print(f"🧹 [MemorySystem] 已删除 WAL 文件: {wal_path}")
            # except Exception as e:
            #     print(f"⚠️ [MemorySystem] 删除 WAL 失败: {e}")
            #     raise RuntimeError(f"WAL 删除失败，数据库可能处于脏状态: {wal_path}")
            
            # 1. 初始化所有依赖组件
            self._embedder = SafeBatchOpenAIEmbedder(
                config=OpenAIEmbedderConfig(
                    api_key=model_api_key, 
                    embedding_model=embedding_model_name,
                    base_url=model_api_base
                ),
                max_batch_size=10
            )

            self._reranker = SafeBatchOpenAIReranker(
                config=LLMConfig(
                    api_key=model_api_key, 
                    model=reranker_model_name,
                    base_url=model_api_base
                ),
                max_batch_size=10
            )

            # 2. 安全打开数据库
            print("💾 [MemorySystem] 正在挂载全局数据库...")
            db_path = os.path.join(db_root, f"{db_name}.kuzu")
            wal_path = os.path.join(db_root, f"{db_name}.wal")
            opened = False
            try:
                self._kuzu_db = kuzu.Database(db_path)
                opened = True
            except Exception as e:
                print("⚠️ [MemorySystem] 数据库打开失败，尝试清理 WAL:", e)
                if os.path.exists(wal_path):
                    try:
                        os.remove(wal_path)
                        print(f"🧹 [MemorySystem] 已删除 WAL 文件: {wal_path}")
                    except Exception as e:
                        print(f"⚠️ [MemorySystem] 删除 WAL 失败: {e}")
                        raise RuntimeError(f"WAL 删除失败，数据库可能处于脏状态: {wal_path}")
            # 再尝试一次打开
            if not opened:
                try:
                    self._kuzu_db = kuzu.Database(db_path)
                    print("✅ [MemorySystem] 数据库已通过 clean start 打开")
                except Exception as retry_err:
                    print("❌ [MemorySystem] clean start 仍失败:", retry_err)
                    raise retry_err
            
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

            if self._worker_task is None or self._worker_task.done():
                self._worker_task = asyncio.create_task(self._memory_worker(),name="memory_worker") 
                print("✅ [MemorySystem] 记忆存储 Worker 已启动") 
        return self

    async def _memory_worker(self):
        """
        记忆存储 Worker
        """
        print("[MemWorker] started")
        while True:
            try:
                name, memory, curtime = await self._memory_queue.get()
                try:
                    await self._save_memory(
                        name=name,
                        memory=memory,
                        curtime=curtime,
                        wait_result=False
                    )
                except Exception as e:
                    print("[MemWorker] save failed:",e)
                finally:
                    self._memory_queue.task_done()
            except asyncio.CancelledError:
                print("[MemWorker] cancelled")
                break
            except Exception as e:
                print("[MemWorker] unexpected error:",e)
                # 防止 crash loop
                await asyncio.sleep(1)
                continue

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
        try:
            result = await self._save_memory(name=name, memory=summary, curtime=create_time, wait_result=True)
        except Exception as e:
            print(f"初始化agent简介失败: {e}")
            return
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
            # 限制记忆长度，避免报错
            MAX_CHARS_PER_EPISODE = 8000
            if len(memory) > MAX_CHARS_PER_EPISODE:
                print(
                    f"[MemoryManager][{name}] memory too large "
                    f"({len(memory)} chars), truncating to {MAX_CHARS_PER_EPISODE}"
                )
                memory = memory[:MAX_CHARS_PER_EPISODE]
            if wait_result:
                result = await self.graphiti.add_episode(
                    name=f"{name}_mem_{curtime}",
                    episode_body=memory,
                    source=EpisodeType.text,
                    source_description=f"{name}_mem_{curtime}", 
                    reference_time=curtime,
                    group_id=group_id
                )
                print(f"[MemoryManager][{name}]存储记忆成功")
                return result
            else:
                await self.graphiti.add_episode(
                    name=f"{name}_mem_{curtime}",
                    episode_body=memory,
                    source=EpisodeType.text,
                    source_description=f"{name}_mem_{curtime}", 
                    reference_time=curtime,
                    group_id=group_id
                )
                print(f"[MemoryManager][{name}]异步存储记忆任务启动")
        except Exception as e:
            print(f"[MemoryManager][{name}]存储记忆失败: {e}")
            if wait_result:
                raise

    async def save_memory(self, name: str, memory: str, curtime: datetime):
        """
        存储记忆
        Args:
            name(str): agent名称
            memory(str): 记忆
            curtime(datetime): 记忆产生时间
        """
        if not self._initialized:
            print("[MemoryManager] not initialized")
            return
        try:
            await asyncio.wait_for(
                self._memory_queue.put((name,memory,curtime)),
                timeout=2
            )
        except asyncio.TimeoutError:
            print("[MemoryManager] queue full timeout:",name)
        except Exception as e:
            print("[MemoryManager] enqueue failed:",e)

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
        search_config = copy.deepcopy(search_config_recipes.COMBINED_HYBRID_SEARCH_RRF)
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
            # 如果 query 存在，但没有匹配到 episode
            # 直接返回空，避免返回所有 memory
            if not episodes_uuid_list:
                return ""
            condition += f"n.uuid in {episodes_uuid_list}"
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

        return mem_episode

    async def wait_memory_flush(
        self,
        timeout: float | None = None
    ) -> bool:
        """
        等待所有 memory 写入完成

        Returns:
            True  -> 所有写入完成
            False -> 超时
        """

        if not self._initialized: return True
        try:
            if timeout is not None:
                await asyncio.wait_for(self._memory_queue.join(),timeout=timeout)
            else:
                await self._memory_queue.join()
            return True
        except asyncio.TimeoutError:
            print("[MemoryManager] wait_memory_flush timeout")
            return False

    async def backup_memory(self, slot_id: int):
        """
        记忆存档（热备份）
            - 支持运行时备份
            - 覆盖已有 slot
            - 自动复制 wal

        建议使用方式：
        flushed = await wait_memory_flush(timeout=2.0)
        if not flushed:
            print(
                "[MemoryManager] flush timeout, "
                "proceeding with backup anyway"
            )
        await backup_memory(slot_id)

        Args:
            slot_id(int): 存档序号。取值范围为[0, MAX_BACKUP_SLOTS-1]
        """
        async with self._backup_lock:
            if not self._initialized:
                raise RuntimeError("未执行initialized")
            if slot_id < 0 or slot_id >= self._max_backup_slots:
                raise ValueError(f"slot_id应在[0, {self._max_backup_slots-1}]范围内")
            print(f"[MemoryManager][记忆备份开始] slot={slot_id}")

            os.makedirs(self._backup_root, exist_ok=True)
            slot_path = os.path.join(self._backup_root, f"slot_{slot_id}")
            # 如果备份目录存在，则需要覆盖
            if os.path.exists(slot_path):
                print(f"[MemoryManager][记忆备份中][覆盖已有备份] slot={slot_id}")
                shutil.rmtree(slot_path)
            os.makedirs(slot_path)
            # 复制数据库文件和WAL文件
            db_file = os.path.join(db_root,f"{db_name}.kuzu")
            wal_file = os.path.join(db_root,f"{db_name}.wal")
            try:
                shutil.copy2(db_file,os.path.join(slot_path,f"{db_name}.kuzu"))
                if os.path.exists(wal_file):
                    shutil.copy2(wal_file,os.path.join(slot_path,f"{db_name}.wal"))
                print(f"[MemoryManager][记忆备份完成] slot={slot_id}")
            except Exception as e:
                print(f"[MemoryManager][记忆备份失败] slot={slot_id}: {e}")
                raise

    async def restore_memory(self, slot_id: int):
        """
        记忆读档
            - 必须在 Agent 停止后调用

        建议使用方式：
        AgentManager().finish()
        await MemoryManager().restore_memory(slot_id=slot_id)
        await MemoryManager().initialize()
        await AgentManager().aload_agent()
        AgentManager().start()

        Args:
            slot_id(int): 存档序号
        """
        async with self._backup_lock:
            print(f"[MemoryManager] 开始读档 slot={slot_id}")
            slot_path = os.path.join(self._backup_root,f"slot_{slot_id}")
            # 如果备份目录不存在，则报错
            if not os.path.exists(slot_path):
                raise RuntimeError(f"slot {slot_id} 不存在")

            await self.close()

            db_file = os.path.join(db_root,f"{db_name}.kuzu")
            wal_file = os.path.join(db_root,f"{db_name}.wal")

            try:
                # 删除当前的数据库文件和WAL文件
                if os.path.exists(db_file):
                    os.remove(db_file)
                if os.path.exists(wal_file):
                    os.remove(wal_file)
                # 复制备份的数据库文件和WAL文件
                shutil.copy2(os.path.join(slot_path,f"{db_name}.kuzu"),db_file)
                backup_wal = os.path.join(slot_path,f"{db_name}.wal")
                if os.path.exists(backup_wal):
                    shutil.copy2(backup_wal,wal_file)
            except Exception as e:
                print(f"[MemoryManager][记忆读档失败] slot={slot_id}: {e}")
                raise
            print(f"[MemoryManager] 读档完成 slot={slot_id}")

    async def list_used_slots(self) -> list[int]:
        """
        获取当前已占用的 slot_id 列表
        Return:
            List[int]
        """
        os.makedirs(self._backup_root, exist_ok=True)
        used_slots = []
        for name in os.listdir(self._backup_root):
            if not name.startswith("slot_"):
                continue
            try:
                slot_id = int(name.split("_")[1])
            except Exception:
                continue

            if 0 <= slot_id < self._max_backup_slots:
                slot_path = os.path.join(self._backup_root, name)
                # 必须包含数据库文件才算有效
                db_file = os.path.join(slot_path,f"{db_name}.kuzu")
                if os.path.exists(db_file):
                    used_slots.append(slot_id)
        used_slots.sort()
        return used_slots

    async def delete_backup_slot(self, slot_id: int):
        """
        删除指定slot的备份

        Args:
            slot_id(int)
        """
        async with self._backup_lock:
            if slot_id < 0 or slot_id >= self._max_backup_slots:
                raise ValueError(f"slot_id应在[0, {self._max_backup_slots-1}]范围内")
            slot_path = os.path.join(self._backup_root,f"slot_{slot_id}")

            if not os.path.exists(slot_path):
                print(f"[MemoryManager] slot {slot_id} 不存在")
                return

            print(f"[MemoryManager] 删除备份 slot={slot_id}")

            try:
                shutil.rmtree(slot_path)
                print(f"[MemoryManager] 删除成功 slot={slot_id}")
            except Exception as e:
                print(f"[MemoryManager] 删除失败 slot={slot_id}: {e}")
                raise

    async def delete_current_memory(self):
        """
        删除当前正在使用的记忆（清空数据库）

        安全流程：
            1. 停止 worker
            2. 关闭连接
            3. 删除 db + wal
            4. 重置状态
        """
        async with self._backup_lock:
            print("[MemoryManager] 删除当前记忆开始")
            await self.close()
            db_file = os.path.join(db_root,f"{db_name}.kuzu")
            wal_file = os.path.join(db_root,f"{db_name}.wal")

            try:
                if os.path.exists(db_file):
                    os.remove(db_file)
                    print("[MemoryManager] 已删除数据库文件")
                if os.path.exists(wal_file):
                    os.remove(wal_file)
                    print("[MemoryManager] 已删除 WAL 文件")

            except Exception as e:
                print("[MemoryManager] 删除当前记忆失败:",e)
                raise

            print("[MemoryManager] 当前记忆已清空")

    async def close(self):
        """显式关闭资源，释放 Kuzu 文件锁"""

        if not self._initialized:
            return
        print("🛑 [1/4] 正在清除记忆任务队列worker")
        if self._worker_task:
            try:
                await asyncio.wait_for(self._memory_queue.join(),timeout=30.0)
            except asyncio.TimeoutError:
                print("[MemorySystem] queue drain timeout")

            self._worker_task.cancel()
            try:
                await self._worker_task
            except asyncio.CancelledError:
                pass
            self._worker_task = None

        print("🛑 [2/4] 正在关闭连接池...")
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
            self._kuzu_db = None 

            # 3. 【核心绝招】强制垃圾回收
            # Python 的 GC 是懒惰的，必须手动踢一脚
            # 只有 Database 对象被 GC 销毁，文件锁才会释放
            gc.collect()
            
            self._initialized = False
            print("✅ [3/4] Python 对象引用已清理")
            print("✅ [4/4] 垃圾回收完成，数据库锁应已释放")
            
        except Exception as e:
            print(f"⚠️ 关闭资源时出错: {e}")