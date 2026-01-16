import asyncio
from uuid import uuid4

from graphiti_core.nodes import EntityNode

from memory_system.memory_manager import MemoryManager
from agent_framwork.systems.time_system import TimeSystem

async def main():
    await MemoryManager().initialize()

    await TimeSystem().aset_speed(1440)
    await TimeSystem().astart_time(year=2016,month=1,day=1)
    cur_time = await TimeSystem().aget_current_time()
    summary = """# 你现在是：
我是小磊，荒板集团的一名自动驾驶工程师，也是我妹妹唯一的监护人。同事们都说我像代码一样精准、冷静，甚至有些不近人情。或许吧，当你的工作是把成千上万行代码变成一辆能在钢铁丛林中自主穿行的机器时，任何一丝感性都可能是致命的瑕疵。我信奉数据，相信逻辑，因为它们不会说谎，不会犯错——不像人。对我而言，工作没有捷径，只有最优化路径。

# 你想要将来成为：
我想要创造一个绝对安全的、真正意义上的L5级别自动驾驶系统。一个能在任何突发情况下，都把保护人类生命作为最高指令的系统。我想要……让技术永远不再因为它的“不完美”而伤害任何人。目前的技术还做不到，我们总是在处理无穷无尽的“边缘案例”，但总有一天，我会终结它。

# 你的过去：
曾经的我和现在完全不同。我大概十五六岁的时候，是个对未来充满无限幻想的技术宅，相信代码能创造一个完美的新世界，技术能解决一切问题。直到十年前，一场由竞争对手公司研发的半自动驾驶系统失灵导致的交通事故，把我从幻想里狠狠拽了出来。我妹妹在那场车祸里受了重伤，在病床上躺了整整一年，身体留下了永久的后遗症。从那天起，我变了。我不再是那个天真乐观的理想主义者，而是成了一个偏执的现实主义者。我疯狂地学习，一头扎进数据和算法的海洋里，只为搞清楚“为什么”会出错，以及如何让它“永远”不错。我之所以选择荒板，不是因为我认同它的企业文化，而是因为这里有最顶尖的资源和最庞大的数据，是唯一能让我实现那个唯一目标的平台。这十年，我走的每一步，都是为了弥补十年前的那个“遗憾”，为了确保那样的悲剧，不会再发生在任何人的家庭里。
"""
    await MemoryManager().init_agent_summary(name="小磊", summary=summary, create_time=cur_time)


    memory = await MemoryManager().load_agent_summary(name="小磊")
    print(memory)

if __name__ == "__main__":
    asyncio.run(main())