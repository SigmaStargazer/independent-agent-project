import asyncio
from datetime import datetime
from typing import Dict

from agent_framwork.base.singleton import singleton
# from agent_framwork.agents.agent import Agent
from agent_framwork.agents.agent_interuptible import Agent

from memory_system.memory_manager import MemoryManager

@singleton
class AgentManager:
    """
    管理所有的agent。
    单例，可直接 AgentManager().start() 这样的方式来使用内部的函数
    """
    def __init__(self):
        self.agents: Dict[str, Agent] = {}

    # =========================================================================
    # 创建
    # =========================================================================

    async def acreate_agent(self, name:str, summary:str, create_time:datetime):
        """
        创建agent，存入self.agents。并将简介存入graphiti中
        Args:
            name(str): agent名称
            summary(str): agent简介
            cur_time(datetime): 当前时间
        """
        # 1. 检查agent manager是否已有该agent
        if self.agents.get(name):
            error_msg = f"[Agent Manager]创建Agent失败: Agent {name} 已存在"
            print(error_msg)
            raise ValueError(error_msg)

        # 2. 检查graphiti是否已有该agent
        group_id = name.encode('utf-8').hex()
        cypher = f"""
MATCH (n: Entity {{group_id: '{group_id}'}})
RETURN n"""
        result = await MemoryManager().conn.execute(cypher)
        if result.has_next():
            error_msg = f"[Agent Manager]创建Agent失败: Agent {name} 已存在"
            print(error_msg)
            raise ValueError(error_msg)
        # 3. 创建agent，存self.agents
        agent = Agent(name=name)
        self.agents[agent.name] = agent

        # 4. 初始化agent的设定和记忆
        await MemoryManager().init_agent_summary(
            name=name, 
            summary=summary, 
            create_time=create_time)

        print(f"[Agent Manager]创建Agent成功: Agent {name}")
        print("-" * 80)

    # =========================================================================
    # 加载
    # =========================================================================
    async def aload_agent(self, name: str):
        """
        从graphiti加载指定Agent
        Args:
            name(str): Agent名称
        """
        if name in self.agents:
            error_msg = f"[Agent Manager]加载Agent失败: Agent {name} 已存在"
            print(error_msg)
            raise ValueError(error_msg)

        group_id = name.encode("utf-8").hex()
        cypher = f"""
MATCH (n)
WHERE n.group_id = '{group_id}'
RETURN n
LIMIT 1
"""

        result = await MemoryManager().conn.execute(cypher)
        if not result.has_next():
            error_msg = f"[Agent Manager]加载Agent失败: Agent {name} 不存在"
            print(error_msg)
            raise ValueError(error_msg)

        agent = Agent(name=name)
        self.agents[name] = agent

        print(f"[Agent Manager]加载Agent成功: Agent {name}")
        print("-" * 80)


    async def aload_agent_all(self) -> list[str]:
        """
        从graphiti加载agent到self.agents
        Return:
            list[str]: agent名称列表
        """
        # 1. 获取kuzu中的所有group_id
        cypher = f"""
        MATCH (n)
        WHERE n.group_id IS NOT NULL
        RETURN DISTINCT n.group_id as group_id"""

        response = await MemoryManager().conn.execute(cypher)
        agent_names = []
        for row in response.rows_as_dict():
            group_id = row['group_id']
            name = bytes.fromhex(group_id).decode('utf-8')
            agent_names.append(name)
            if name not in self.agents:
                agent = Agent(name=name)
                self.agents[agent.name] = agent
        # print(f"加载Agent: {agent_names}")
        return agent_names

    # =========================================================================
    # 移除
    # =========================================================================

    async def aremove_agent(self, name: str):
        """
        从manager移除Agent
        不删除graphiti中的数据
        """
        if name not in self.agents:
            error_msg = f"[Agent Manager]移除Agent失败: Agent {name} 不存在"
            print(error_msg)
            raise ValueError(error_msg)

        # 先finish
        await self.agents[name].afinish()
        del self.agents[name]

    # =========================================================================
    # 发消息
    # =========================================================================

    async def asend_message(self, name: str, message: str, force_interrupt: bool = False):
        """
        发送消息给指定Agent
        Args:
            name(str): Agent名称
            message(str): 消息内容
            force_interrupt(bool): 是否强制打断
        """
        # 1. 检查Agent是否存在
        if name not in self.agents:
            error_msg = f"[Agent Manager]向Agent发送消息失败: Agent {name} 不存在"
            print(error_msg)
            raise ValueError(error_msg)
        # 2. 判读是否需要打断
        if not (self.agents[name].runtime_state["focus_mode"] and not force_interrupt):# 专注模式下且非强制打断时，不打断
            await self.agents[name].ainterrupt(reason="被打断")
        # 3. 发送消息
        await self.agents[name].asend_message(message)
        # 4. 重启
        if not (self.agents[name].runtime_state["focus_mode"] and not force_interrupt):
            await self.agents[name].astart()

    async def asend_message_all(
        self,
        message: str,
        force_interrupt: bool = False
    ):
        """
        给所有Agent发送消息
        Args:
            message(str): 消息内容
            force_interrupt(bool): 是否强制打断
        """

        await asyncio.gather(*[
            self.asend_message(
                name=name,
                message=message,
                force_interrupt=force_interrupt
            ) for name in self.agents])

    # =========================================================================
    # 启动
    # =========================================================================

    async def astart(self, name: str):
        """
        启动指定Agent
        Args:
            name(str): Agent名称
        """
        if name not in self.agents:
            error_msg = f"[Agent Manager]启动Agent失败: Agent {name} 不存在"
            print(error_msg)
            raise ValueError(error_msg)
        await self.agents[name].astart()

    async def astart_all(self):
        """
        启动所有Agent
        """
        await asyncio.gather(*[
            agent.astart()
            for agent in self.agents.values()
        ])

        print("[Agent Manager]: 已启动所有Agent")
        print("-" * 80)

    # =========================================================================
    # 打断
    # =========================================================================

    async def ainterrupt(self, name: str, reason: str = "系统关闭"):
        """
        打断指定Agent
        Args:
            name(str): Agent名称
            reason(str): 打断原因
        """
        if name not in self.agents:
            error_msg = f"[Agent Manager]打断Agent失败: Agent {name} 不存在"
            print(error_msg)
            raise ValueError(error_msg)
        await self.agents[name].ainterrupt(reason)

    async def ainterrupt_all(self, reason: str = "系统关闭"):
        """
        打断所有Agent
        Args:
            reason(str): 打断原因
        """
        await asyncio.gather(*[
            agent.ainterrupt(reason)
            for agent in self.agents.values()
        ])

        print("[Agent Manager]: 已打断所有Agent")
        print("-" * 80)
    
    # =========================================================================
    # 结束
    # =========================================================================
    async def afinish(self, name: str):
        """
        结束指定Agent
        Args:
            name(str): Agent名称
        """
        if name not in self.agents:
            error_msg = f"[Agent Manager]结束Agent失败: Agent {name} 不存在"
            print(error_msg)
            raise ValueError(error_msg)
        await self.agents[name].afinish()

    async def afinish_all(self):
        """
        结束所有Agent
        """
        await asyncio.gather(*[
            agent.afinish()
            for agent in self.agents.values()
        ])

        print("[Agent Manager]: 已停止所有Agent")
        print("-" * 80)
