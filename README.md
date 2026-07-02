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

**克隆后第一步（推荐）：启用本地编码 pre-commit 拦截**

为防止以 GBK 等非 UTF-8 编码提交 `.cs` / `.md` / `.proto` 等文本文件，仓库自带本地 git hook：

```bash
# Git Bash / Linux / macOS
bash Tools/enable_hooks.sh

# Windows CMD
Tools\enable_hooks.cmd
```

执行后 `git config core.hooksPath` 会被设为 `Tools/hooks`，之后 `git commit` 会自动跑 `Tools/check_file_encoding.py --staged`。出现 GBK 嫌疑文件可用 `python Tools/check_file_encoding.py --fix <path>` 批量转换。详见 `DevDocs/feature-design/项目编码基线.md`。

#### 使用说明

1. 单独测试智能体

   运行test.py文件

2. 测试智能体与客户端连接

   * 服务端：运行test_agent_server_protobuff.py

   * 客户端：运行CSharpClient.sln文件，或者运行unity客户端
