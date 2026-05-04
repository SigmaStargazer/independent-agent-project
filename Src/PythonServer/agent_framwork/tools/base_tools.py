import asyncio
import uuid
from typing import Annotated, List
from pydantic import Field
from agent_framwork.tools.action_sequence_model.model.action_sequence import ActionStep

from langchain_core.runnables import RunnableConfig
from langchain_core.tools import tool
from typing_extensions import Annotated
from langgraph.prebuilt import InjectedState

from graphiti_core.search import search_config_recipes

from agent_framwork.tools.action_sequence_model.model.action import (
    WaitAction as WaitActionModel,
    MoveAction as MoveActionModel,
    InteractAction as InteractActionModel,
    SelectAction as SelectActionModel,
    InputAction as InputActionModel,
)
# from agent_framwork.tools.action_sequence_model.model.action_sequence import ActionSequence

from agent_framwork.systems.alarm_system import AlarmSystem
from agent_framwork.systems.time_system import TimeSystem

from network.servers import AgentServerNetMessage, TOOL_WAITERS
from network import message_pb2

# tool中调用langgraph中state内参数的方式，请参考下面的 InjectedState 中的内容：
# https://langchain-ai.github.io/langgraph/reference/agents/#langgraph.prebuilt.tool_node.ToolNode.inject_tool_args

TOOL_TIMEOUT = 30#RPC调用超时时间

# region Agent交流工具
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
# endregion

# region Agent动作工具

def build_pb_action_step(step) -> message_pb2.ActionStep:
    pb_step = message_pb2.ActionStep()

    # ===== condition =====
    if hasattr(step, "condition"):
        pb_step.condition = step.condition
    else:
        pb_step.condition = ""

    # ===== action =====
    if isinstance(step, WaitActionModel):
        pb_step.wait.CopyFrom(message_pb2.WaitAction())

    elif isinstance(step, MoveActionModel):
        pb_step.move.direction = (
            message_pb2.MoveAction.RIGHT
            if step.direction == "right"
            else message_pb2.MoveAction.LEFT
        )
        if step.allowed_contact_obj_ids:
            pb_step.move.allowed_contact_obj_ids.extend(
                step.allowed_contact_obj_ids
            )

    elif isinstance(step, InteractActionModel):
        pb_step.interact.CopyFrom(message_pb2.InteractAction())

    elif isinstance(step, SelectActionModel):
        pb_step.select.selection = step.selection

    elif isinstance(step, InputActionModel):
        pb_step.input.input_text = step.input_text

    else:
        raise TypeError(f"Unsupported ActionStep type: {type(step)}")

    return pb_step

@tool
async def plan_action_sequence_cmd(
    agent: Annotated[str, InjectedState("name")], 
    action_sequence: Annotated[
        List[ActionStep],
        Field(min_length=1, description="按顺序执行的动作序列。每个动作将在满足condition后结束。")
    ]
    ) -> str:
    """规划一串连续的动作。
    后续经过对动作序列进行校验、确认执行后，才会开始执行动作序列
    举例：
    1) (假如信号灯的序号为1)在信号灯变绿后，立刻向右走2米。
    action_sequence = [
        {
            "action": "wait",
            "condition": "objects[1].State == 'GreenLight'"
        },
        {
            "action": "move",
            "direction": "right",
            "condition": "displacement >= 2",
            "allowed_contact_obj_ids": []
        }
        ]
    2) (假如按钮的序号为2)走到按钮旁边，按下按钮。
    action_sequence = [
        {
            "action": "move",
            "direction": "right",
            "condition": "canInteract == true && nearestInteractableIndex == 2",
        },
        {
            "action": "interact",
        }
    ]
    3) 
    a: (假设商人的序号为3；商人的方位为右侧2米处；商人朝向为左侧)走到商人身后，进行剽窃
    解析：由于商人方位在右侧，商人朝向为左侧，因此商人是面对你自己的。要走到他身后，需要向右移动超过2米，才能绕到商人身后。
    action_sequence = [
        {
            "action": "move",
            "direction": "right",
            "condition": "displacement >= 2 && canInteract == true && nearestInteractableIndex == 3",
        },
        {
            "action": "interact",
        }
    ]
    b: (假设商人的序号为3；商人的方位为右侧2米处；商人朝向为右侧)走到商人身后，进行剽窃
    解析：由于商人方位在右侧，商人朝向为右侧，因此商人背对你自己的。要走到他身后，只需向他移动直至可以交互即可。
    action_sequence = [
        {
            "action": "move",
            "direction": "right",
            "condition": "canInteract == true && nearestInteractableIndex == 3",
        },
        {
            "action": "interact",
        }
    ]

    Args:
        action_sequence(List[ActionStep]): 动作序列
    Return:
        str: 规划动作序列结果
    """
    # seq = ActionSequence(action_sequence=action_sequence)

    request_id = str(uuid.uuid4())
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    # 注册等待池
    TOOL_WAITERS[request_id] = fut

    try:
        request = message_pb2.AgentPlanActionSequenceRequest()
        request.agent = agent
        request.request_id = request_id
        for step in action_sequence:
            request.action_sequence.append(
                build_pb_action_step(step)
            )

        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] plan_action_sequence_cmd 发起请求 {request_id}")
        # 等待客户端回调（阻塞 await）
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)

        # 闭环返回模型
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]规划动作序列超时"
    except Exception as e:
        return f"[{agent}]规划动作序列异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)
