import importlib
import asyncio
import os

from agent_framwork.base.singleton import singleton
from agent_framwork.base.delegate import Delegate

from google.protobuf.message import Message

from network.message_pb2 import NetMessage 

import importlib
import asyncio
import os

from agent_framwork.base.singleton import singleton
from agent_framwork.base.delegate import Delegate

from google.protobuf.message import Message

from network.message_pb2 import NetMessage 

@singleton
class AgentServerNetMessage:
    def __init__(self, port=0, port_config_file=None):
        # 默认端口配置文件
        if port_config_file is None:
            port_config_file = os.path.join(os.path.dirname(__file__), 'agent_server_port.txt')
        self.port_config_file = port_config_file
        self.port = port  # 只记录端口，不绑定 socket

        self.message_types = self._load_proto_messages()
        self._message_name_mapping = {}  # 添加名称映射字典

        # 消息事件映射
        self.message_events = {}

        # 客户端连接管理：键为客户端ID，值为连接上下文
        self.clients = {}  # addr -> writer

        # 事件循环
        self.loop = asyncio.get_event_loop()

    def _load_proto_messages(self, proto_module_name=None):
        if proto_module_name is None or not importlib.util.find_spec(proto_module_name):
            module_name = "network.message_pb2"
        else:
            module_name = proto_module_name
        print(f'module_name: {module_name}')
        proto_module = importlib.import_module(module_name)
        message_types = {}
        for name in dir(proto_module):
            obj = getattr(proto_module, name)
            if isinstance(obj, type) and issubclass(obj, Message) and obj is not Message:
                message_types[name] = obj
        print(f"Loaded message types: {message_types.keys()}")
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
                    msg_len = int.from_bytes(len_bytes, 'big')

                    # 2) 读 NetMessage 完整 body
                    body = await reader.readexactly(msg_len)
                    net_msg = NetMessage()
                    net_msg.ParseFromString(body)

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
        field_name = sub_response.DESCRIPTOR.name  # PascalCase
        field_name = field_name[0].lower() + field_name[1:]  # Convert to camelCase

        # 使用 MergeFrom 方法将子消息嵌入到父消息中
        if hasattr(net_msg.Response, field_name):
            getattr(net_msg.Response, field_name).MergeFrom(sub_response)
        else:
            print(f"Field '{field_name}' not found in NetMessage.Response")
            return

        body = net_msg.SerializeToString()
        writer = context['writer']
        writer.write(len(body).to_bytes(4, 'big') + body)
        await writer.drain()

    async def broadcast_message(self, request_msg: Message):
        """
        向所有在线客户端广播一条请求消息。
        request_msg 必须是 NetMessageRequest 里的某个子消息。
        """
        # 1) 构造最外层 NetMessage
        net_msg = NetMessage()
        field_name = request_msg.DESCRIPTOR.name  # 如 "AgentSendMessageRequest"
        field_name = field_name[0].lower() + field_name[1:]  # Convert to camelCase

        # 使用 MergeFrom 方法将子消息嵌入到父消息中
        if hasattr(net_msg.Request, field_name):
            getattr(net_msg.Request, field_name).MergeFrom(request_msg)
        else:
            print(f"Field '{field_name}' not found in NetMessage.Request")
            return

        # 2) 序列化并加 4 字节长度头
        payload = net_msg.SerializeToString()
        header = len(payload).to_bytes(4, byteorder='big')
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
        field_name = response_msg.DESCRIPTOR.name  # 获取消息的字段名
        field_name = field_name[0].lower() + field_name[1:]  # 转换为 camelCase

        # 使用 MergeFrom 方法将子消息嵌入到父消息中
        if hasattr(net_msg.Response, field_name):
            getattr(net_msg.Response, field_name).MergeFrom(response_msg)
        else:
            print(f"Field '{field_name}' not found in NetMessage.Response")
            return

        payload = net_msg.SerializeToString()
        header = len(payload).to_bytes(4, byteorder='big')
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

