import asyncio
from datetime import datetime
from agent_framwork.managers.agent_manager import AgentManager
from memory_system.memory_manager import MemoryManager
from agent_framwork.systems.time_system import TimeSystem

async def test():
    await MemoryManager().initialize()
    await TimeSystem().aset_time(year=2016, month=1, day=1)

    result = await AgentManager().acreate_agent(name="小明", summary="是一名应届生，目前正在求职agent工程师岗位", create_time=datetime.now())
    agent_names = await AgentManager().aload_agent()
    await AgentManager().astart()
    await AgentManager().agents['小明'].asend_message("你好，小明，请做一个自我介绍")
    await asyncio.sleep(10)
    await AgentManager().agents['小明'].asend_message("对于我们公司，你有什么想要了解的吗？")
    await asyncio.sleep(100)
    await AgentManager().agents['小明'].asend_message("对于你的岗位，你有哪些期待？")
    await asyncio.sleep(500)
    result = await AgentManager().afinish()

async def test2():
    """
    读取事实记忆
    """
    await MemoryManager().initialize()
    # await TimeSystem().aset_time(year=2016, month=1, day=1)
    mem_fact = await MemoryManager().search_fact_memory(name="小明", query="有哪些关于小红的信息", limit=10)
    print(mem_fact)

if __name__ == "__main__":
    asyncio.run(test2())