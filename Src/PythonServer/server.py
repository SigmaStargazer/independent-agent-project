import socket
import json
import struct

class UnitySocketServer:
    def __init__(self):
        self.server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server.bind(('localhost', 8000))
        self.server.listen(1)
        self.client, self.address = self.server.accept()

    def send_command(self, command):
        # 将JSON指令转换为字节流
        json_data = json.dumps(command).encode('utf-8')
        self.client.sendall(json_data)

    def close(self):
        self.client.close()
        self.server.close()

# 示例：发送移动指令
if __name__ == "__main__":
    server = UnitySocketServer()
    command = {
        "type": "control",
        "action": "move",
        "target": "player",
        "position": {"x": 1.0, "y": 0.0, "z": 2.0}
    }
    server.send_command(command)
    server.close()