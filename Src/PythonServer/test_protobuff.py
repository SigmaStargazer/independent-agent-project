import sys
import os

import socket
import struct

# 项目根目录
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))

# 添加proto路径
sys.path.append(os.path.join(PROJECT_ROOT, 'Lib', 'proto'))

import message_pb2

def main():
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_socket.bind(('localhost', 5005))
    server_socket.listen(1)
    print("Server listening on port 5005...")

    conn, addr = server_socket.accept()
    print(f"Connected by {addr}")

    # 接收并解析 Protobuf 消息
    data = conn.recv(4)  # 接收消息长度
    msg_len = struct.unpack('!I', data)[0]
    data = conn.recv(msg_len)  # 接收实际数据

    person = message_pb2.Person()
    person.ParseFromString(data)
    print(f"Received: {person.name}, {person.age}, {person.message}")

    # 构造并发送响应消息
    response = message_pb2.Person()
    response.name = "Server"
    response.age = 2025
    response.message = "Hello from Python server!"
    data = response.SerializeToString()
    conn.sendall(struct.pack('!I', len(data)) + data)

    conn.close()
    server_socket.close()

if __name__ == "__main__":
    main()