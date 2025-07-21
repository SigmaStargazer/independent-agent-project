using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Google.Protobuf;
using IndependentAgentProject.Protobuf;

namespace CSharpClient
{
    class Program
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

            // 发送 LoginRequest
            var login = new LoginRequest
            {
                Username = "Alice",
                Password = "secret123"
            };
            await SendAsync(login, stream); // 参数顺序调整

            // 接收 LoginResponse
            var response = await ReceiveAsync<LoginResponse>(stream);
            if (response != null)
            {
                Console.WriteLine($"LoginResponse: Success={response.Success}, Message={response.Message}, UserId={response.UserId}");
            }

            // 发送 ChatMessage
            var chat = new ChatMessage
            {
                Sender = "Alice",
                Text = "Hello from C# client!"
            };
            await SendAsync(chat, stream); // 参数顺序调整

            // 接收 ChatMessage 回复
            var reply = await ReceiveAsync<ChatMessage>(stream);
            if (reply != null)
            {
                Console.WriteLine($"ChatMessage Reply: {reply.Sender} said: {reply.Text}");
            }

            Console.WriteLine("Done.");
        }

        static async Task SendAsync(IMessage message, NetworkStream stream)
        {
            byte[] data = message.ToByteArray();
            await stream.WriteAsync(data, 0, data.Length);
            Console.WriteLine($"Sent {message.Descriptor.FullName}, size={data.Length}");
        }

        static async Task<T> ReceiveAsync<T>(NetworkStream stream) where T : IMessage, new()
        {
            T message = new T();
            byte[] buffer = new byte[4096];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                Console.WriteLine("Connection closed by server.");
                return default;
            }

            byte[] data = new byte[bytesRead];
            Array.Copy(buffer, data, bytesRead);

            try
            {
                message.MergeFrom(data);
                Console.WriteLine($"Received {message.Descriptor.FullName}, size={bytesRead}");
                return message;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to parse message: " + ex.Message);
                return default;
            }
        }
    }
}
