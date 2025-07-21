import importlib
import socket
import asyncio
import os

from agent_framwork.base.singleton import singleton
from agent_framwork.base.delegate import Delegate

from google.protobuf.message import Message

@singleton
class AgentServerProtobuff:
    def __init__(self, port=0, port_config_file=None):
        # 默认端口配置文件
        if port_config_file is None:
            port_config_file = os.path.join(os.path.dirname(__file__), 'agent_server_port.txt')
        self.port_config_file = port_config_file
        self.port = port  # 只记录端口，不绑定 socket

        # 加载 protobuf 消息类型
        self.message_types = self._load_proto_messages()

        # 消息事件映射
        self.message_events = {}

        # 事件循环
        self.loop = asyncio.get_event_loop()

    def _load_proto_messages(self):
        module_name = ".network.message_pb2" # 这里需要改成外界传入
        proto_module = importlib.import_module(".network.message_pb2", package="agent_framwork")
        message_types = {}
        for name in dir(proto_module):
            obj = getattr(proto_module, name)
            if isinstance(obj, type) and issubclass(obj, Message) and obj is not Message:
                message_types[name] = obj
        return message_types

    def on_message(self, message_type_name):
        """装饰器注册消息处理器"""
        def decorator(handler):
            if message_type_name not in self.message_events:
                self.message_events[message_type_name] = Delegate()
            self.message_events[message_type_name] += handler
            return handler
        return decorator

    def subscribe(self, message_type_name, handler):
        """注册消息处理器"""
        if message_type_name not in self.message_events:
            self.message_events[message_type_name] = Delegate()
        self.message_events[message_type_name] += handler

    def unsubscribe(self, message_type_name, handler):
        """注销消息处理器"""
        if message_type_name in self.message_events:
            self.message_events[message_type_name] -= handler

    def unsubscribe_all(self):
        """清空所有消息处理函数"""
        for event in self.message_events.values():
            event.clear()
        self.message_events.clear()

    async def astart(self, listen_backlog=128):
        server = await asyncio.start_server(
            self.handle_connection,
            'localhost',
            self.port
        )
        self.port = server.sockets[0].getsockname()[1]  # 获取实际绑定的端口
        print(f"Server started on port {self.port}")

        # 保存端口到配置文件
        with open(self.port_config_file, 'w') as f:
            f.write(str(self.port))

        async with server:
            await server.serve_forever()

    async def handle_connection(self, reader, writer):
        addr = writer.get_extra_info('peername')
        print(f"New connection from {addr}")
        try:
            while True:
                data = await reader.read(4096)
                if not data:
                    break

                message = self._parse_message(data)
                if message is None:
                    continue

                message_type = type(message).__name__
                print(f"Received message type: {message_type}")

                context = {
                    'writer': writer,
                    'reader': reader,
                    'address': addr,
                    'server': self
                }

                if message_type in self.message_events:
                    for handler in self.message_events[message_type].handlers:
                        if asyncio.iscoroutinefunction(handler):
                            await handler(message, context)
                        else:
                            handler(message, context)
                else:
                    print(f"No handler registered for message type: {message_type}")
        except Exception as e:
            print(f"Error handling connection: {e}")
        finally:
            writer.close()
            await writer.wait_closed()
            print(f"Connection from {addr} closed.")

    def send_message(self, message: Message, context):
        """发送 protobuf 消息"""
        try:
            data = message.SerializeToString()
            writer = context['writer']
            writer.write(data)
        except Exception as e:
            print(f"Failed to send message: {e}")

    def _parse_message(self, data):
        """尝试解析所有已知消息类型"""
        for name, cls in self.message_types.items():
            try:
                msg = cls()
                msg.ParseFromString(data)
                return msg
            except Exception:
                continue
        print("Failed to parse message")
        return None

    def shutdown(self):
        """关闭服务器"""
        self.server_socket.close()
        print("Server shut down")

# @singleton
# class AgentServer:
#     def __init__(self, port=0, port_config_file=None):
#         # 默认端口配置文件
#         if port_config_file is None:
#             port_config_file = os.path.join(os.path.dirname(__file__), 'agent_server_port.txt')
#         self.port_config_file = port_config_file

#         # 创建 socket
#         self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
#         self.server_socket.bind(('localhost', port))
#         self.port = self.server_socket.getsockname()[1]
#         with open(self.port_config_file, 'w') as f:
#             f.write(str(self.port))
#         print(f"Server bound to port {self.port}")

#         self.loop = asyncio.get_event_loop()

#     async def astart(self, listen_backlog=128):
#         self.server_socket.listen(listen_backlog)
#         print('Waiting for connection...')

#         while True:
#             # 异步等待client连接
#             sock, addr = await asyncio.to_thread(self.server_socket.accept)
#             print('接受一个新连接')
#             self.loop.create_task(self.tcplink(sock, addr))
            
#     async def tcplink(self, sock: socket.socket, addr):
#         """
#         连接建立后，服务器执行以下操作：
#         1）发一条欢迎消息；
#         2）等待客户端数据，并加上Hello再发送给客户端；
#         3）如果客户端发送了exit字符串，就直接关闭连接。
#         """
#         print('Accept new connection from %s:%s...' % addr)
#         sock.send(f'Welcome!'.encode('utf-8'))
#         while True:
#             data = await self.loop.run_in_executor(None, sock.recv, 1024)
#             if not data or data.decode('utf-8') == 'exit':
#                 break
#             await asyncio.sleep(1)
#             sock.send(('Hello, %s!' % data.decode('utf-8')).encode('utf-8'))
#         sock.close()
#         print('Connection from %s:%s closed.' % addr)