# 确认开始执行动作序列
@tool
async def start_action_sequence_cmd(agent: Annotated[str, InjectedState("name")]) -> str:
    """
    确认开始执行动作序列。
    重要行为规则：
    - 动作序列是长时任务（long-running task）。
    - 执行结果不会在本轮对话中返回。
    - 执行完成后，系统会主动发送通知消息。

    当你启动动作序列后：
    - 不要反复调用 observe 等待完成。
    - 应结束本轮对话，等待执行结果通知。

    Return:
        str: 动作序列执行结果
    """
    request_id = str(uuid.uuid4())
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    # 注册等待池
    TOOL_WAITERS[request_id] = fut

    try:
        request = message_pb2.AgentStartActionSequenceRequest()
        request.agent = agent
        request.request_id = request_id

        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] start_action_sequence_cmd 发起请求 {request_id}")
        # 等待客户端回调（阻塞 await）
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)

        # 闭环返回模型
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]开始执行动作序列超时"
    except Exception as e:
        return f"[{agent}]开始执行动作序列异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)

# 继续执行动作序列
@tool
async def continue_action_sequence_cmd(agent: Annotated[str, InjectedState("name")]) -> str:
    """继续执行动作序列
    重要行为规则：
    - 动作序列是长时任务（long-running task）。
    - 执行结果不会在本轮对话中返回。
    - 执行完成后，系统会主动发送通知消息。

    当你启动动作序列后：
    - 不要反复调用 observe 等待完成。
    - 应结束本轮对话，等待执行结果通知。

    Return:
        str: 动作序列继续执行结果
    """
    request_id = str(uuid.uuid4())
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    # 注册等待池
    TOOL_WAITERS[request_id] = fut

    try:
        request = message_pb2.AgentContinueActionSequenceRequest()
        request.agent = agent
        request.request_id = request_id
        
        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] continue_action_sequence_cmd 发起请求 {request_id}")
        # 等待客户端回调（阻塞 await）
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)

        # 闭环返回模型
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]继续执行动作序列超时"
    except Exception as e:
        return f"[{agent}]继续执行动作序列异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)

# 停止动作序列
@tool
async def stop_action_sequence_cmd(agent: Annotated[str, InjectedState("name")]) -> str:
    """停止动作序列
    Return:
        str: 动作序列停止结果
    """
    request_id = str(uuid.uuid4())
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    # 注册等待池
    TOOL_WAITERS[request_id] = fut
    
    try:
        request = message_pb2.AgentStopActionSequenceRequest()
        request.agent = agent
        request.request_id = request_id
        
        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] stop_action_sequence_cmd 发起请求 {request_id}")
        # 等待客户端回调（阻塞 await）
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)

        # 闭环返回模型
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]停止动作序列超时"
    except Exception as e:
        return f"[{agent}]停止动作序列异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)

async def drain_feedback_queue(feedback_queue: asyncio.Queue) -> str:
    """
    取出 feedback_queue 内全部内容，并按时间排序
    """
    items = []
    while not feedback_queue.empty():
        msg = feedback_queue.get_nowait()
        items.append(msg)
        feedback_queue.task_done()
    if not items:
        return ""
    items.sort()
    return "\n".join(item.content for item in items)

@tool
async def observe_cmd(
    agent: Annotated[str, InjectedState("name")],
    config: RunnableConfig
    # feedback_queue: Annotated[asyncio.Queue, InjectedState("feedback_queue")]
    ) -> str:
    """观察周围环境。
    用途：
    - 获取当前环境信息
    - 获取系统发送的反馈消息（例如动作完成通知）

    重要行为规则：

    1) observe 不是用于等待长时任务完成。
    2) 当你刚刚启动移动或动作序列时：
    - 默认应结束本轮对话
    - 等待系统通知
    - 而不是持续调用 observe

    3) 避免频繁调用 observe：
    - 如果环境没有明显变化
    - 或没有收到新的反馈消息
    - 不要连续多次调用 observe

    建议策略：

    - 在不确定环境状态时调用 observe
    - 在收到任务完成通知后再调用 observe
    - 不要将 observe 作为“等待机制”

    Return:
        str: 环境观察结果。
    """
    feedback_queue = config["configurable"]["feedback_queue"] 

    request_id = str(uuid.uuid4())
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    # 注册等待池
    TOOL_WAITERS[request_id] = fut

    try:
        request = message_pb2.AgentObserveRequest()
        request.agent = agent
        request.request_id = request_id

        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] observe_cmd 发起请求 {request_id}")
        # 等待客户端回调（阻塞 await）
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)
        # 获取目前获取的工具反馈结果
        feedback_text = await drain_feedback_queue(feedback_queue)
        if feedback_text:
            result += "\n" + feedback_text
        # 闭环返回模型
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]观察超时"
    except Exception as e:
        return f"[{agent}]观察异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)

