# Independent-Agent-Project

#### 介绍
本项目演示了 **Python (智能体)** 与 **C# (环境)** 之间的跨语言交互控制，用于验证智能体系统的通信链路与控制逻辑。

#### 软件架构
采用 **Client-Server (C/S)** 架构：

1. **Python 服务端 (Brain)**:
   - 托管智能体核心逻辑，下发控制指令。
2. **C# 客户端 (Environment)**:
   - 构建模拟工作环境，处理物理/逻辑反馈。
3. **数据通信**:
   - 使用 **Protobuf** 进行高效消息序列化。
   - 字节序标准：**Little-Endian (小端)**。


#### 安装教程

clone后，使用以下命令安装依赖：

```
uv sync
```

#### 使用说明

1. 单独测试智能体

   运行test.py文件

2. 测试智能体与客户端连接

   * 服务端：运行test_agent_server_protobuff.py

   * 客户端：运行CSharpClient.sln文件，或者运行unity客户端
