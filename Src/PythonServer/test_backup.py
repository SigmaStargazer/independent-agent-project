"""
测试记忆备份和读档
- backup生效：备份到slot_id = 0，观察是否有对应文件生成（debug模式看生成文件的路径）
	- 小红是公司的hr
- backup生效2：删除kuzu文件，备份到slot_id = 1，输入不同的记忆
	- 小红是公司的agent工程师
- restore：
	- 获取slot_id = 0，询问小红是谁
	- 获取slot_id = 1，询问小红是谁
"""
from datetime import datetime
import asyncio

from agent_framwork.managers.agent_manager import AgentManager
from agent_framwork.systems.time_system import TimeSystem
from memory_system.memory_manager import MemoryManager



async def test_backup():
    await MemoryManager().initialize()
    await TimeSystem().aset_time(year=2016, month=1, day=1)

    result = await AgentManager().acreate_agent(name="小明", summary="是一个帮助机器人", create_time=datetime.now())
    agent_names = await AgentManager().aload_agent()
    AgentManager().start()

    slot_id = 0
    # 对话：小红是公司的hr
    await AgentManager().agents['小明'].asend_message("小红是公司的hr")
    await asyncio.sleep(10)


async def test_restore():
    pass


if __name__ == "__main__":
    pass
