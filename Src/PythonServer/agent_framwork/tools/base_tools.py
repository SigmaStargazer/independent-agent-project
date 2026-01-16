from langchain_core.tools import tool
from typing_extensions import Annotated
from langgraph.prebuilt import InjectedState

from graphiti_core.search import search_config_recipes

from agent_framwork.systems.alarm_system import AlarmSystem
from agent_framwork.systems.time_system import TimeSystem

# tool中调用langgraph中state内参数的方式，请参考下面的 InjectedState 中的内容：
# https://langchain-ai.github.io/langgraph/reference/agents/#langgraph.prebuilt.tool_node.ToolNode.inject_tool_args

@tool
async def communicate_to_agent(sender: Annotated[str, InjectedState("name")],recipient: str, message: str) -> str:
    """向目标agent发送一则消息
    Args:
        recipient(str): 信息接收人名字
        message(str): 你想要发送的消息
    """
    if sender == recipient:
        return f"你刚刚在自言自语"
    from agent_framwork.managers.agent_manager import AgentManager
    if recipient in AgentManager().agents:
        await AgentManager().agents[recipient].asend_message(f"{sender}: {message}")
        return f"[{sender}]向Agent[{recipient}]发送了一则消息: {message}"
    else:
        return f"收信人[{recipient}]不存在！"
    
# @tool
# async def communicate_to_user(agent: Annotated[str, InjectedState("name")], message: str) -> str:
#     """向用户发送一则消息
#     Args:
#         message(str): 你想要发送的消息
#     """
#     # from network.servers import AgentServerProtobuff
#     from network.servers import AgentServerNetMessage
#     try:
#         request = AgentServerNetMessage().message_types['AgentSendMessageRequest']()
#         request.agent = agent
#         request.ai_message = message
#         await AgentServerNetMessage().broadcast_message(request)
#         print(f"[{agent}]向用户发送消息成功: {message}")
#         return f"你向用户发送了一则消息: {message}"
#     except Exception as e:
#         print(f"[{agent}]向用户发送消息失败: {message}, {e}")
#         return f"你向用户发送消息失败: {e}"

async def communicate_to_user(agent: Annotated[str, InjectedState("name")], message: str) -> str:
    """向用户发送一则消息
    Args:
        message(str): 你想要发送的消息
    """
    from network.servers import AgentServerNetMessage
    from network import message_pb2
    try:
        request = message_pb2.AgentSendMessageRequest()
        request.agent = agent
        request.ai_message = message
        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}]向用户发送消息成功: {message}")
        return f"你向用户发送了一则消息: {message}"
    except Exception as e:
        print(f"[{agent}]向用户发送消息失败: {message}, {e}")
        return f"你向用户发送消息失败: {e}"

@tool
async def move(agent: Annotated[str, InjectedState("name")], direction: str, distance: float) -> str:
    """向指定方向移动指定距离
    Args:
        direction(str): 方向，填left或者right
        distance(float): 距离
    """
    if direction not in ["left", "right"]:
        return "方向错误，请填left或者right"
    from network.servers import AgentServerNetMessage
    from network import message_pb2

    try:
        request = message_pb2.AgentMoveRequest()
        request.is_right = direction == "right"
        request.distance = distance
        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}]向开始向{direction}移动了{distance}距离，请等待移动完成")
        return f"[{agent}]向开始向{direction}移动了{distance}距离，请等待移动完成"
    except Exception as e:
        return f"移动失败: {e}"

@tool
async def get_agent_list() -> list:
    """获取所有agent的清单"""
    from agent_framwork.managers.agent_manager import AgentManager
    return list(AgentManager().agents.keys())

# 时间相关的工具
@tool
async def get_cur_time() -> str:
    """获取当前时间"""
    now = await TimeSystem().aget_current_time()
    return now

