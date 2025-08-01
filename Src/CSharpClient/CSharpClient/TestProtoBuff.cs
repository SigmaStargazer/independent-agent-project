using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using IndependentAgentProject.Protobuf;

namespace CSharpClient
{
    public class Client
    {
        public static void Main_1()
        {
            try
            {
                TcpClient client = new TcpClient("localhost", 5005);
                using (NetworkStream stream = client.GetStream())
                {
                    // 构造并发送消息
                    Person person = new Person
                    {
                        Name = "Alice",
                        Age = 30,
                        Message = "Hello from C# client!"
                    };
                    byte[] data = person.ToByteArray();
                    byte[] lengthPrefix = BitConverter.GetBytes((int)data.Length);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(lengthPrefix); // 转换为大端序
                    }
                    stream.Write(lengthPrefix, 0, lengthPrefix.Length);
                    stream.Write(data, 0, data.Length);

                    // 接收并解析响应
                    byte[] lenBytes = new byte[4];
                    stream.Read(lenBytes, 0, 4);
                    int responseLength = BitConverter.ToInt32(lenBytes, 0);
                    if (BitConverter.IsLittleEndian)
                    {
                        responseLength = IPAddress.NetworkToHostOrder(responseLength);
                    }
                    byte[] responseData = new byte[responseLength];
                    stream.Read(responseData, 0, responseData.Length);
                    Person response = Person.Parser.ParseFrom(responseData);
                    Console.WriteLine($"Received: Name={response.Name}, Age={response.Age}, Message={response.Message}");
                }
                client.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
