from langchain_core.tools import tool
from typing_extensions import Annotated
from langgraph.prebuilt import InjectedState

from agent_framwork.systems.alarm_system import AlarmSystem
from agent_framwork.systems.time_system import TimeSystem

# tool中调用langgraph中state内参数的方式，请参考下面的 InjectedState 中的内容：
# https://langchain-ai.github.io/langgraph/reference/agents/#langgraph.prebuilt.tool_node.ToolNode.inject_tool_args

@tool
async def communicate_to_agent(agent: Annotated[str, InjectedState("name")],recipient: str, message: str) -> str:
    """向目标agent发送一则消息
    Args:
        recipient(str): 信息接收人名字
        message(str): 你想要发送的消息
    """
    from agent_framwork.managers.agent_manager import AgentManager
    if recipient in AgentManager().agents:
        await AgentManager().agents[recipient].asend_message(f"Agent[{agent}]向你发送了一则消息: {message}")
        return f"[{agent}]向Agent[{recipient}]发送了一则消息: {message}"
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
    try:
        request = AgentServerNetMessage().message_types['AgentSendMessageRequest']()
        request.agent = agent
        request.ai_message = message
        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}]向用户发送消息成功: {message}")
        return f"你向用户发送了一则消息: {message}"
    except Exception as e:
        print(f"[{agent}]向用户发送消息失败: {message}, {e}")
        return f"你向用户发送消息失败: {e}"

@tool
async def get_agent_list() -> list:
    """获取所有agent的清单"""
    from agent_framwork.managers.agent_manager import AgentManager
    return list(AgentManager().agents.keys())

# 时间相关的工具
@tool
async def get_cur_time() -> str:
    """获取当前时间"""
    now = await TimeSystem().aget_current_time_str()
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
        # now = await TimeSystem().aget_current_time_str()
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

