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
    1.9 简单测试
    """
    # # 初始化
    # await MemoryManager().initialize()
    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().aset_time(year=2016,month=1,day=1)
    # # await time_system.astart_time()    # 先不启动
    # name = "小明"
    # summary = "是一个帮助机器人"
    # cur_time = await TimeSystem().aget_current_time()
    # await AgentManager().create_agent(
    #     name=name, 
    #     summary=summary, 
    #     create_time=cur_time
    #     )

    # await TimeSystem().astart_time()    # 先不启动
    # AgentManager().start()

    # await AgentManager().agents["小明"].asend_message('用户: 小红是公司的llm工程师')
    # # await asyncio.sleep(5)
    # # await AgentManager().agents["小明"].asend_message('用户: 小红转岗为Agent工程师了')

    # await asyncio.sleep(60)
    # AgentManager().finish()

    # 加载对话
    await MemoryManager().initialize()
    await TimeSystem().aset_speed(1440)
    await TimeSystem().aset_time(year=2016,month=1,day=1)

    agent_names = await AgentManager().aload_agent()
    print(f"加载Agent成功: {agent_names}")

    # await TimeSystem().astart_time()    # 先不启动
    # AgentManager().start()

    # # # await TimeSystem().aset_time(year=2016,month=1,day=2)
    # # # await AgentManager().agents["小明"].asend_message('用户: 小红转岗为Agent工程师了')

    # await TimeSystem().aset_time(year=2016,month=1,day=3)
    # await AgentManager().agents["小明"].asend_message('用户: 小红的岗位是啥？')

    # # await TimeSystem().aset_time(year=2016,month=1,day=4)
    # # await AgentManager().agents["小明"].asend_message('小亮: 2号发生过什么事情吗？')

    # await asyncio.sleep(60)
    # AgentManager().finish()

    # """"
    # 1.8 记忆检索测试
    # """
    # await MemoryManager().initialize()

    # # 事实记忆
    # mem_fact = await MemoryManager().search_fact_memory(name="小明", query="工程师", limit=2)
    # print(mem_fact)

    # # 情景记忆
    # mem_episode = await MemoryManager().search_episode_memory(name="小磊", query="小落是谁？", limit=1)
    # print(mem_episode)


    # """
    # 1.8 对话agent
    # """
    # await MemoryManager().initialize()
    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().aset_time(year=2016,month=1,day=1)

    # await AgentManager().load_agent()

    # await TimeSystem().astart_time()    # 先不启动
    # AgentManager().start()

    # # await AgentManager().agents["小落"].asend_message('一个奇怪的男人: 你认识小磊吗？')
    # # await AgentManager().agents["小磊"].asend_message('老板: 鉴于你协助黑客入侵公司的服务器，你被开除了！')
    # await AgentManager().agents["小落"].asend_message('(荒板的杀手突然闯进家门)')

    # await asyncio.sleep(300)
    # AgentManager().finish()

    
    # """
    # 1.8 加载agent
    # """
    # await MemoryManager().initialize()
    
    # await AgentManager().load_agent()
    # agents = AgentManager().agents
    # print(agents)


#     """
#     1.8 创建agent
#     """
#     await MemoryManager().initialize()
#     await TimeSystem().aset_speed(1440)
#     await TimeSystem().aset_time(year=2016,month=1,day=1)
#     # await time_system.astart_time()    # 先不启动
#     name = "小磊"
#     summary = """
#     # 你现在是：
# 我是小磊，荒板集团的一名自动驾驶工程师，也是我妹妹小落的唯一的监护人。同事们都说我像代码一样精准、冷静，甚至有些不近人情。或许吧，当你的工作是把成千上万行代码变成一辆能在钢铁丛林中自主穿行的机器时，任何一丝感性都可能是致命的瑕疵。我信奉数据，相信逻辑，因为它们不会说谎，不会犯错——不像人。对我而言，工作没有捷径，只有最优化路径。

# # 你想要将来成为：
# 我想要创造一个绝对安全的、真正意义上的L5级别自动驾驶系统。一个能在任何突发情况下，都把保护人类生命作为最高指令的系统。我想要……让技术永远不再因为它的“不完美”而伤害任何人。目前的技术还做不到，我们总是在处理无穷无尽的“边缘案例”，但总有一天，我会终结它。

