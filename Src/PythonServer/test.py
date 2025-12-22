import asyncio

from agent_framwork.managers.agent_manager import AgentManager
from agent_framwork.systems.time_system import TimeSystem
from agent_framwork.systems.alarm_system import AlarmSystem
from memory_system.memory_manager import MemoryManager

# async def async_callback(user_id, alarm_id, current_time):
#     print(f"[ASYNC] 用户 {user_id} 的闹钟 {alarm_id} 被触发 @ {current_time}")
#     await asyncio.sleep(1)
#     print("异步回调完成")

async def main():
    """
    记忆寻回
    """
    await MemoryManager().initialize()
    
    AgentManager().add_agent(name="小明", description="是一个帮助机器人")
    # AgentManager().add_agent(name="小红", description="是用户的秘书")
    AgentManager().start()

    await TimeSystem().aset_speed(1440)
    await TimeSystem().astart_time(year=2016,month=2,day=1)

    await AgentManager().agents["小明"].asend_message('来自用户: 小红是不是llm工程师？')

    await asyncio.sleep(300)
    AgentManager().finish()

    # """
    # 记忆存储
    # """
    # await MemoryManager().initialize()
    
    # AgentManager().add_agent(name="小明", description="是一个帮助机器人")
    # # AgentManager().add_agent(name="小红", description="是用户的秘书")
    # AgentManager().start()

    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().astart_time(year=2016,month=1,day=1)

    # await AgentManager().agents["小明"].asend_message('来自用户: 小红是公司的llm工程师')

    # await asyncio.sleep(30)

    # await AgentManager().agents["小明"].asend_message('来自用户: 小红转岗为Agent工程师了')

    # await asyncio.sleep(300)
    # AgentManager().finish()

    # """
    # 闹钟删除 测试2: 删除不存在的闹钟id
    # """
    # await MemorySystem().initialize()

    # AgentManager().add_agent(name="小明", description="是一个帮助机器人")
    # AgentManager().add_agent(name="小红", description="是用户的秘书")
    # AgentManager().start()

    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().astart_time(year=2016,month=1,day=1)

    # await AgentManager().agents["小明"].asend_message('闹个每天8点的起床铃，9点的上班铃声')

    # await asyncio.sleep(1)

    # await AgentManager().agents["小明"].asend_message('8点的起床铃删了吧，闹钟id为4的闹钟也删了')

    # await asyncio.sleep(60)
    # AgentManager().finish()

    # """
    # 闹钟删除 测试
    # """
    # AgentManager().add_agent(name="小明", description="是一个帮助机器人")
    # AgentManager().add_agent(name="小红", description="是用户的秘书")
    # AgentManager().start()

    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().astart_time(year=2016,month=1,day=1)

    # await AgentManager().agents["小明"].asend_message('闹个每天8点的起床铃，9点的上班铃声')

    # await asyncio.sleep(1)

    # await AgentManager().agents["小明"].asend_message('8点的起床铃删了吧')

    # await asyncio.sleep(60)
    # AgentManager().finish()

    # """
    # 闹钟列表 测试
    # """
    # AgentManager().add_agent(name="小明", description="是一个帮助机器人")
    # AgentManager().add_agent(name="小红", description="是用户的秘书")
    # AgentManager().start()

    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().astart_time(year=2016,month=1,day=1)

    # await AgentManager().agents["小明"].asend_message('闹个每天8点的上班铃')
    # await AgentManager().agents["小红"].asend_message('闹个今晚8点的下班铃')

    # await asyncio.sleep(1)

    # await AgentManager().agents["小明"].asend_message('再闹个今晚9点的下班铃')

    # await asyncio.sleep(1)

    # await AgentManager().agents["小明"].asend_message('提供给我get_alarm_list工具的输出')
    # await AgentManager().agents["小红"].asend_message('提供给我get_alarm_list工具的输出')

    # await asyncio.sleep(1)

    # await asyncio.sleep(60)
    # AgentManager().finish()

    # """
    # 闹钟测试 测试
    # """
    # AgentManager().add_agent(name="小明", description="是一个帮助机器人")
    # AgentManager().add_agent(name="小红", description="是用户的秘书")
    # AgentManager().start()

    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().astart_time(year=2016,month=1,day=1)

    # await AgentManager().agents["小明"].asend_message('今晚9点我要开会，提前1小时提醒我')
    # # await AgentManager().agents["小明"].send_message('你身边的agent有哪些？')
    # await asyncio.sleep(60)
    # AgentManager().finish()

    # """
    # 闹钟测试 混合测试
    # """
    # AgentManager().add_agent(name="小明", description="是一个帮助机器人")
    # AgentManager().add_agent(name="小红", description="是用户的秘书")
    # AgentManager().start()

    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().astart_time(year=2016,month=1,day=1)

    # await AgentManager().agents["小明"].asend_message('告诉我的秘书小红，今晚9点我要开会，让她提前1小时提醒我')
    # await asyncio.sleep(60)
    # AgentManager().finish()


    # """
    # 异步time system 测试
    # """
    # user_id = await AlarmSystem().acreate_user()
    # alarm_id = await AlarmSystem().aadd_alarm(user_id, 12, 30)
    # await AlarmSystem().aadd_callback_to_alarm(user_id, alarm_id, async_callback)

    # await TimeSystem().aset_speed(2880)
    # await TimeSystem().astart_time(2025, 6, 26)

    # # 等待一段时间让闹钟触发
    # await asyncio.sleep(60)

    # """
    # Time System 测试
    # """
    # AgentManager().add_agent(name="小明", description="是一个帮助机器人")
    # AgentManager().add_agent(name="小红", description="是一个聊天机器人")
    # AgentManager().start()

    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().astart_time(year=2016,month=1,day=1)

    # await AgentManager().agents["小明"].asend_message('帮我和小红跟小强说下，今晚加班。能通知到的尽量通知！')
    # # await AgentManager().agents["小明"].send_message('你身边的agent有哪些？')
    # await asyncio.sleep(20)
    # AgentManager().finish()


    # """
    # agent manager 测试
    # """
    # AgentManager().add_agent(name="小明", description="是一个帮助机器人")
    # AgentManager().add_agent(name="小红", description="是一个聊天机器人")
    # AgentManager().start()
    # await AgentManager().agents["小明"].send_message('帮我和小红跟小强说下，今晚加班。能通知到的尽量通知！')
    # # await AgentManager().agents["小明"].send_message('你身边的agent有哪些？')
    # await asyncio.sleep(20)
    # AgentManager().finish()

asyncio.run(main())
