using Google.Protobuf;
using IndependentAgentProject.Protobuf;
using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace CSharpClient
{
    class TestProtoBuff_3
    {
        static async Task Main(string[] args)
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
                //Console.WriteLine($"rootPath: {rootPath}");
                Console.WriteLine($"projectRoot: {projectRoot}");
                //Console.WriteLine($"currentDirectory: {currentDirectory}");
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

            using var client = new TcpClient();
            await client.ConnectAsync("localhost", port);
            var stream = client.GetStream();

            Console.WriteLine("Connected to server.");
            //// 创建Agent
            // 发送 Request
            var agentCreateRequest = new AgentCreateRequest
            {
                Name = "小明",
                Desc = ""
            };
            await SendAsync(agentCreateRequest, stream);

            // 接收 Response
            var response = await ReceiveAsync<AgentCreateResponse>(stream);
            if (response != null)
            {
                Console.WriteLine($"AgentCreateResponse: Success={response.Success}, Message={response.Errormsg}");
            }

            //// 开始场景
            var startSceneRequest = new StartSceneRequest
            {
                MapId = 1
            };
            await SendAsync(startSceneRequest, stream);

            //// 发送聊天信息
            var agentSendMessageRequest = new AgentSendMessageRequest
            {
                Agent = "小明",
                UserMessage = "闹个每天8点的起床铃，9点的上班铃声"
            };
            await SendAsync(agentSendMessageRequest, stream);

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
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

        private static async Task<T> ReceiveAsync<T>(NetworkStream stream) where T : IMessage, new()
        {
            // 1. 4 字节长度
            byte[] lenBuf = new byte[4];
            await ReadExactlyAsync(stream, lenBuf, 0, 4);
            int len = BitConverter.ToInt32(lenBuf, 0);
            if (BitConverter.IsLittleEndian)
                len = System.Net.IPAddress.NetworkToHostOrder(len);

            // 2. body
            byte[] body = new byte[len];
            await ReadExactlyAsync(stream, body, 0, len);

            T msg = new T();
            msg.MergeFrom(body);
            return msg;
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

//// 发送 LoginRequest
//var login = new LoginRequest
//{
//    Username = "Alice",
//    Password = "secret123"
//};
//await SendAsync(login, stream); // 参数顺序调整

//// 接收 LoginResponse
//var response = await ReceiveAsync<LoginResponse>(stream);
//if (response != null)
//{
//    Console.WriteLine($"LoginResponse: Success={response.Success}, Message={response.Message}, UserId={response.UserId}");
//}

//// 发送 ChatMessage
//var chat = new ChatMessage
//{
//    Sender = "Alice",
//    Text = "Hello from C# client!"
//};
//await SendAsync(chat, stream); // 参数顺序调整

//// 接收 ChatMessage 回复
//var reply = await ReceiveAsync<ChatMessage>(stream);
//if (reply != null)
//{
//    Console.WriteLine($"ChatMessage Reply: {reply.Sender} said: {reply.Text}");
//}

//Console.WriteLine("Done.");