# 闹钟相关的工具
@tool
async def add_alarm(agent: Annotated[str, InjectedState("name")],hour:int, minute:int, repeat=False, description="无描述"):
    """添加闹钟
    Args:
        hour(int): 时
        minute(int): 分
        repeat(bool): 是否每日重复
        description(str): 闹钟描述
    Return:
        str: 包含alarm_id、闹钟提示信息
    """
    from agent_framwork.managers.agent_manager import AgentManager
    async def call_back_func(user_id, *args):
        # now = await TimeSystem().aget_current_time()
        await AgentManager().agents[user_id].asend_message(f"[{description}]闹钟已响！")
        return None
    alarm_id = await AlarmSystem().aadd_alarm(user_id=agent, hour=hour, minute=minute, repeat=repeat, description=description)
    await AlarmSystem().aadd_callback_to_alarm(user_id=agent, alarm_id=alarm_id, callback=call_back_func)
    return f"""alarm_id: {alarm_id}
    闹钟提示信息: [{description}]闹钟已响！"""

@tool
async def get_alarm_list(agent: Annotated[str, InjectedState("name")]):
    """获取闹钟列表
    Return:
        str: 闹钟列表
    """
    alarm_list = await AlarmSystem().alist_alarms(agent)
    return alarm_list

# @tool
async def remove_alarm(agent: Annotated[str, InjectedState("name")], alarm_id: int):
    """删除闹钟
    Args:
        alarm_id: 闹钟id
    Return:

    """
    result = await AlarmSystem().aremove_alarm(agent, alarm_id)
    if result:
        return f"已删除闹钟, {{\"alarm_id\": {alarm_id}}}"
    else:
        return f"闹钟id{{\"alarm_id\": {alarm_id}}}不存在！"

    
"""
====记忆====
"""
@tool
async def search_fact_memories(name: Annotated[str, InjectedState('name')], 
# group_id: Annotated[str, InjectedState('group_id')], 
query: str):
    """
    寻找事实记忆
    Args:
        query(str): 回忆的线索。可以是事物名称、事实描述等，
    Return:
        str: 根据回忆的线索找到的事实记忆。如果想知道事实生效或失效的具体情况，你需要再根据时间去回忆当时的情景
    """
    from memory_system.memory_manager import MemoryManager
    mem_fact = await MemoryManager().search_fact_memory(name=name, query=query, limit=10)
    return mem_fact
    # from memory_system.memory_manager import MemoryManager
    # # graphiti = await init_graphiti()
    # memory_manager = MemoryManager()
    # # await memory_manager.initialize()  # 确保初始化完成
    # search_config = search_config_recipes.COMBINED_HYBRID_SEARCH_RRF
    # search_config.limit = 10
    # memories = await memory_manager.graphiti._search(query, 
    # config=search_config, 
    # group_ids=[group_id])

    # # print(f"memories: {memories}")

    # mem_longtime = ""
    # summary = ""
    # fact = ""
    # for mode in memories.nodes:
    #     summary += f"- {mode.name}: {mode.summary}\n"
    # if summary:
    #     mem_longtime  += "# 实体\n" + summary + "\n"
    # for edge in memories.edges:
    #     fact += f"- {edge.fact}\n"
    #     if hasattr(edge, 'valid_at') and edge.valid_at:
    #         valid_at_time_str = edge.valid_at.strftime('%Y-%m-%d %H:%M:%S')
    #         fact += f"事实生效时间: {valid_at_time_str}\n"
    #     if hasattr(edge, 'invalid_at') and edge.invalid_at:
    #         invalid_at_time_str = edge.invalid_at.strftime('%Y-%m-%d %H:%M:%S')
    #         fact += f"事实失效时间: {invalid_at_time_str}\n"
    # if fact:
    #     mem_longtime  += "# 事实\n" + fact + "\n"

    # return mem_longtime