@singleton
class AgentServerProtobuff:
    def __init__(self, port=0, port_config_file=None, proto_module_name=None):
        # 默认端口配置文件
        if port_config_file is None:
            port_config_file = os.path.join(os.path.dirname(__file__), 'agent_server_port.txt')
        self.port_config_file = port_config_file
        self.port = port  # 只记录端口，不绑定 socket

        # 加载 protobuf 消息类型
        self.message_types = self._load_proto_messages(proto_module_name)

        # 消息事件映射
        self.message_events = {}

        # 客户端连接管理：键为客户端ID，值为连接上下文
        self.clients = {}  # addr -> writer

        # 事件循环
        self.loop = asyncio.get_event_loop()

    def _load_proto_messages(self, proto_module_name=None):
        if proto_module_name is None or not importlib.util.find_spec(proto_module_name):
            module_name = "network.message_pb2"
        else:
            module_name = proto_module_name
        print(f'module_name: {module_name}')
        proto_module = importlib.import_module(module_name)
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
        self.clients[addr] = writer  # 保存连接

        try:
            while True:
                try:
                    # 1) 读 4 字节名字长度
                    try:
                        name_len_bytes = await reader.readexactly(4)
                    except asyncio.IncompleteReadError as e:
                        if e.partial == b'':
                            # 对端正常关闭连接
                            break
                        else:
                            # 只收到部分数据，协议异常
                            print(f"Protocol error reading name_len from {addr}: {e}")
                            break
                    name_len = int.from_bytes(name_len_bytes, 'big')

                    # 2) 读名字
                    try:
                        name_bytes = await reader.readexactly(name_len)
                    except asyncio.IncompleteReadError as e:
                        print(f"Incomplete name from {addr}: {e}")
                        break
                    name = name_bytes.decode()

                    # 3) 读 4 字节 body 长度
                    try:
                        body_len_bytes = await reader.readexactly(4)
                    except asyncio.IncompleteReadError as e:
                        print(f"Incomplete body_len from {addr}: {e}")
                        break
                    body_len = int.from_bytes(body_len_bytes, 'big')

                    # 4) 读 body
                    try:
                        body = await reader.readexactly(body_len)
                    except asyncio.IncompleteReadError as e:
                        print(f"Incomplete body from {addr}: {e}")
                        break

                    # ==== 至此消息已完整 ====
                    cls = self.message_types.get(name)
                    if cls is None:
                        print(f"Unknown message: {name} from {addr}")
                        continue
                    msg = cls()
                    try:
                        msg.ParseFromString(body)
                    except Exception as e:
                        print(f"Protobuf parse error from {addr}: {e}")
                        continue

                    print(f"Received message type: {name} from {addr}")
                    context = {
                        'writer': writer,
                        'reader': reader,
                        'address': addr,
                        'server': self
                    }

                    # 触发处理器
                    if name in self.message_events:
                        for handler in self.message_events[name].handlers:
                            if asyncio.iscoroutinefunction(handler):
                                await handler(msg, context)
                            else:
                                handler(msg, context)
                    else:
                        print(f"No handler registered for message type: {name}")

                # 捕获其他未预期的异常
                except Exception as e:
                    print(f"Unexpected error handling connection from {addr}: {e}")
                    break
        finally:
            writer.close()
            await writer.wait_closed()
            del self.clients[addr]  # 移除连接
            print(f"Connection from {addr} closed.")

    async def send_message(self, message: Message, context, *, flush: bool = True):
        """
        异步发送一条 Protobuf 消息。
        如果 flush=True（默认），则在写完数据后立刻 await drain()。
        """
        message_type_name = message.DESCRIPTOR.name
        name_bytes = message_type_name.encode('utf-8')
        body = message.SerializeToString()

        name_len = len(name_bytes).to_bytes(4, byteorder='big')
        body_len = len(body).to_bytes(4, byteorder='big')
        writer = context['writer']

        try:
            # 1) 检查 socket 是否仍然有效
            sock = writer.get_extra_info('socket')
            if sock is None or sock.fileno() == -1:
                print(f"Connection is closed for client: {context['address']}")
                self._remove_client(context['address'])
                return

            # 2) 写数据
            writer.write(name_len + name_bytes + body_len + body)
            if flush:
                await writer.drain()          # 关键：同步等待刷缓存
        except (ConnectionResetError, BrokenPipeError, OSError) as e:
            print(f"Failed to send message to {context['address']}: {e}")
            self._remove_client(context['address'])

    async def broadcast_message(self, message: Message):
        """向所有连接的客户端广播消息"""
        body = message.SerializeToString()
        message_type_name = message.DESCRIPTOR.name
        name_bytes = message_type_name.encode('utf-8')
        
        name_len = len(name_bytes).to_bytes(4, byteorder='big')
        body_len = len(body).to_bytes(4, byteorder='big')
        data = name_len + name_bytes + body_len + body
        
        # 复制当前客户端列表，避免在迭代过程中修改
        clients_to_remove = []
        tasks = []
        
        for addr, writer in self.clients.items():
            try:
                # 检查连接是否有效
                if writer.get_extra_info('socket').fileno() == -1:
                    clients_to_remove.append(addr)
                    continue
                    
                # 发送消息
                writer.write(data)
                tasks.append(writer.drain())
            except Exception as e:
                print(f"Failed to send message to {addr}: {e}")
                clients_to_remove.append(addr)
        
        # 移除无效连接
        for addr in clients_to_remove:
            del self.clients[addr]
            print(f"Removed disconnected client: {addr}")
        
        # 等待所有有效的客户端的drain完成
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)

    async def send_to_all_except(self, message: Message, exclude_addr):
        """向除指定客户端外的所有客户端发送消息"""
        body = message.SerializeToString()
        message_type_name = message.DESCRIPTOR.name
        name_bytes = message_type_name.encode('utf-8')
        
        name_len = len(name_bytes).to_bytes(4, byteorder='big')
        body_len = len(body).to_bytes(4, byteorder='big')
        data = name_len + name_bytes + body_len + body
        
        tasks = []
        for addr, writer in self.clients.items():
            if addr != exclude_addr:
                task = writer.write(data)
                if writer.get_extra_info('socket').fileno() != -1:
                    tasks.append(writer.drain())
        
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)

    def _parse_message_body(self, body: bytes):
        """body 里只有 Protobuf 二进制，不包含长度头"""
        for name, cls in self.message_types.items():
            try:
                msg = cls()
                msg.ParseFromString(body)
                return msg
            except Exception:
                continue
        print("Failed to parse message body")
        return None

    def shutdown(self):
        """关闭服务器"""
        self.server_socket.close()
        print("Server shut down")
