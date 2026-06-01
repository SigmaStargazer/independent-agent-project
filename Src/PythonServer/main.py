import sys
import os
import asyncio

from network.servers import AgentServerNetMessage, TOOL_WAITERS
from network import message_pb2

from agent_framwork.managers.agent_manager import AgentManager
from agent_framwork.systems.time_system import TimeSystem
from memory_system.memory_manager import MemoryManager

# 项目根目录
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
PORT_CONFIG_FILE = os.path.abspath(os.path.join(PROJECT_ROOT, 'Data', 'Config', 'agent_server_port.txt'))

# 添加proto路径
sys.path.append(os.path.join(PROJECT_ROOT, 'Lib', 'proto'))

# 获取单例
server = AgentServerNetMessage(port_config_file=PORT_CONFIG_FILE)

# ======================
# 定义消息处理函数
# ======================

@server.on_message(message_pb2.AgentCreateRequest)
async def handle_agent_create_request(msg, context):
    name = msg.name
    desc = msg.desc

    print(f"创建Agent: {name}: {desc}")
    # await MemoryManager().initialize()
    # await TimeSystem().aset_time(year=2016,month=1,day=1)
    cur_time = await TimeSystem().aget_current_time()
    
    response = message_pb2.AgentCreateResponse()
    try:
        result = await AgentManager().acreate_agent(
            name=name, 
            summary=desc,
            create_time=cur_time
            )
        response.success = True
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"创建Agent失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message(message_pb2.AgentLoadRequest)
async def handle_agent_load_request(msg, context):
    print("加载Agent")
    # await MemoryManager().initialize()
    response = message_pb2.AgentLoadResponse()
    try:
        agent_names = await AgentManager().aload_agent_all()
        response.agent_names.extend(agent_names) # agent_names 的list
        response.success = True
        # response.errormsg = ""
        print(f"加载Agent成功: {agent_names}")
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"加载Agent失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message(message_pb2.SceneStartRequest)
async def handle_scene_start_request(msg, context):
    map_id = msg.map_id
    print(f"启动场景: {map_id}")
    response = message_pb2.SceneStartResponse()
    try:
        # await MemoryManager().initialize()
        
        await TimeSystem().aset_speed(1440)
        await TimeSystem().aset_time(year=2016,month=1,day=1)
        await TimeSystem().astart_time()
        
        await AgentManager().astart_all()
        response.success = True
        # response.errormsg = ""
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"场景启动失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message(message_pb2.SceneStopRequest)
async def handle_scene_stop_request(msg, context):
    print("停止场景")
    response = message_pb2.SceneStopResponse()
    try:
        print("停止时间")
        await TimeSystem().apause_time()
        print("停止 Agent...")
        await AgentManager().aremove_all()
        response.success = True
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"场景停止失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message(message_pb2.AgentInterruptRequest)
async def handle_agent_interrupt_request(msg, context):
    print("中断Agent")
    reason = msg.reason
    response = message_pb2.AgentInterruptResponse()
    try:
        await AgentManager().ainterrupt_all(reason=reason)
        response.success = True
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"中断Agent失败: {str(e)}")
        await context['server'].send_message(response, context)    

@server.on_message(message_pb2.UserSendMessageRequest)
async def handle_user_send_msg_request(msg, context):
    agent = msg.agent
    user_message = msg.user_message
    force_interrupt = msg.force_interrupt
    try:
        # to_agent_message = f"""用户向你发送了一则消息: {user_message}"""
        to_agent_message = f"""{user_message}"""
        await AgentManager().asend_message(
            name=agent, 
            message=to_agent_message,
            force_interrupt=force_interrupt
            )
    except Exception as e:
        print(f"接收消息失败: {str(e)}")

@server.on_message(message_pb2.UserSendFeedbackRequest)
async def handle_user_send_feedback_request(msg, context):
    agent = msg.agent
    user_feedback = msg.user_feedback
    force_interrupt = msg.force_interrupt
    try:
        await AgentManager().asend_feedback(
            name=agent, 
            feedback=user_feedback, 
            force_interrupt=force_interrupt
            )
    except Exception as e:
        print(f"接收反馈失败: {str(e)}")

@server.on_message(message_pb2.UserSendMessageAllRequest)
async def handle_user_send_msg_all_request(msg, context):
    user_message = msg.user_message
    force_interrupt = msg.force_interrupt
    try:
        # to_agent_message = f"""用户向你发送了一则消息: {user_message}"""
        to_agent_message = f"""{user_message}"""
        await AgentManager().asend_message_all(
            message=to_agent_message,
            force_interrupt=force_interrupt
            )
    except Exception as e:
        print(f"接收消息失败: {str(e)}")

@server.on_message(message_pb2.SendToolResultMessageRequest)
async def handle_tool_result_request(msg, context):
    agent = msg.agent
    tool_name = msg.tool_name
    request_id = msg.request_id
    result = msg.result

    fut = TOOL_WAITERS.get(request_id)

    if fut is None:
        print(f"[TOOL_WAITERS] 未找到等待中的 request_id: {request_id} (tool={tool_name})")
        return

    if fut.done():
        print(f"[TOOL_WAITERS] request_id 已完成: {request_id}")
        return

    # 唤醒 observe_cmd / 其他工具 await
    fut.set_result(result)

    print(f"[TOOL_WAITERS] 工具回调完成: agent={agent}, tool={tool_name}, request_id={request_id}")

# ======================
# 记忆存档
# ======================

@server.on_message(message_pb2.MemoryBackupRequest)
async def handle_memory_backup_request(msg, context):
    slot_id = msg.slot_id
    response = message_pb2.MemoryBackupResponse()
    try:
        result = await MemoryManager().backup_memory(slot_id=slot_id)
        response.success = True
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"备份失败: {str(e)}")
    await context['server'].send_message(response,context)

@server.on_message(message_pb2.MemoryRestoreRequest)
async def handle_memory_restore_request(msg, context):
    slot_id = msg.slot_id
    response = message_pb2.MemoryRestoreResponse()
    try:
        print("读档...")
        result = await MemoryManager().restore_memory(slot_id=slot_id)
        response.success = True
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"读档失败: {str(e)}")
    await context['server'].send_message(response,context)

@server.on_message(message_pb2.MemoryDeleteCurrentRequest)
async def handle_memory_delete_request(msg, context):
    response = message_pb2.MemoryDeleteCurrentResponse()
    try:
        result = await MemoryManager().delete_current_memory()
        response.success = True
        print("删除当前记忆成功")
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"删除当前记忆失败: {str(e)}")
    await context['server'].send_message(response,context)

# ======================
# 启动服务器
# ======================
async def other_tasks():
    print("Other tasks started")
    await asyncio.sleep(10)
    print("Other tasks done")

async def main():
    # 1. 在这里全局执行一次初始化
    print("正在初始化 MemoryManager...")
    await MemoryManager().initialize()
    print("MemoryManager 初始化完成。")

    # 也可以在这里初始化 TimeSystem，如果需要的话
    await TimeSystem().aset_time(year=2016, month=1, day=1)

    print("正在启动服务器...")
    # 2. 系统初始化完成后，再启动网络服务和其他任务
    await asyncio.gather(
        server.astart(),
        other_tasks()
    )

if __name__ == "__main__":
    asyncio.run(main())