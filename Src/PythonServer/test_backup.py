"""
测试记忆备份和读档
- （通过）记忆删除：观察当前kuzu文件是否会被删除
- （通过）列举当前使用的slot_id：观察是否报没有文件
- （未通过）backup生效：与agent对话，等记忆写入完毕后备份到slot_id = 0，观察是否有对应文件生成（debug模式看生成文件的路径）
	- 小红是公司的hr
- （未通过）backup生效2：删除kuzu文件，与agent对话，等记忆写入完毕后备份到slot_id = 1，输入不同的记忆
	- 小红是公司的agent工程师
- 不启动agent，直接备份：观察是否有对应文件生成（debug模式看生成文件的路径）
	- 备份到slot_id = 2
- 列举当前使用的slot_id2：观察是否是0和1
- restore：
	- 获取slot_id = 0，询问小红是谁
	- 获取slot_id = 1，询问小红是谁
- 记忆备份删除：观察slot的kuzu文件是否会被删除
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
    AgentManager().start()

async def init_load():
    await MemoryManager().initialize()
    await TimeSystem().aset_time(year=2016, month=1, day=1)

    agent_names = await AgentManager().aload_agent()
    AgentManager().start()

async def test_1_memory_delete():
    await MemoryManager().delete_current_memory()

async def test_2_list_used_slots():
    used_slots = await MemoryManager().list_used_slots()
    print(f"当前使用的slot_id: {used_slots}")

async def test_3_backup():
    # 删除当前记忆文件
    result = await MemoryManager().delete_current_memory()
    # 重新对话
    await init_create()
    slot_id = 0
    # 对话：小红是公司的hr
    await AgentManager().agents['小明'].asend_message("小红是公司的hr")
    # 等待对话完成，并且wait_memory_flush结果为True
    await asyncio.sleep(180)
    while True:
        flushed = await MemoryManager().wait_memory_flush(timeout=2.0)
        if flushed:
            break
        await asyncio.sleep(1)
    await MemoryManager().backup_memory(slot_id=slot_id)
    used_slots = await MemoryManager().list_used_slots()
    print(f"已备份记忆:: slot_id: {used_slots}")

async def test_4_backup_2():
    # 删除当前记忆文件
    result = await MemoryManager().delete_current_memory()
    # 重新对话
    await init_create()
    slot_id = 1
    # 对话：小红是公司的agent工程师
    await AgentManager().agents['小明'].asend_message("小红是公司的agent工程师")
    # 等待对话完成，并且wait_memory_flush结果为True
    await asyncio.sleep(180)
    while True:
        flushed = await MemoryManager().wait_memory_flush(timeout=2.0)
        if flushed:
            break
        await asyncio.sleep(1)
    await MemoryManager().backup_memory(slot_id=slot_id)
    used_slots = await MemoryManager().list_used_slots()
    print(f"已备份记忆:: slot_id: {used_slots}")

async def test_5_backup_3():
    """
    - 不启动agent，直接备份：观察是否有对应文件生成（debug模式看生成文件的路径）
    - 备份到slot_id = 2
    """
    slot_id = 2
    await MemoryManager().backup_memory(slot_id=slot_id)
    used_slots = await MemoryManager().list_used_slots()
    print(f"已备份记忆:: slot_id: {used_slots}")

async def test_6_list_used_slots():
    used_slots = await MemoryManager().list_used_slots()
    print(f"当前使用的slot_id: {used_slots}")

async def test_7_restore_1():
    """
    - 获取slot_id = 0，询问小红是谁
    """
    slot_id = 0
    await MemoryManager().restore_memory(slot_id=slot_id)
    print(f"已恢复记忆:: slot_id: {slot_id }")
    await MemoryManager().initialize()
    await TimeSystem().aset_time(year=2016,month=1,day=1)
    agent_names = await AgentManager().aload_agent()
    AgentManager().start()
    print(f"加载Agent成功: {agent_names}")
    await AgentManager().agents['小明'].asend_message("小红是谁")
    await asyncio.sleep(300)
    result = await AgentManager().afinish()

async def test_8_restore_2():
    """
    - 获取slot_id = 1，询问小红是谁
    """
    slot_id = 1
    await MemoryManager().restore_memory(slot_id=slot_id)
    print(f"已恢复记忆:: slot_id: {slot_id }")
    await MemoryManager().initialize()
    await TimeSystem().aset_time(year=2016,month=1,day=1)
    agent_names = await AgentManager().aload_agent()
    AgentManager().start()
    print(f"加载Agent成功: {agent_names}")
    await AgentManager().agents['小明'].asend_message("小红是谁")
    await asyncio.sleep(300)
    result = await AgentManager().afinish()

async def test_9_memory_delete():
    """
    - 记忆备份删除：观察slot的kuzu文件是否会被删除
    """
    await MemoryManager().delete_backup_memory(slot_id=2)
    used_slots = await MemoryManager().list_used_slots()
    print(f"当前使用的slot_id: {used_slots}")

if __name__ == "__main__":
    asyncio.run(test_9_memory_delete())
