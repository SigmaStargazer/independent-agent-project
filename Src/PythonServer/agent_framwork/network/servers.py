import socket
import threading
import time
import asyncio
import os

from agent_framwork.base.singleton import singleton

print(os.path.dirname(__file__))
# 默认存在network里的agent_server_port.txt
DEFAULT_PORT_CONFIG_FILE = os.path.abspath(os.path.join(os.path.dirname(__file__), 'agent_server_port.txt'))

@singleton
class AgentServerProtobuff:
    def __init__(self, port=0, port_config_file=DEFAULT_PORT_CONFIG_FILE):
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM) # 创建一个Socket对象
        self.server_socket.bind(('localhost', port)) # 绑定监听端口为0，表示让操作系统自动分配一个空闲端口
        self.port = self.server_socket.getsockname()[1] # 获取实际分配的端口号
        print(f'Server bound to port {self.port}')
        with open(port_config_file, 'w') as f:
            f.write(str(self.port))

        self.loop = asyncio.get_event_loop()

    async def astart(self, listen_backlog=128):
        self.server_socket.listen(listen_backlog)
        print('Waiting for connection...')

        while True:
            # 异步等待client连接
            sock, addr = await asyncio.to_thread(self.server_socket.accept)
            print('接受一个新连接')
            self.loop.create_task(self.tcplink(sock, addr))
            
    async def tcplink(self, sock: socket.socket, addr):
        """
        连接建立后，服务器执行以下操作：
        1）发一条欢迎消息；
        2）等待客户端数据，并加上Hello再发送给客户端；
        3）如果客户端发送了exit字符串，就直接关闭连接。
        """
        print('Accept new connection from %s:%s...' % addr)
        sock.send(f'Welcome!'.encode('utf-8'))
        while True:
            data = await self.loop.run_in_executor(None, sock.recv, 1024)
            if not data or data.decode('utf-8') == 'exit':
                break
            await asyncio.sleep(1)
            sock.send(('Hello, %s!' % data.decode('utf-8')).encode('utf-8'))
        sock.close()
        print('Connection from %s:%s closed.' % addr)

@singleton
class AgentServer:
    def __init__(self, port=0):
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM) # 创建一个Socket对象
        self.server_socket.bind(('localhost', port)) # 绑定监听端口为0，表示让操作系统自动分配一个空闲端口
        self.port = self.server_socket.getsockname()[1] # 获取实际分配的端口号
        print(f'Server bound to port {self.port}')
        # 将端口号写入文件， 用于和client共享
        print(DEFAULT_PORT_CONFIG_FILE)
        # with open('../server_port.txt', 'w') as f:
        with open(DEFAULT_PORT_CONFIG_FILE, 'w') as f:
            f.write(str(self.port))

        self.loop = asyncio.get_event_loop()

    async def astart(self, listen_backlog=128):
        self.server_socket.listen(listen_backlog)
        print('Waiting for connection...')

        while True:
            # 异步等待client连接
            sock, addr = await asyncio.to_thread(self.server_socket.accept)
            print('接受一个新连接')
            self.loop.create_task(self.tcplink(sock, addr))
            
    async def tcplink(self, sock: socket.socket, addr):
        """
        连接建立后，服务器执行以下操作：
        1）发一条欢迎消息；
        2）等待客户端数据，并加上Hello再发送给客户端；
        3）如果客户端发送了exit字符串，就直接关闭连接。
        """
        print('Accept new connection from %s:%s...' % addr)
        sock.send(f'Welcome!'.encode('utf-8'))
        while True:
            data = await self.loop.run_in_executor(None, sock.recv, 1024)
            if not data or data.decode('utf-8') == 'exit':
                break
            await asyncio.sleep(1)
            sock.send(('Hello, %s!' % data.decode('utf-8')).encode('utf-8'))
        sock.close()
        print('Connection from %s:%s closed.' % addr)

