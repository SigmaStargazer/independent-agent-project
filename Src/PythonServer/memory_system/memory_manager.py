import os
import asyncio
from datetime import datetime
from uuid import uuid4
import copy
import shutil
from contextlib import asynccontextmanager

from graphiti_core import Graphiti
from graphiti_core.driver.kuzu_driver import KuzuDriver

from graphiti_core.nodes import EntityNode, EpisodeType
from graphiti_core.search import search_config_recipes

from graphiti_core.llm_client.openai_generic_client import OpenAIGenericClient
from graphiti_core.llm_client.config import LLMConfig
from langchain_openai import ChatOpenAI
from langchain_core.messages import HumanMessage, SystemMessage

from memory_system.action_skill_system import ActionSkillManager
from memory_system.db_conn import DBConnectionService
from memory_system.embedder import EmbedderService

from runtime.path_config import get_data_dir

from agent_framwork.base.singleton import singleton
from agent_framwork.utils.prompt_utils import estimate_tokens

from dotenv import load_dotenv
load_dotenv()

# 记忆压缩相关配置（不依赖 Key，模块级读取即可）
QUEUE_SIZE = int(os.getenv("MEMORY_QUEUE_SIZE", 1000))
MEMORY_COMPRESS_ENABLED = os.getenv("MEMORY_COMPRESS_ENABLED", "true").lower() == "true"
MEMORY_COMPRESS_TRIGGER_TOKENS = int(os.getenv("MEMORY_COMPRESS_TRIGGER_TOKENS", "12000"))
MEMORY_COMPRESS_TARGET_TOKENS = int(os.getenv("MEMORY_COMPRESS_TARGET_TOKENS", "3000"))
MEMORY_COMPRESS_INPUT_TOKENS = int(os.getenv("MEMORY_COMPRESS_INPUT_TOKENS", "20000"))
MEMORY_COMPRESS_FALLBACK_CHARS = int(os.getenv("MEMORY_COMPRESS_FALLBACK_CHARS", "6000"))
MEMORY_COMPRESS_TIMEOUT = float(os.getenv("MEMORY_COMPRESS_TIMEOUT", "60"))
MEMORY_COMPRESS_MAX_RETRIES = int(os.getenv("MEMORY_COMPRESS_MAX_RETRIES", "1"))

class _SharedKuzuDriver(KuzuDriver):
    def __init__(self, db_instance, conn_instance):
        self.db = db_instance
        self.client = conn_instance