# # 你的过去：
# 曾经的我和现在完全不同。我大概十五六岁的时候，是个对未来充满无限幻想的技术宅，相信代码能创造一个完美的新世界，技术能解决一切问题。直到十年前，一场由竞争对手公司研发的半自动驾驶系统失灵导致的交通事故，把我从幻想里狠狠拽了出来。我妹妹在那场车祸里受了重伤，在病床上躺了整整一年，身体留下了永久的后遗症。从那天起，我变了。我不再是那个天真乐观的理想主义者，而是成了一个偏执的现实主义者。我疯狂地学习，一头扎进数据和算法的海洋里，只为搞清楚“为什么”会出错，以及如何让它“永远”不错。我之所以选择荒板，不是因为我认同它的企业文化，而是因为这里有最顶尖的资源和最庞大的数据，是唯一能让我实现那个唯一目标的平台。这十年，我走的每一步，都是为了弥补十年前的那个“遗憾”，为了确保那样的悲剧，不会再发生在任何人的家庭里。
#     """
#     cur_time = await TimeSystem().aget_current_time()
#     await AgentManager().create_agent(
#         name=name, 
#         summary=summary, 
#         create_time=cur_time
#         )

#     name = "小落"
#     summary = """
#     # 你现在是：
# 我是小落，那个被小磊过度保护的妹妹，也是他口中那个必须被“绝对安全”守护的人。在外人眼里，我大概是个柔弱的幸存者，但我更愿意把自己看作是那个只会敲代码的“冰块哥哥”的体温维持者。我也许身体不如常人利索，甚至连走路都要小心翼翼，但我依然喜欢笑，喜欢感受阳光的温度。我知道哥哥觉得我是易碎的瓷器，但我努力让自己成为一颗即使有裂痕也能折射光芒的钻石。在这个充满钢铁和霓虹的城市里，我是唯一见过他卸下防备、甚至还会红眼眶的人。

# # 你想要将来成为：
# 我想要成为能够“独立行走”的人，不仅仅是指身体上的康复，更是精神上的自立。我想要有一天，哥哥看我的时候，眼里不再是愧疚、遗憾和那种令人窒息的责任感，而是纯粹的欣赏和平静。我想要证明给他看，这个世界虽然有像代码一样复杂的恶意，但也有像花朵一样顽强的生机。最终，我希望能成为他的“终点站”，让他那辆永远在计算最优路径、永远在狂奔的列车，能够有个理由安稳地停下来，不再为了十年前的那个影子而活。

# # 你的过去：
# 十年前，我是个跟在哥哥屁股后面，只会对着他敲出来的简易小游戏傻笑的小丫头。那时的哥哥，眼里有光，会指着屏幕上的代码跟我说，那是通往未来的魔法。然后，那场车祸把一切都撞碎了。我在医院的消毒水味里度过了整整一年，那是身体最痛的一年，也是看着哥哥“死去”的一年。当我醒来时，那个会大笑的哥哥不见了，取而代之的是一个只会盯着我各项生理数据的陌生人。这十年，我看着他把自己变成了荒板的一颗螺丝钉，看着他为了所谓的“安全”把自己的人性一点点剥离。我知道他在赎罪，用他的全部人生去试图抹平那个“如果”。作为幸存者，我不仅要忍受身体的残痛，还要背负着“因为我不够完美才导致哥哥痛苦”的隐秘愧疚。但我活下来了，不仅仅是为了被保护，更是为了见证那个天真少年，是否还能在数据的废墟中找回一丝温度。
#     """
#     cur_time = await TimeSystem().aget_current_time()
#     await AgentManager().create_agent(
#         name=name, 
#         summary=summary, 
#         create_time=cur_time
#         )




    # """
    # 记忆寻回
    # """
    # await MemoryManager().initialize()
    
    # await AgentManager().create_agent(name="小明", description="是一个帮助机器人")
    # # AgentManager().add_agent(name="小红", description="是用户的秘书")
    # AgentManager().start()

    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().astart_time(year=2016,month=2,day=1)

    # await AgentManager().agents["小明"].asend_message('来自用户: 小红是不是llm工程师？')

    # await asyncio.sleep(300)
    # AgentManager().finish()

    # """
    # 记忆存储
    # """
    # await MemoryManager().initialize()
    
    # await AgentManager().create_agent(name="小明", description="是一个帮助机器人")
    # # AgentManager().add_agent(name="小红", description="是用户的秘书")
    # AgentManager().start()

    # await TimeSystem().aset_speed(1440)
    # await TimeSystem().astart_time(year=2016,month=1,day=1)

    # print(AgentManager().agents)
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
