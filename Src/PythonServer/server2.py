import socket
import json
import threading

# 配置服务端监听参数
HOST = '0.0.0.0'  # 监听所有IPv4地址
PORT = 65432        # 监听端口
BUFF_SIZE = 1024     # 接收缓冲区大小

# 存储客户端连接
client_sockets = {}

# 处理客户端连接的线程
def client_thread(client_socket, client_address):
    print(f"客户端 {client_address} 已连接")
    try:
        while True:
            # 接收客户端发送的数据
            data = client_socket.recv(BUFF_SIZE)
            if not data:
                break
            # 解析JSON数据
            unity_data = json.loads(data.decode('utf-8'))
            # 处理Unity发送的信息
            if unity_data.get('type') == 'send_info':
                print(f"接收到来自Unity的信息: {unity_data}")
                # 生成控制指令并发送回Unity
                command = {
                    'type': 'control_command',
                    'target': unity_data.get('object'),
                    'command': 'move',
                    'position': {
                        'x': unity_data['position']['x'] + 0.1,
                        'y': unity_data['position']['y'],
                        'z': unity_data['position']['z']
                    }
                }
                client_socket.send(json.dumps(command).encode('utf-8'))
    except Exception as e:
        print(f"客户端 {client_address} 断开连接: {e}")
    finally:
        client_sockets.close()
        if client_address in client_sockets:
            del client_sockets[client_address]

# 创建TCP服务端
def start_server():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server_socket:
        server_socket.bind((HOST, PORT))
        server_socket.listen(5)
        print(f"服务端已启动，监听地址: {HOST}:{PORT}")

        while True:
            # 接受客户端连接
            client_socket, client_address = server_socket.accept()
            # 将客户端添加到连接字典
            client_sockets[client_address] = client_socket
            # 启动新线程处理客户端
            threading.Thread(target=client_thread, args=(client_socket, client_address)).start()

if __name__ == "__main__":
    start_server()