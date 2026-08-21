# -*- coding: utf-8 -*-
"""AgentLifecycle: 进游戏 / 回 Title 的统一生命周期编排入口（v0.23.0b 生命周期重构）。

职责：
    - enter_game(): 进游戏时注入最新 api_config.json 并全新初始化记忆系统（幂等）。
    - leave_game():  回 Title 时停止 Agent、清 LLM 缓存、归零时间并关闭全部系统（幂等）。

设计依据见 `DevDocs/Architecture/生命周期架构.md`：
    - Title 阶段零系统，进游戏才初始化，回 Title 就关闭。
    - 关闭顺序：先 Agent（停止推理）→ 清 LLM 缓存 → 归零时间 → 再 Memory → DB → Embedder。
"""
from __future__ import annotations

from memory_system import MemoryManager
from memory_system.db_conn import DBConnectionService
from memory_system.embedder import EmbedderService
from agent_framwork.managers.agent_manager import AgentManager
from agent_framwork.systems.time_system import TimeSystem
from agent_framwork.agents.agent_interuptible import reset_llm_cache
from config.api_config_loader import load_api_config_into_env


class AgentLifecycle:
    """进游戏 / 回 Title 的统一生命周期入口。"""

    @staticmethod
    async def enter_game() -> None:
        """进游戏：注入最新配置并全新初始化。
        - 幂等：已在游戏内（已初始化）则直接返回，不重复初始化。
        - force=True：无条件用 api_config.json 覆盖 os.environ——
          回 Title 再进游戏时 env 可能残留上一局旧 Key，必须强制刷新才能让新 Key 生效。"""
        # 时钟真正走动仍由 SceneStart 的 astart_time() 负责。见 DevDocs/Architecture/生命周期架构.md §2.2。
        await TimeSystem().aset_time(year=2016, month=1, day=1)
        
        if MemoryManager().is_initialized:
            print("[lifecycle] 已在游戏中（已初始化），跳过 enter_game")
            return
        load_api_config_into_env(force=True)      # 读最新 api_config.json 注入 env
        await MemoryManager().initialize()        # 内部 init dbsvc + embedder + graphiti + action_skill
        # 设置虚拟时间基准（不启动时钟）：CreateAgent/LoadAgent 在 SceneStart 之前执行，
        # 需要此时就能取到非 None 的当前时间（EntityNode.created_at 等）。

        print("[lifecycle] enter_game 完成，系统已初始化")

    @staticmethod
    async def leave_game() -> None:
        """回 Title：停止全部 Agent、归零时间并关闭全部系统，回到零系统状态。幂等。
        - 停止 Agent / 归零时间始终执行（即使记忆系统未初始化，也要兜底清理）。
        - 关闭资源仅在已初始化时执行。"""
        await AgentManager().aremove_all()        # 1. 停止并移除全部 Agent（始终执行）
        reset_llm_cache()                         # 2. 清 Agent LLM 缓存（agent_interuptible）
        await TimeSystem().areset()               # 3. 暂停并归零虚拟时间（始终执行）
        if not MemoryManager().is_initialized:
            print("[lifecycle] 记忆系统本就在零系统状态，跳过资源关闭")
            return
        await MemoryManager().close()             # 4. 关 MM（worker/graphiti/driver）
        await DBConnectionService().close()       # 5. 关 Kuzu 连接
        await EmbedderService().close()           # 6. 关 Embedder/Reranker
        print("[lifecycle] leave_game 完成，已关闭全部系统，回到零系统状态")
