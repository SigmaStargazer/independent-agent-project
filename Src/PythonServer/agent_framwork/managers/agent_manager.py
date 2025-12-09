import asyncio

from agent_framwork.base.singleton import singleton
# from agent_framwork.agents.agent import Agent
from agent_framwork.agents.agent_with_mem import Agent

@singleton
class AgentManager:
    """
    管理所有的agent。
    单例，可直接 AgentManager().start() 这样的方式来使用内部的函数
    """
    def __init__(self):
        self.agents = {}
        self.processing_tasks = {}

    def add_agent(self, name:str, description:str):
        """
        添加agent到agent manager
        """
        if self.agents.get(name):
            print(f"[Agent Manager]: Agent {name}已存在")
        else:
            agent = Agent(name=name, description=description)
            self.agents[agent.name] = agent

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
