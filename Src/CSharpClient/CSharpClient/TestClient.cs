using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace CSharpClient
{
    class ClientProgram
    {
        static void Main_0(string[] args)
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

            TcpClient client = null;
            NetworkStream stream = null;

            try
            {
                // 建立连接
                Console.WriteLine($"开始建立连接到端口 {port}");
                client = new TcpClient("localhost", port);
                stream = client.GetStream();

                // 接收欢迎消息
                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                string welcomeMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine(welcomeMessage);

                // 发送数据
                string[] messages = { "Michael", "Tracy", "Sarah" };
                foreach (string message in messages)
                {
                    byte[] dataToSend = Encoding.UTF8.GetBytes(message);
                    stream.Write(dataToSend, 0, dataToSend.Length);

                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine(response);
                }

                // 发送退出命令
                byte[] exitData = Encoding.UTF8.GetBytes("exit");
                stream.Write(exitData, 0, exitData.Length);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }
            finally
            {
                // 关闭流和客户端
                stream?.Close();
                client?.Close();
            }
        }
    }
}