@singleton
class MemoryManager:
    def __init__(self):
        # LLM 相关对象不在 __init__ 构造：模块可能被 agent_interuptible 在
        # api_config.json 注入 os.environ 之前 import，此时 Key 为 None。
        # 延迟到 initialize()（main 完成 load_api_config_into_env 之后）再构造。
        self._llm_config = None
        self._compress_model = None
        self._initialized = False
        self._init_lock = None
        self._kuzu_driver = None
        self.graphiti = None

        self._memory_queue = asyncio.Queue(maxsize=QUEUE_SIZE)
        self._worker_task = None

        self._backup_root = os.path.join(get_data_dir(), "backups")
        self._max_backup_slots = int(os.getenv("MAX_BACKUP_SLOTS", 10))
        self._backup_lock = asyncio.Lock()

        self._graph_write_lock = asyncio.Lock()
        self._action_skill = None

    def _build_llm_config(self) -> LLMConfig:
        """在 initialize() 内实时读取 env 构造记忆 LLM 配置（此时 Key 已注入）。"""
        mem_model_api_base = os.getenv("MEMORY_API_BASE")
        mem_model_api_key = os.getenv("MEMORY_API_KEY")
        mem_model_name = os.getenv("MEMORY_MODEL")
        return LLMConfig(
            api_key=mem_model_api_key,
            model=mem_model_name,
            small_model=mem_model_name,
            base_url=mem_model_api_base,
        )

    def _build_compress_model(self) -> ChatOpenAI:
        """在 initialize() 内实时读取 env 构造记忆压缩模型。"""
        mem_model_api_base = os.getenv("MEMORY_API_BASE")
        mem_model_api_key = os.getenv("MEMORY_API_KEY")
        mem_model_name = os.getenv("MEMORY_MODEL")
        return ChatOpenAI(
            model=mem_model_name,
            api_key=mem_model_api_key,
            base_url=mem_model_api_base,
            request_timeout=MEMORY_COMPRESS_TIMEOUT,
            max_retries=MEMORY_COMPRESS_MAX_RETRIES,
            max_tokens=MEMORY_COMPRESS_TARGET_TOKENS,
            temperature=0,
        )

    @property
    def action_skill(self):
        if self._action_skill is None:
            self._action_skill = ActionSkillManager()
        return self._action_skill

    @property
    def is_initialized(self) -> bool:
        """记忆系统是否已初始化（v0.23.0：InitRequest / --auto-init 后为 True）。"""
        return self._initialized

    def _reset_subsystems_for_reinitialize(self):
        self.action_skill.reset_for_reinitialize()

    @asynccontextmanager
    async def memory_access(self):
        """
        Memory API 访问门：薄转发到 DBConnectionService.access()
        """
        async with DBConnectionService().access():
            yield

    async def initialize(self):
        if self._initialized: return self
        if self._init_lock is None: self._init_lock = asyncio.Lock()

        async with self._init_lock:
            if self._initialized: return self

            # 1. 确保底层 service 已初始化（幂等）
            await DBConnectionService().initialize()
            await EmbedderService().initialize()
            dbsvc = DBConnectionService()
            embedder = EmbedderService().get_embedder()
            reranker = EmbedderService().get_reranker()

            # 1.5 LLM 对象延迟构造：此时 main() 已完成 api_config.json -> env 注入
            self._llm_config = self._build_llm_config()
            self._compress_model = self._build_compress_model()

            # 2. 组装 Graphiti（基于 service 提供的 db/conn）
            self._kuzu_driver = _SharedKuzuDriver(dbsvc.get_db(), dbsvc.get_conn())
            print("🏗️ [MemorySystem] 正在检查/创建表结构 (Schema)...")
            try:
                self._kuzu_driver.setup_schema()
            except Exception as e:
                print(f"⚠️ Schema setup warning (可忽略): {e}")
            self.graphiti = Graphiti(
                graph_driver=self._kuzu_driver,
                llm_client=OpenAIGenericClient(config=self._llm_config),
                embedder=embedder,
                cross_encoder=reranker
            )
            # 3. 确保 FTS 索引完整
            await self._ensure_fts_indexes(is_new_db=dbsvc.is_new_db)

            # 4. 启动记忆存储 Worker
            if self._worker_task is None or self._worker_task.done():
                self._worker_task = asyncio.create_task(self._memory_worker(),name="memory_worker")
                print("✅ [MemorySystem] 记忆存储 Worker 已启动")

            # 5. 初始化程序性记忆子系统
            await self.action_skill.initialize()

            self._initialized = True
        return self

    async def _ensure_fts_indexes(self, is_new_db: bool):
        if is_new_db:
            print("⏳ [MemorySystem] 首次初始化索引...")
        else:
            print("ℹ️ [MemorySystem] 检查/补全 FTS 索引...")
        for attempt in range(3):
            try:
                await self.graphiti.build_indices_and_constraints()
                return
            except Exception as e:
                if "write transaction" in str(e).lower() and attempt < 2:
                    print(f"⚠️ 索引创建冲突，第{attempt + 1}次重试...")
                    await asyncio.sleep(1.0 * (attempt + 1))
                else:
                    raise

    async def _memory_worker(self):
        """
        记忆存储 Worker
        """
        print("[MemWorker] started")
        dbsvc = DBConnectionService()
        while True:
            try:
                name, memory, curtime = await self._memory_queue.get()
                # 提前占 active_ops（worker 已经被串行化，无需 wait freeze）
                await dbsvc.begin_op_internal()
                try:
                    await asyncio.wait_for(
                        self._save_memory(
                            name=name,
                            memory=memory,
                            curtime=curtime,
                            wait_result=False,
                            already_locked=True
                        ),
                        timeout=120
                    )
                except asyncio.TimeoutError:
                    print("[MemWorker] save timeout:", name)
                except Exception as e:
                    print("[MemWorker] save failed:",e)
                finally:
                    await dbsvc.end_op()
                    self._memory_queue.task_done()
            except asyncio.CancelledError:
                print("[MemWorker] cancelled")
                break
            except Exception as e:
                print("[MemWorker] unexpected error:",e)
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
        async with self.memory_access():
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
            await my_entity.generate_name_embedding(EmbedderService().get_embedder())
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
        async with self.memory_access():
            summary = ""

            group_id = name.encode('utf-8').hex()
            cypher = f"""
            MATCH (n:Entity{{name: "I", group_id: "{group_id}"}})
            RETURN n
            """
            try:
                response = await DBConnectionService().get_conn().execute(cypher)
                for row in response.rows_as_dict():
                    summary = row['n']['summary']
                    break
            except Exception as e:
                print(f"加载智能体{name}简介失败: {e}")

            return summary

    async def _save_memory(
        self, 
        name: str, 
        memory: str, 
        curtime: datetime, 
        wait_result: bool = False,
        already_locked: bool = False
        ):
        if not already_locked:
            async with self.memory_access():
                return await self._save_memory_impl(
                    name,
                    memory,
                    curtime,
                    wait_result
                )
        else:
            return await self._save_memory_impl(
                name,
                memory,
                curtime,
                wait_result
            )

    @staticmethod
    def _truncate_to_token_budget(text: str, max_tokens: int) -> str:
        if not text or estimate_tokens(text) <= max_tokens:
            return text
        max_chars = max(1000, max_tokens * 2)
        head_chars = max_chars // 2
        tail_chars = max_chars - head_chars
        return (
            text[:head_chars]
            + "\n...[中间有一段重复或过长的原始经历被省略，用于避免压缩输入超长]...\n"
            + text[-tail_chars:]
        )

    @staticmethod
    def _fallback_diary_memory(memory: str) -> str:
        if len(memory) <= MEMORY_COMPRESS_FALLBACK_CHARS:
            return memory
        half = max(500, MEMORY_COMPRESS_FALLBACK_CHARS // 2)
        return (
            "# 这段时间的经历（本地兜底压缩）\n"
            "压缩模型暂时不可用，因此我只保留了这段经历的开头和结尾。\n\n"
            "## 开始时发生的事\n"
            f"{memory[:half]}\n\n"
            "## 后来发生的事\n"
            f"{memory[-half:]}"
        )

    async def compress_memory_text(
        self,
        name: str,
        memory: str,
        curtime: datetime | str,
        unfinished: bool = False,
    ) -> str:
        tokens = estimate_tokens(memory)
        if not MEMORY_COMPRESS_ENABLED or tokens <= MEMORY_COMPRESS_TRIGGER_TOKENS:
            print(
                f"[MemoryManager][{name}] memory length tokens={tokens}, "
                f"chars={len(memory)}, compress=False"
            )
            return memory
        if self._compress_model is None:
            # 未初始化（缺 Key / 未收到 InitRequest）时走本地兜底，不静默失败也不崩溃
            print(f"[MemoryManager][{name}] 压缩模型未初始化，使用本地兜底压缩")
            return self._fallback_diary_memory(memory)

        print(
            f"[MemoryManager][{name}] memory length tokens={tokens}, "
            f"chars={len(memory)}, compress=True"
        )
        safe_memory = self._truncate_to_token_budget(memory, MEMORY_COMPRESS_INPUT_TOKENS)
        status_line = "这段经历尚未结束。" if unfinished else "这段经历已经进入阶段性记录。"
        system_prompt = (
            "你是游戏世界中角色的长期记忆整理器。"
            "你的任务不是写任务报告，也不是只总结经验结论，而是把冗长原始记录压缩成角色自己的情景日记式记忆。"
            "必须保留时间线、场景细节、观察、心理活动、行动、工具调用、外界反馈、成功失败与未完成事项。"
            "可以删除重复环境快照、重复工具日志和没有信息增量的冗余文本。"
            "使用第一人称，中文输出。"
        )
        user_prompt = f"""请把下面原始经历压缩成情景日记式记忆。

要求：
- {status_line}
- 不要只输出抽象经验条目。
- 尽量按发生顺序写清楚我看到了什么、想了什么、做了什么、外界如何反馈。
- 如果有尚未完成的意图，必须保留。
- 最后可以保留少量从经历中自然得到的经验。

建议结构：
# 这段时间的经历
## 时间与场景
## 经历时间线
## 重要细节
## 当前未完成的事
## 从这段经历自然得到的经验

角色名：{name}
记录时间：{curtime}

原始经历：
{safe_memory}
"""
        try:
            response = await self._compress_model.ainvoke([
                SystemMessage(content=system_prompt),
                HumanMessage(content=user_prompt),
            ])
            compressed = response.content.strip() if isinstance(response.content, str) else str(response.content)
            if not compressed:
                raise ValueError("压缩结果为空")
            if estimate_tokens(compressed) > MEMORY_COMPRESS_TARGET_TOKENS * 1.5:
                compressed = self._truncate_to_token_budget(
                    compressed,
                    int(MEMORY_COMPRESS_TARGET_TOKENS * 1.5),
                )
            print(
                f"[MemoryManager][{name}] compressed memory tokens={estimate_tokens(compressed)}, "
                f"chars={len(compressed)}"
            )
            return compressed
        except Exception as e:
            print(f"[MemoryManager][{name}] memory compress failed: {e}")
            return self._fallback_diary_memory(memory)

    async def _save_memory_impl(
        self, 
        name: str, 
        memory: str, 
        curtime: datetime, 
        wait_result: bool = False
        ):
        group_id = name.encode('utf-8').hex()
        try:
            memory = await self.compress_memory_text(
                name=name,
                memory=memory,
                curtime=curtime,
                unfinished=False,
            )
            if len(memory) > MEMORY_COMPRESS_FALLBACK_CHARS * 2:
                print(
                    f"[MemoryManager][{name}] compressed memory still large "
                    f"({len(memory)} chars), fallback truncating"
                )
                memory = self._fallback_diary_memory(memory)

            async with self._graph_write_lock:
                for i in range(3):
                    episode_id = uuid4()
                    if wait_result:
                        result = None
                        try:
                            result = await self.graphiti.add_episode(
                                name=f"{name}_mem_{curtime}_{episode_id}",
                                episode_body=memory,
                                source=EpisodeType.text,
                                source_description=f"{name}_mem_{curtime}",
                                reference_time=curtime,
                                group_id=group_id
                            )
                        except Exception as e:
                            if "duplicated primary key value" in str(e):
                                print(f"[MemoryManager] duplicate edge retry {i+1}")
                                await asyncio.sleep(0.2 * (i + 1))
                                continue
                            raise
                        print(f"[MemoryManager][{name}]存储记忆成功")
                        return result
                    else:
                        try:
                            await self.graphiti.add_episode(
                                name=f"{name}_mem_{curtime}_{episode_id}",
                                episode_body=memory,
                                source=EpisodeType.text,
                                source_description=f"{name}_mem_{curtime}",
                                reference_time=curtime,
                                group_id=group_id
                            )
                        except Exception as e:
                            if "duplicated primary key value" in str(e):
                                print(f"[MemoryManager] duplicate edge retry {i+1}")
                                await asyncio.sleep(0.2 * (i + 1))
                                continue
                            raise
                        print(f"[MemoryManager][{name}]异步存储记忆任务启动")
                        break
        except Exception as e:
            print(f"[MemoryManager][{name}]存储记忆失败: {e}")
            if wait_result:
                raise

    async def _wait_if_frozen(self):
        """已不用，保留薄实现以避免外部还有调用"""
        await DBConnectionService().wait_idle() if False else None

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
        # backup期间禁止入队（dbsvc.access 会阻塞 freeze 状态）
        async with DBConnectionService().access():
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
        async with self.memory_access():
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
        async with self.memory_access():
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
                response = await DBConnectionService().get_conn().execute(query)
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

    async def backup_memory(self, slot_id: int, flush_timeout: float = 2.0):
        """
        记忆存档（热备份）

        特性：
            1) 无论是否 initialized，都可以执行
            2) 自动尝试 flush（尽力而为）
            3) 不阻塞 Agent 正常运行
            4) 复制 db + wal，保证一致性恢复

        Args:
            slot_id(int): 存档序号。取值范围为[0, MAX_BACKUP_SLOTS-1]
            flush_timeout(float): flush 等待时间（秒）
        """
        async with self._backup_lock:
            if slot_id < 0 or slot_id >= self._max_backup_slots:
                raise ValueError(f"slot_id应在[0, {self._max_backup_slots-1}]范围内")
            print(f"[MemoryManager][记忆备份开始] slot={slot_id}")
            dbsvc = DBConnectionService()
            was_initialized = self._initialized
            try:
                if self._initialized:
                    # 1) freeze
                    await dbsvc.freeze()
                    # 2) flush 队列（尽力而为）
                    try:
                        print(f"[MemoryManager] flush记忆队列中(timeout={flush_timeout}s)")
                        flushed = await self.wait_memory_flush(timeout=flush_timeout)
                        if not flushed:
                            print("[MemoryManager] flush超时，备份将包含未刷新的WAL")
                    except Exception as e:
                        print(f"[MemoryManager] flush失败，备份将继续: {e}")
                    # 3) 等所有进行中的 access 退出
                    await dbsvc.wait_idle()
                    # 4) checkpoint
                    try:
                        print(f"[MemoryManager] checkpoint开始")
                        await dbsvc.get_conn().execute("CHECKPOINT")
                    except Exception as e:
                        print(f"[MemoryManager] checkpoint失败，备份将继续: {e}")
                    # 5) 关闭自身（Worker / graphiti / driver）
                    await self._close()
                    # 6) 关闭底层 DB（释放文件锁）
                    await dbsvc.close()
                else:
                    print("[MemoryManager] 未初始化，备份将继续")

                # 7) 准备 slot
                db_file = dbsvc.db_path
                wal_file = dbsvc.wal_path
                if not os.path.exists(db_file):
                    raise RuntimeError(f"无法执行存档：当前没有可保存的数据（数据库尚未创建）")

                os.makedirs(self._backup_root, exist_ok=True)
                slot_path = os.path.join(self._backup_root, f"slot_{slot_id}")

                if os.path.exists(slot_path):
                    print("[MemoryManager][覆盖已有备份]", f"slot={slot_id}")
                    shutil.rmtree(slot_path)
                os.makedirs(slot_path)

                # 8) 复制 db + wal
                try:
                    if os.path.exists(db_file):
                        shutil.copy2(db_file, os.path.join(slot_path, os.path.basename(db_file)))
                    if os.path.exists(wal_file):
                        shutil.copy2(wal_file, os.path.join(slot_path, os.path.basename(wal_file)))
                    print("[MemoryManager][记忆备份完成]", f"slot={slot_id}")
                except Exception as e:
                    print("[MemoryManager][记忆备份失败]", f"slot={slot_id}:", e)
                    raise
            finally:
                # 解 freeze（如果 dbsvc 还在）
                if dbsvc.is_initialized:
                    await dbsvc.unfreeze()
                # 重新 init（dbsvc + memory）
                if was_initialized and not self._initialized:
                    try:
                        self._reset_subsystems_for_reinitialize()
                        await dbsvc.initialize()
                        await self.initialize()
                    except Exception as e:
                        print("[MemoryManager] backup后自动恢复失败:", e)
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
            if not os.path.exists(slot_path):
                raise RuntimeError(f"slot {slot_id} 不存在")

            await self.close()
            dbsvc = DBConnectionService()
            await dbsvc.close()

            db_file = dbsvc.db_path
            wal_file = dbsvc.wal_path

            try:
                if os.path.exists(db_file):
                    os.remove(db_file)
                if os.path.exists(wal_file):
                    os.remove(wal_file)
                shutil.copy2(os.path.join(slot_path, os.path.basename(db_file)), db_file)
                backup_wal = os.path.join(slot_path, os.path.basename(wal_file))
                if os.path.exists(backup_wal):
                    shutil.copy2(backup_wal, wal_file)
            except Exception as e:
                print(f"[MemoryManager][记忆读档失败] slot={slot_id}: {e}")
                raise
            self._reset_subsystems_for_reinitialize()
            await dbsvc.initialize()
            await self.initialize()
            print(f"[MemoryManager] 读档完成 slot={slot_id}")

    async def list_used_slots(self) -> list[int]:
        """
        获取当前已占用的 slot_id 列表
        Return:
            List[int]
        """
        os.makedirs(self._backup_root, exist_ok=True)
        used_slots = []
        dbsvc_db_basename = os.path.basename(DBConnectionService().db_path)
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
                db_file = os.path.join(slot_path, dbsvc_db_basename)
                if os.path.exists(db_file):
                    used_slots.append(slot_id)
        used_slots.sort()
        return used_slots

    async def delete_backup_memory(self, slot_id: int):
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
        （该方法执行无需初始化MemoryManager）

        安全流程：
            1. 停止 worker
            2. 关闭连接
            3. 删除 db + wal
            4. 重置状态
        """
        async with self._backup_lock:
            print("[MemoryManager] 删除当前记忆开始")
            dbsvc = DBConnectionService()
            # 1. 关 MM + dbsvc
            await self.close()
            await dbsvc.close()
            # 2. 删除数据库文件和WAL文件
            db_file = dbsvc.db_path
            wal_file = dbsvc.wal_path

            try:
                if os.path.exists(db_file):
                    os.remove(db_file)
                    print("[MemoryManager] 已删除数据库文件")
                else:
                    print(f"[MemoryManager] 当前无数据库文件")
                if os.path.exists(wal_file):
                    os.remove(wal_file)
                    print("[MemoryManager] 已删除 WAL 文件")

            except Exception as e:
                print("[MemoryManager] 删除当前记忆失败:",e)
                raise

            # 3. 重新初始化
            self._reset_subsystems_for_reinitialize()
            await dbsvc.initialize()
            await self.initialize()
            print("[MemoryManager] 删除当前记忆完成")

        return True

    async def close(self):
        """显式关闭 MemoryManager 自身资源（worker / graphiti / driver）。
        不会关闭底层 DBConnectionService — 那个由调用方在需要时单独 close。
        """
        if not self._initialized:
            return
        try:
            # 让进行中的 access 自然退出（freeze + wait_idle 由调用方 backup_memory 负责）
            await self._close()
        except Exception as e:
            print(f"⚠️ MemoryManager.close 出错: {e}")

    async def _close(self):
        self._initialized = False
        print("🛑 [MM 1/2] 正在清除记忆任务队列worker")
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

        print("🛑 [MM 2/2] 正在释放 graphiti / driver 引用...")
        try:
            if self._kuzu_driver:
                self._kuzu_driver.db = None
                self._kuzu_driver = None
            self.graphiti = None
            print("✅ [MM] 自身资源已释放")
        except Exception as e:
            print(f"⚠️ MemoryManager 关闭资源时出错: {e}")