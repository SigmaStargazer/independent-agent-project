import sys
import os
import asyncio

from agent_framwork.network.servers import AgentServerProtobuff

# 项目根目录
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
PORT_CONFIG_FILE = os.path.abspath(os.path.join(PROJECT_ROOT, 'Data', 'Config', 'agent_server_port.txt'))

# 获取单例
server = AgentServerProtobuff(port_config_file=PORT_CONFIG_FILE)

# ======================
# 定义消息处理函数
# ======================

@server.on_message("LoginRequest")
def handle_login_request(msg, context):
    print(f"Login request: {msg.username}")
    response = context['server'].message_types['LoginResponse']()
    response.success = True
    response.message = "Login successful"
    response.user_id = 1001
    context['server'].send_message(response, context)

@server.on_message("ChatMessage")
async def handle_chat_message(msg, context):
    print(f"Chat message from {msg.sender}: {msg.text}")
    reply = context['server'].message_types['ChatMessage']()
    reply.sender = "server"
    reply.text = f"Echo: {msg.text}"
    await asyncio.sleep(1)  # 模拟异步操作
    context['server'].send_message(reply, context)

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