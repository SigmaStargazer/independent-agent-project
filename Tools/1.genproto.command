#!/bin/bash
cd "$(dirname "$0")" || exit

# ==========================================
# 工具路径
# ==========================================
PROTOGEN="protogen"
PROTOC="protoc"
PROTO_FILE="message.proto"
CS_OUT_PATH="../Src/Lib/AgentProtocol/"
PY_OUT_PATH="../Src/PythonServer/network/"

echo "=============================================="
echo "[1/3] Generating C# Code (Standard Mode)..."
echo "=============================================="

# 1. 使用默认模式生成 (不加 +names=Original)
# 这样 Name, Desc, MapId, Success 等属性都会正确生成为大写 (PascalCase)
$PROTOGEN --csharp_out=$CS_OUT_PATH $PROTO_FILE

if [ $? -ne 0 ]; then
    echo "[ERROR] C# 生成失败！"
    exit 1
fi

echo ""
echo "=============================================="
echo "[2/3] Patching C# Code (Fixing CamelCase)..."
echo "=============================================="

# 2. 定位生成的 C# 文件 (通常是 message.cs，或者与 proto 同名)
# protobuf-net 通常会生成 message.cs
CS_FILE="$CS_OUT_PATH/message.cs"

if [ ! -f "$CS_FILE" ]; then
    echo "[ERROR] 找不到生成的 C# 文件: $CS_FILE"
    exit 1
fi

# 3. 使用 sed 批量修正特殊的字段名
# 将 "public AgentCreateRequest AgentCreateRequest" 替换为 "public AgentCreateRequest agentCreateRequest"
# 这样既保留了 Name 的大写，又满足了 agentCreateRequest 的小写需求

# 定义修正函数 (适配 Mac 的 sed 语法)
fix_field() {
    TYPE_NAME=$1
    # 查找 public TypeName TypeName -> public TypeName typeName
    # 首字母变小写
    PROP_NAME="$(tr '[:upper:]' '[:lower:]' <<< ${TYPE_NAME:0:1})${TYPE_NAME:1}"
    
    echo "  - Patching $TYPE_NAME -> $PROP_NAME"
    sed -i '' "s/public $TYPE_NAME $TYPE_NAME/public $TYPE_NAME $PROP_NAME/g" "$CS_FILE"
}

# 4. 对报错中涉及的几个关键类进行修正
# 这些是根据你之前的报错日志提取的
fix_field "AgentCreateRequest"
fix_field "SceneStartRequest"
fix_field "UserSendMessageRequest"
fix_field "AgentSendMessageRequest"

fix_field "AgentCreateResponse"
fix_field "SceneStartResponse"
fix_field "AgentSendMessageResponse"

# 如果还有其他类似的 xxxRequest/xxxResponse 报错，可以在这里继续添加

echo "修正完成！"

echo ""
echo "=============================================="
echo "[3/3] Generating Python Code..."
echo "=============================================="

$PROTOC --python_out=$PY_OUT_PATH $PROTO_FILE

if [ $? -ne 0 ]; then
    echo "[ERROR] Python 生成失败！"
    exit 1
fi

echo ""
echo "=============================================="
echo "[SUCCESS] 所有协议生成并修正成功！"
echo "=============================================="
read -p "按任意键继续..." -n 1 -s
echo ""