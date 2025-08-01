using Google.Protobuf;
using IndependentAgentProject.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CSharpClient
{
    class TestProtoBuff_3
    {
        static NetworkStream stream;
        static bool isConnected = true;
        static TcpClient currentClient;

        // 修改1: 正确获取所有消息类型
        static readonly Dictionary<string, Type> MessageTypes =
            typeof(IndependentAgentProject.Protobuf.AgentCreateRequest).Assembly
                .GetTypes()
                .Where(t => t.Namespace == "IndependentAgentProject.Protobuf" &&
                           typeof(IMessage).IsAssignableFrom(t) &&
                           !t.IsAbstract)
                .ToDictionary(t => t.Name);

        static async Task ConnectAndRun()
        {
            int port;
            try
            {
                // 获取当前目录的上一级目录路径
                string projectRoot = Directory.GetParent(AppContext.BaseDirectory)
                                  ?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName;

                if (projectRoot == null)
                {
                    throw new DirectoryNotFoundException("Unable to find the Src directory.");
                }

                string filePath = Path.Combine(projectRoot, "Data", "Config", "agent_server_port.txt");
                Console.WriteLine($"filePath: {filePath}");

                // 从文件中读取服务端端口号
                string portStr = File.ReadAllText(filePath).Trim();
                port = int.Parse(portStr);
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("Server port file not found. Please ensure the server is running.");
                return;
            }
            catch (FormatException)
            {
                Console.WriteLine("Invalid port number in server port file.");
                return;
            }
            catch (DirectoryNotFoundException e)
            {
                Console.WriteLine(e.Message);
                return;
            }

            // 清理旧连接
            if (currentClient != null)
            {
                currentClient.Close();
                currentClient = null;
            }

            currentClient = new TcpClient();
            await currentClient.ConnectAsync("localhost", port);
            stream = currentClient.GetStream();

            Console.WriteLine("Connected to server.");

            // 启动一个任务来持续接收服务端消息
            var receiveTask = Task.Run(() => ReceiveMessages());

            // 发送一些初始消息
            await SendInitialMessages();

            // 等待接收任务完成（当连接断开时）
            await receiveTask;
        }

        static async Task Main(string[] args)
        {
            await ConnectAndRun();
        }

        static async Task SendInitialMessages()
        {
            // 创建Agent
            var agentCreateRequest = new AgentCreateRequest
            {
                Name = "小明",
                Desc = ""
            };
            await SendAsync(agentCreateRequest, stream);

            // 开始场景
            var startSceneRequest = new StartSceneRequest
            {
                MapId = 1
            };
            await SendAsync(startSceneRequest, stream);

            // 发送聊天信息
            var agentSendMessageRequest = new AgentSendMessageRequest
            {
                Agent = "小明",
                UserMessage = "闹个每天8点的起床铃，9点的上班铃声"
            };
            await SendAsync(agentSendMessageRequest, stream);
        }

        static async Task ReceiveMessages()
        {
            try
            {
                while (isConnected)
                {
                    // 1. 4 字节 name 长度（大端）
                    byte[] nameLenBuf = new byte[4];
                    await ReadExactlyAsync(stream, nameLenBuf, 0, 4);
                    int nameLen = BitConverter.ToInt32(nameLenBuf, 0);
                    if (BitConverter.IsLittleEndian)
                        nameLen = System.Net.IPAddress.NetworkToHostOrder(nameLen);

                    // 2. 读 name
                    byte[] nameBytes = new byte[nameLen];
                    await ReadExactlyAsync(stream, nameBytes, 0, nameLen);
                    string name = Encoding.UTF8.GetString(nameBytes);

                    // 3. 4 字节 body 长度（大端）
                    byte[] bodyLenBuf = new byte[4];
                    await ReadExactlyAsync(stream, bodyLenBuf, 0, 4);
                    int bodyLen = BitConverter.ToInt32(bodyLenBuf, 0);
                    if (BitConverter.IsLittleEndian)
                        bodyLen = System.Net.IPAddress.NetworkToHostOrder(bodyLen);

                    // 4. 读 body
                    byte[] body = new byte[bodyLen];
                    await ReadExactlyAsync(stream, body, 0, bodyLen);

                    // 使用预加载的消息类型字典
                    if (MessageTypes.TryGetValue(name, out Type messageType))
                    {
                        var msg = (IMessage)Activator.CreateInstance(messageType);
                        msg.MergeFrom(body);
                        Console.WriteLine($"Received message: {name}");

                        // 处理特定类型的消息
                        //if (msg is UpdateDataRequest updateData)
                        //{
                        //    Console.WriteLine($"Received data update: {updateData.Data}");
                        //}
                        // 可以添加更多消息类型的处理...
                        if (msg is AgentCreateResponse response)
                        {
                            Console.WriteLine($"Received AgentCreateResponse: {response}");
                            // 在这里处理响应数据
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Unknown message type: {name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving message: {ex.Message}");
                isConnected = false;

                // 尝试重连
                await Task.Delay(5000);
                if (!isConnected)
                {
                    Console.WriteLine("Attempting to reconnect...");
                    isConnected = true; // 重置连接状态
                    await ConnectAndRun(); // 使用新的连接方法
                }
            }
        }

        private static string GetMessageName<T>() => typeof(T).Name;

        private static async Task SendAsync<T>(T message, NetworkStream stream) where T : IMessage
        {
            string name = GetMessageName<T>();
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            byte[] body = message.ToByteArray();

            // 结构：4字节名字长度 + 名字 + 4字节body长度 + body
            byte[] nameLen = BitConverter.GetBytes(nameBytes.Length);
            byte[] bodyLen = BitConverter.GetBytes(body.Length);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(nameLen);
                Array.Reverse(bodyLen);
            }

            await stream.WriteAsync(nameLen);
            await stream.WriteAsync(nameBytes);
            await stream.WriteAsync(bodyLen);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
        }

        private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int n = await stream.ReadAsync(buffer, offset, count);
                if (n == 0) throw new IOException("Connection closed by peer");
                offset += n;
                count -= n;
            }
        }
    }
}