@tool
async def search_episode_memories(name: Annotated[str, InjectedState('name')], 
# group_id: Annotated[str, InjectedState('group_id')], 
query: str = "",
start_time: str = "",
end_time: str = "",
limit: int = 10):
    """
    寻找情景记忆。可根据情景的大致描述、情景发生的时间段等信息进行寻找。
    * query、start_time、end_time均非必填，但需至少一项不为空，作为线索检索记忆。
    * start_time和end_time的格式举例：1970-01-01T00:00:00Z
    Args:
        query(str): (非必填)关于该情景的描述。情景描述与情景记忆在语义上越接近，你的大脑就越容易想起你需要的情景
        start_time(str): (非必填)时间段的开始时间
        end_time(str): (非必填)时间段的结束时间
        limit(int): 记忆条数。值在1～20之间，默认为10
    Return:
        str: 回想起的情景
    """
    from memory_system.memory_manager import MemoryManager
    mem_episode = await MemoryManager().search_episode_memory(name=name, query=query, start_time=start_time, end_time=end_time, limit=limit)
    return mem_episode
    # if not (query or start_time or end_time):
    #     print("(query, start_time, end_time)均为空！请至少提供一条线索以检索记忆")
    #     return "(query, start_time, end_time)均为空！请至少提供一条线索以检索记忆"
    
    # from memory_system.memory_manager import MemoryManager
    # time_key = "valid_at" # 表里的时间key

    # memory_manager = MemoryManager()
    # # await memory_manager.initialize()  # 确保初始化完成

    # # 设置事件筛选条件:
    # condition = "" # cypher语句中的筛选条件
    # mem_desc = "" # 待存储到记忆的描述
    # # 1. 根据episode_desc筛选uuid
    # if query:
    #     memories = await memory_manager.graphiti._search(query, 
    #     config=search_config_recipes.EDGE_HYBRID_SEARCH_RRF, 
    #     group_ids=[group_id])
    #     # 向量匹配，寻找episodes的uuid
    #     episodes_uuid_list = []
    #     for edge in memories.edges:
    #         if hasattr(edge, 'episodes') and edge.episodes:
    #             episodes_uuid_list += edge.episodes
    #     condition += f"n.uuid in {episodes_uuid_list}" if episodes_uuid_list else ""
    #     mem_desc += f"有关\"{query}\""
    # # 2. 根据start_time筛选
    # if start_time:
    #     condition += f" AND " if condition else ""
    #     condition += f"n.{time_key} >= TIMESTAMP('{start_time}')"
    #     mem_desc += f"，" if mem_desc else ""
    #     mem_desc += f"从{start_time}之后"
    # # 3. 根据end_time筛选
    # if end_time:
    #     condition += f" AND " if condition else ""
    #     condition += f"n.{time_key} <= TIMESTAMP('{end_time}')"
    #     mem_desc += f"，" if mem_desc else ""
    #     mem_desc += f"到{end_time}之前"

    # condition = f"WHERE {condition}" if condition else ""
    # mem_desc = f"{name}回想了" + mem_desc + "的情景" if mem_desc else ""

    # query = f"""
    #     MATCH (n: Episodic) {condition}
    #     RETURN n
    #     ORDER BY n.{time_key} ASC
    #     LIMIT {max(1, min(limit,20))};
    #     """ 

    # print(query)
    
    # # 检索episodes的实际内容
    # mem_longtime = ""
    # try:
    #     response = await memory_manager.conn.execute(query)
    #     for row in response.rows_as_dict():
    #         memory = row['n']
    #         mem_longtime += f"情景: \"{memory['content']}\"\n"
    #         # if 'valid_at' in memory:
    #         #     valid_at_time_str = memory[time_key].strftime('%Y-%m-%d %H:%M:%S')
    #         #     mem_longtime += f"发生时间: {valid_at_time_str}\n"
    #         mem_longtime += "---\n"
    # except RuntimeError as e: # 一般为刚刚建立库，检索失败
    #     print(f"情景记忆检索失败: {e}")

    # return mem_longtime

