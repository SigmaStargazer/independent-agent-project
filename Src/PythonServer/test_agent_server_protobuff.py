import sys
import os
import asyncio

from network.servers import AgentServerProtobuff
from agent_framwork.managers.agent_manager import AgentManager
from agent_framwork.systems.time_system import TimeSystem

# 项目根目录
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
PORT_CONFIG_FILE = os.path.abspath(os.path.join(PROJECT_ROOT, 'Data', 'Config', 'agent_server_port.txt'))

# 添加proto路径
sys.path.append(os.path.join(PROJECT_ROOT, 'Lib', 'proto'))
PROTO_MODULE_NAME = 'message_pb2'

# 获取单例
server = AgentServerProtobuff(port_config_file=PORT_CONFIG_FILE, proto_module_name=PROTO_MODULE_NAME)

# ======================
# 定义消息处理函数
# ======================

@server.on_message("LoginRequest")
async def handle_login_request(msg, context):
    print(f"Login request: {msg.username}")
    response = context['server'].message_types['LoginResponse']()
    response.success = True
    response.message = "Login successful"
    response.user_id = 1001
    await context['server'].send_message(response, context)

@server.on_message("ChatMessage")
async def handle_chat_message(msg, context):
    print(f"Chat message from {msg.sender}: {msg.text}")
    reply = context['server'].message_types['ChatMessage']()
    reply.sender = "server"
    reply.text = f"Echo: {msg.text}"
    await asyncio.sleep(1)  # 模拟异步操作
    await context['server'].send_message(reply, context)

@server.on_message("AgentCreateRequest")
async def handle_agent_create_request(msg, context):
    name = msg.name
    desc = msg.desc
    print(f"创建Agent: {name}: {desc}")
    response = context['server'].message_types['AgentCreateResponse']()
    try:
        AgentManager().acreate_agent(name=name, description=desc)
        response.success = True
        response.errormsg = ""
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"创建Agent失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message("StartSceneRequest")
async def handle_start_scene_request(msg, context):
    map_id = msg.map_id
    print(f"启动场景: {map_id}")
    response = context['server'].message_types['StartSceneResponse']()
    try:
        await AgentManager().astart_all()
        await TimeSystem().aset_speed(1440)
        await TimeSystem().astart_time(year=2016,month=1,day=1)
        response.success = True
        response.errormsg = ""
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"场景启动失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message("UserSendMessageRequest")
async def handle_user_send_msg_request(msg, context):
    agent = msg.agent
    user_message = msg.user_message
    try:
        to_agent_message = f"""用户向你发送了一则消息: {user_message}"""
        await AgentManager().agents.get(agent).asend_message(to_agent_message)
    except Exception as e:
        print(f"发送消息失败: {str(e)}")

# ======================
# 启动服务器
# ======================

async def other_tasks():
    print("Other tasks started")
    await asyncio.sleep(10)
    print("Other tasks done")

async def main():
    await asyncio.gather(
        server.astart(),
        other_tasks()
    )

if __name__ == "__main__":
    asyncio.run(main())
