using SkillBridge.Message;
using ProtoBuf;
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
    class TestProtoBuff_5
    {
        static NetworkStream stream;
        static bool isConnected = true;
        static TcpClient currentClient;

        static readonly Dictionary<string, Type> MessageTypes =
            typeof(SkillBridge.Message.AgentCreateRequest).Assembly
                .GetTypes()
                .Where(t => t.Namespace == "IndependentAgentProject.Protobuf" &&
                           typeof(IExtensible).IsAssignableFrom(t) &&
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

        static async Task Main_5(string[] args)
        {
            await ConnectAndRun();
        }

        static async Task SendInitialMessages()
        {
            // 创建Agent
            var agentCreateRequest = new AgentCreateRequest
            {
                Name = "小明",
                Desc = "是一个帮助机器人"
            };
            var netMessageAgentCreateRequest = new NetMessage
            {
                Request = new NetMessageRequest
                {
                    agentCreateRequest = agentCreateRequest
                }
            };
            await SendAsync(netMessageAgentCreateRequest, stream);

            agentCreateRequest = new AgentCreateRequest
            {
                Name = "小红",
                Desc = "是用户的秘书"
            };
            netMessageAgentCreateRequest = new NetMessage
            {
                Request = new NetMessageRequest
                {
                    agentCreateRequest = agentCreateRequest
                }
            };
            await SendAsync(netMessageAgentCreateRequest, stream);

            // 开始场景
            var sceneStartRequest = new SceneStartRequest
            {
                MapId = 1
            };
            var netMessageStartSceneRequest = new NetMessage
            {
                Request = new NetMessageRequest
                {
                    sceneStartRequest = sceneStartRequest
                }
            };
            await SendAsync(netMessageStartSceneRequest, stream);

            // 发送聊天信息
            var agentSendMessageRequest = new UserSendMessageRequest
            {
                Agent = "小明",
                UserMessage = "和小红说，让她闹个每天8点的起床铃，9点的上班铃声，然后每天闹铃响时让她直接通知我。"
            };
            var netMessageAgentSendMessageRequest = new NetMessage
            {
                Request = new NetMessageRequest
                {
                    userSendMessageRequest = agentSendMessageRequest
                }
            };
            await SendAsync(netMessageAgentSendMessageRequest, stream);

            //var agentSendMessageRequest = new UserSendMessageRequest
            //{
            //    Agent = "小明",
            //    UserMessage = "你是谁？"
            //};
            //await SendAsync(agentSendMessageRequest, stream);
        }

        static async Task ReceiveMessages()
        {
            try
            {
                while (isConnected)
                {
                    // 1. 4 字节 NetMessage 长度（小端）
                    byte[] lenBuf = new byte[4];
                    await ReadExactlyAsync(stream, lenBuf, 0, 4);
                    int len = BitConverter.ToInt32(lenBuf, 0);
                    //if (BitConverter.IsLittleEndian)
                    //    len = System.Net.IPAddress.NetworkToHostOrder(len);

                    // 2. 读完整 NetMessage
                    byte[] body = new byte[len];
                    await ReadExactlyAsync(stream, body, 0, len);

                    // 3. 反序列化
                    var netMsg = Serializer.Deserialize<NetMessage>(new MemoryStream(body));

                    // 1) 先看 Response 里到底装了哪一个
                    if (netMsg.Response?.agentCreateResponse is { } acr)
                    {
                        Console.WriteLine($"AgentCreateResponse Success={acr.Success}, Errormsg={acr.Errormsg}");
                    }
                    else if (netMsg.Response?.sceneStartResponse is { } ssr)
                    {
                        Console.WriteLine($"StartSceneResponse Success={ssr.Success}, Errormsg={ssr.Errormsg}");
                    }
                    else if (netMsg.Request?.agentSendMessageRequest is { } asm)
                    {
                        Console.WriteLine($"Received AgentSendMessageRequest: Agent={asm.Agent}, AiMessage={asm.AiMessage}");
                    }
                    else
                    {
                        Console.WriteLine("Unknown or empty Response.");
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

        private static async Task SendAsync<T>(T message, NetworkStream stream) where T : IExtensible
        {
            // 1. protobuf-net 序列化 NetMessage
            byte[] body;
            using (var ms = new MemoryStream())
            {
                Serializer.Serialize(ms, message);
                body = ms.ToArray();
            }

            //// 2. 4 字节长度头（小端）
            var header = BitConverter.GetBytes(body.Length);
            //if (BitConverter.IsLittleEndian)
            //    Array.Reverse(header);

            // 3. 发送
            await stream.WriteAsync(header);
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
