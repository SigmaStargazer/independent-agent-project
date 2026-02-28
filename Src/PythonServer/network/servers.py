import importlib
import asyncio
import os

from agent_framwork.base.singleton import singleton
from agent_framwork.base.delegate import Delegate

from google.protobuf.message import Message

from network.message_pb2 import NetMessage 

# 工具等待器：键为request_id，值为等待器
TOOL_WAITERS: dict[str, asyncio.Future] = {}

@singleton
class AgentServerNetMessage:
    def __init__(self, port=0, port_config_file=None):
        # 默认端口配置文件
        if port_config_file is None:
            port_config_file = os.path.join(os.path.dirname(__file__), 'agent_server_port.txt')
        self.port_config_file = port_config_file
        self.port = port  # 只记录端口，不绑定 socket

        # self.message_types = self._load_proto_messages()
        self._message_name_mapping = {}  # 添加名称映射字典

        # 消息事件映射
        self.message_events = {}

        # 客户端连接管理：键为客户端ID，值为连接上下文
        self.clients = {}  # addr -> writer

        # 事件循环
        self.loop = asyncio.get_event_loop()

    def _get_message_field_name(self, msg_cls):
        """从消息类获取对应的 Protobuf 字段名（驼峰式）"""
        name = msg_cls.DESCRIPTOR.name
        return name[0].lower() + name[1:]

    def on_message(self, msg_cls):
        """装饰器：注册消息处理函数，参数为 Protobuf 消息类"""
        field_name = self._get_message_field_name(msg_cls)
        def decorator(handler):
            if field_name not in self.message_events:
                self.message_events[field_name] = Delegate()
            self.message_events[field_name] += handler
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
            '127.0.0.1',
            self.port,
            backlog=listen_backlog
        )
        self.port = server.sockets[0].getsockname()[1]  # 获取实际绑定的端口
        print(f"Server started on port {self.port}")

        # 保存端口到配置文件
        with open(self.port_config_file, 'w', encoding='utf-8') as f:
            f.write(str(self.port))

        async with server:
            await server.serve_forever()

    async def handle_connection(self, reader, writer):
        addr = writer.get_extra_info('peername')
        print(f"New connection from {addr}")
        self.clients[addr] = writer

        try:
            while True:
                try:
                    # 1) 读 4 字节 NetMessage 长度
                    len_bytes = await reader.readexactly(4)
                    msg_len = int.from_bytes(len_bytes, 'little')

                    # 2) 读 NetMessage 完整 body
                    body = await reader.readexactly(msg_len)
                    net_msg = NetMessage()
                    net_msg.ParseFromString(body)

                    # # 测试
                    # print(f"Raw header: {len_bytes.hex()}  body-len={msg_len}")
                    # print(f"Raw body  : {body.hex()}")

                    # 3) 提取子消息
                    sub_msg, sub_name = self._extract_sub_message(net_msg)
                    if sub_msg is None:
                        print(f"No sub-message found in NetMessage from {addr}")
                        continue

                    # 4) 构造 context
                    context = {
                        'writer': writer,
                        'reader': reader,
                        'address': addr,
                        'server': self,
                    }

                    # 5) 触发子消息 handler
                    if sub_name in self.message_events:
                        for handler in self.message_events[sub_name].handlers:
                            if asyncio.iscoroutinefunction(handler):
                                await handler(sub_msg, context)
                            else:
                                handler(sub_msg, context)
                    else:
                        print(f"No handler registered for {sub_name}")

                except asyncio.IncompleteReadError:
                    break
                except Exception as e:
                    print(f"Unexpected error: {e}")
                    break
        finally:
            writer.close()
            await writer.wait_closed()
            del self.clients[addr]
            print(f"Connection from {addr} closed.")

    def _extract_sub_message(self, net_msg):
        container = None
        if net_msg.HasField('Request'):
            container = net_msg.Request
        elif net_msg.HasField('Response'):
            container = net_msg.Response
        else:
            return None, None

        for field, value in container.ListFields():
            return value, field.name          # 直接返回 snake_case
        return None, None

    async def send_message(self, sub_response: Message, context):
        net_msg = NetMessage()
        field_name = self._get_message_field_name(sub_response)

        # 使用 MergeFrom 方法将子消息嵌入到父消息中
        if hasattr(net_msg.Response, field_name):
            getattr(net_msg.Response, field_name).MergeFrom(sub_response)
        else:
            print(f"Field '{field_name}' not found in NetMessage.Response")
            return

        body = net_msg.SerializeToString()
        print(f"DEBUG: 实际发送的二进制长度: {len(body)}, 内容: {body.hex()}")
        writer = context['writer']
        writer.write(len(body).to_bytes(4, 'little') + body)
        await writer.drain()

    async def broadcast_message(self, request_msg: Message):
        """
        向所有在线客户端广播一条请求消息。
        request_msg 必须是 NetMessageRequest 里的某个子消息。
        """
        # 1) 构造最外层 NetMessage
        net_msg = NetMessage()
        field_name = self._get_message_field_name(request_msg)

        # 使用 MergeFrom 方法将子消息嵌入到父消息中
        if hasattr(net_msg.Request, field_name):
            getattr(net_msg.Request, field_name).MergeFrom(request_msg)
        else:
            print(f"Field '{field_name}' not found in NetMessage.Request")
            return

        # 2) 序列化并加 4 字节长度头
        payload = net_msg.SerializeToString()
        header = len(payload).to_bytes(4, byteorder='little')
        data = header + payload

        # 3) 广播
        to_remove = []
        tasks = []
        for addr, writer in self.clients.items():
            try:
                sock = writer.get_extra_info('socket')
                if sock is None or sock.fileno() == -1:
                    to_remove.append(addr)
                    continue
                writer.write(data)
                tasks.append(writer.drain())
            except Exception as e:
                print(f"broadcast failed to {addr}: {e}")
                to_remove.append(addr)

        # 4) 清理失效连接
        for addr in to_remove:
            self.clients.pop(addr, None)
            print(f"Removed disconnected client: {addr}")
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)

    async def send_to_all_except(self, response_msg: Message, exclude_addr):
        """
        向除 exclude_addr 外的所有客户端发送一条响应消息。
        """
        net_msg = NetMessage()
        field_name = self._get_message_field_name(response_msg)

        # 使用 MergeFrom 方法将子消息嵌入到父消息中
        if hasattr(net_msg.Response, field_name):
            getattr(net_msg.Response, field_name).MergeFrom(response_msg)
        else:
            print(f"Field '{field_name}' not found in NetMessage.Response")
            return

        payload = net_msg.SerializeToString()
        header = len(payload).to_bytes(4, byteorder='little')
        data = header + payload

        tasks = []
        for addr, writer in self.clients.items():
            if addr == exclude_addr:
                continue
            try:
                sock = writer.get_extra_info('socket')
                if sock is None or sock.fileno() == -1:
                    continue
                writer.write(data)
                tasks.append(writer.drain())
            except Exception as e:
                print(f"send_to_all_except failed to {addr}: {e}")

        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)
            print(f"DEBUG: Sending NetMessage: {net_msg}")


