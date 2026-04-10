import asyncio
from datetime import datetime
from typing import Dict

from agent_framwork.base.singleton import singleton
# from agent_framwork.agents.agent import Agent
from agent_framwork.agents.agent_with_mem import Agent

from memory_system.memory_manager import MemoryManager

@singleton
class AgentManager:
    """
    管理所有的agent。
    单例，可直接 AgentManager().start() 这样的方式来使用内部的函数
    """
    def __init__(self):
        self.agents: Dict[str, Agent] = {}
        self.processing_tasks = {}

    async def acreate_agent(self, name:str, summary:str, create_time:datetime) -> str:
        """
        创建agent，存入self.agents。并将简介存入graphiti中
        Args:
            name(str): agent名称
            summary(str): agent简介
            cur_time(datetime): 当前时间
        return:
            str: 创建结果
        """
        # 1.1. 检查agent manager是否已有该agent
        if self.agents.get(name):
            return f"[Agent Manager]: Agent {name} 已存在"

        # 1.2. 检查graphiti是否已有该agent
        group_id = name.encode('utf-8').hex()
        cypher = f"""
MATCH (n: Entity {{group_id: '{group_id}'}})
RETURN n"""
        result = await MemoryManager().conn.execute(cypher)
        if result.has_next():
            return f"[Agent Manager]: Agent {name} 已存在"

        # 2. 创建agent，存self.agents
        agent = Agent(name=name)
        self.agents[agent.name] = agent

        # 3. 初始化agent的设定和记忆
        await MemoryManager().init_agent_summary(
            name=name, 
            summary=summary, 
            create_time=create_time)

        return f"[Agent Manager]: Agent {name} 创建成功"

    # async def create_agent(self, name:str, description:str):
    #     """
    #     添加agent到agent manager
    #     """
    #     if self.agents.get(name):
    #         print(f"[Agent Manager]: Agent {name}已存在")
    #         return

    #     # 创建agent
    #     agent = Agent(name=name, description=description)
    #     self.agents[agent.name] = agent

    async def aload_agent(self) -> list[str]:
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

    def start(self):
        """
        开始所有agent的process_message协程
        """
        for name, agent in self.agents.items():
            self.processing_tasks[name] = asyncio.create_task(agent.aprocess_message())  # 创建任务后不再取消
        print("[Agent Manager]: 已启动所有Agent")
        print("-" * 80)

    def finish(self):
        """
        结束所有agent的process_message协程
        """
        for name, processing_task in self.processing_tasks.items():
            processing_task = processing_task.cancel()
        print("-" * 80)
        print("[Agent Manager]: 已停止所有Agent")
