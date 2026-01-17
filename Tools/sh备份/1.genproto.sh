#!/bin/bash

# 设置字符集 (通常 Mac 终端默认就是 UTF-8，但为了保险起见)
export LANG=en_US.UTF-8

# ==========================================
# 工具路径配置
# ==========================================
# 注意：在 Mac 上，通常建议通过 Homebrew 安装 protoc (brew install protobuf)
# 如果你已经安装了全局 protoc，可以直接用 "protoc"
# 如果你有特定的本地路径（例如下载了 protoc-3.2.0-osx-x86_64），请修改下面的路径
PROTOC="protoc"

# 原脚本中使用了 protogen 生成 C#，protoc 生成 Python。
# 如果这是标准的 Google Protobuf，同一个 protoc 就可以处理两者。
# 如果你需要特定版本的工具，请修改这里：
PROTOGEN="$PROTOC" 

echo "=============================================="
echo "[1/2] Generating C# Code..."
echo "=============================================="

# 执行 C# 生成命令
$PROTOGEN --csharp_out=../Src/Lib/AgentProtocol/ message.proto

# 检测上一条命令是否出错 ($? 获取上一个命令的退出状态，0 表示成功)
if [ $? -ne 0 ]; then
    echo ""
    echo "[ERROR] C# 生成失败！请检查 message.proto 文件。"
    echo "错误码: $?"
    read -p "按任意键退出..." -n 1 -s
    exit 1
fi

echo ""
echo "=============================================="
echo "[2/2] Generating Python Code..."
echo "=============================================="

# 执行 Python 生成命令
$PROTOC --python_out=../Src/PythonServer/network/ message.proto

# 检测上一条命令是否出错
if [ $? -ne 0 ]; then
    echo ""
    echo "[ERROR] Python 生成失败！请检查 message.proto 文件。"
    echo "错误码: $?"
    read -p "按任意键退出..." -n 1 -s
    exit 1
fi

echo ""
echo "=============================================="
echo "[SUCCESS] 所有协议生成成功！"
echo "=============================================="

# 暂停 (Mac 相当于 Windows 的 pause)
read -p "按任意键继续..." -n 1 -s
echo ""