@tool
async def move_cmd(agent: Annotated[str, InjectedState("name")], direction: str, distance: float) -> str:
    """向指定方向移动指定距离
    重要行为规则：
    - 移动是异步执行的。
    - 移动结果不会在本轮对话中返回。
    - 移动完成后，系统会主动发送通知消息。

    当你执行移动后：
    - 不要持续调用 observe 等待移动完成。
    - 应结束本轮对话，等待移动完成通知。
    
    Args:
        direction(str): 方向，填left或者right
        distance(float): 距离
    Return:
        str: 移动是否开始。注意：移动结果将通过新的消息另行通知。
    """
    if direction not in ["left", "right"]:
        return "方向错误，请填left或者right"
    from network.servers import AgentServerNetMessage
    from network import message_pb2

    try:
        request = message_pb2.AgentMoveRequest()
        request.agent = agent
        request.is_right = direction == "right"
        request.distance = distance
        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}]尝试向{direction}移动了{distance}距离。待移动完成后，你将收到移动完成的消息。")
        return f"[{agent}]尝试向{direction}移动了{distance}距离。待移动完成后，你将收到移动完成的消息。"
    except Exception as e:
        return f"移动失败: {e}"

@tool
async def interact_cmd(agent: Annotated[str, InjectedState("name")]) -> str:
    """与身旁的标注为\"可选择交互\"的对象进行交互
    Return:
        str: 交互结果
    """
    request_id = str(uuid.uuid4())
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    # 注册等待池
    TOOL_WAITERS[request_id] = fut

    try:
        request = message_pb2.AgentInteractRequest()
        request.agent = agent
        request.request_id = request_id

        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] interact_cmd 发起请求 {request_id}")
        # 等待客户端回调（阻塞 await）
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)

        # 闭环返回模型
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]交互超时"
    except Exception as e:
        return f"[{agent}]交互异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)

@tool
async def select_cmd(agent: Annotated[str, InjectedState("name")], selection: int) -> str:
    """
    与\"可选择交互\"的对象进行交互后，若交互结果提供了选项，则使用此工具选择选项
    Args:
        selection(int): 选项编号
    Return:
        str: 选择结果
    """
    request_id = str(uuid.uuid4())
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    # 注册等待池
    TOOL_WAITERS[request_id] = fut

    try:
        request = message_pb2.AgentSelectRequest()
        request.agent = agent
        request.request_id = request_id
        request.selection = selection

        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] select_cmd 发起请求 {request_id}")
        # 等待客户端回调（阻塞 await）
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)

        # 闭环返回模型
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]选择超时"
    except Exception as e:
        return f"[{agent}]选择异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)

@tool
async def input_cmd(agent: Annotated[str, InjectedState("name")], input_text: str) -> str:
    """
    与\"可选择交互\"的对象进行交互后，若交互结果提供了输入框，则使用此工具输入文本
    Args:
        input_text(str): 输入文本
    Return:
        str: 输入结果
    """
    request_id = str(uuid.uuid4())
    loop = asyncio.get_running_loop()
    fut = loop.create_future()

    # 注册等待池
    TOOL_WAITERS[request_id] = fut
    
    try:
        request = message_pb2.AgentInputRequest()
        request.agent = agent
        request.request_id = request_id
        request.input_text = input_text

        await AgentServerNetMessage().broadcast_message(request)
        print(f"[{agent}] input_cmd 发起请求 {request_id}")
        # 等待客户端回调（阻塞 await）
        result = await asyncio.wait_for(fut, timeout=TOOL_TIMEOUT)

        # 闭环返回模型
        return f"{result}"
    except asyncio.TimeoutError:
        return f"[{agent}]输入超时"
    except Exception as e:
        return f"[{agent}]输入异常: {e}"
    finally:
        TOOL_WAITERS.pop(request_id, None)
# endregion

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
    
# region 记忆工具
@tool
async def search_fact_memories(name: Annotated[str, InjectedState('name')], 
# group_id: Annotated[str, InjectedState('group_id')], 
query: str):
    """
    回忆自己脑海中有关事实的记忆
    Args:
        query(str): 回忆的线索。可以是事物名称、事实描述等，
    Return:
        str: 根据回忆的线索找到的事实记忆。如果想知道事实生效或失效的具体情况，你需要再根据时间去回忆当时的情景
    """
    from memory_system.memory_manager import MemoryManager
    mem_fact = await MemoryManager().search_fact_memory(name=name, query=query, limit=10)
    return mem_fact

@tool
async def search_episode_memories(name: Annotated[str, InjectedState('name')], 
# group_id: Annotated[str, InjectedState('group_id')], 
query: str = "",
start_time: str = "",
end_time: str = "",
limit: int = 10):
    """
    回忆自己脑海中有关某段情景的记忆。可根据情景的大致描述、情景发生的时间段等信息进行寻找。
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

# region 闹钟工具
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
# endregion