import sys
import os
import asyncio

from .network.servers import AgentServerProtobuff

# 项目根目录
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))

PORT_CONFIG_FILE = os.path.abspath(os.path.join(PROJECT_ROOT, 'Data', 'Config', 'agent_server_port.txt'))

async def other_tasks():
    print("Starting other tasks...")
    await asyncio.sleep(5)
    print("Other tasks completed.")

async def main():
    await asyncio.gather(AgentServerProtobuff(port_config_file=PORT_CONFIG_FILE).astart(), other_tasks())
    # await asyncio.gather(AgentServerProtobuff().astart(), other_tasks())

# 运行主函数
if __name__ == "__main__":
    asyncio.run(main())


