"""
"""

from datetime import datetime
import asyncio

from agent_framwork.managers.agent_manager import AgentManager
from agent_framwork.systems.time_system import TimeSystem
from memory_system.memory_manager import MemoryManager

async def init_create():
    await MemoryManager().initialize()
    await TimeSystem().aset_time(year=2016, month=1, day=1)

    result = await AgentManager().acreate_agent(name="小明", summary="是一个帮助机器人", create_time=datetime.now())
    agent_names = await AgentManager().aload_agent()
    await AgentManager().astart()

async def init_load():
    await MemoryManager().initialize()
    await TimeSystem().aset_time(year=2016, month=1, day=1)

    agent_names = await AgentManager().aload_agent()
    await AgentManager().astart()

async def test1():
    """
场景一测试：
测试一：
1）启动场景，发消息
2）AgentManager.ainterrupt，然后发场景消息
3）astart，然后再发场景“进入新场景”的消息
    """
    await init_create()
    await AgentManager().agents['小明'].asend_message("用户: 您好，你记得哪些和小红有关的事情吗？")
    await asyncio.sleep(5)
    # await AgentManager().ainterrupt(reason="进入新场景")
    await AgentManager().agents['小明'].asend_message("系统: 进入新场景。用户不在该场景内，不再讨论小红的事情")
    await asyncio.sleep(10)
    await AgentManager().astart()


if __name__ == "__main__":
    asyncio.run(test1())