#!/bin/bash

# 设置字符集
export LANG=en_US.UTF-8

# ================= 配置区域 =================
# 注意：Mac/Linux 使用正斜杠 "/"，而不是反斜杠 "\"
# 源路径 (C# 类库输出目录)
SOURCE_PATH="../Src/Lib/AgentProtocol/bin/Debug"

# 目标路径 (Unity 工程目录)
DEST_PATH="../Src/ShootingEditor2D/Assets/References"

# ================= 执行区域 =================

echo "=============================================="
echo "[INFO] 正在部署 DLL 到 Unity..."
echo "源: $SOURCE_PATH"
echo "标: $DEST_PATH"
echo "=============================================="

# 1. 检查源目录是否存在
if [ ! -d "$SOURCE_PATH" ]; then
    echo ""
    echo "[ERROR] 源目录不存在！"
    echo "请先在 Visual Studio (Mac) 中编译 AgentProtocol 项目。"
    echo "路径: $SOURCE_PATH"
    read -p "按任意键退出..." -n 1 -s
    exit 1
fi

# 2. 准备目标目录 (如果目标目录不存在，mkdir -p 会自动创建，类似于 xcopy /I)
mkdir -p "$DEST_PATH"

# 3. 执行复制
# cp -R: 递归复制目录
# -f: 强制覆盖 (不提示)
# "$SOURCE_PATH/"*: 复制目录下所有内容
cp -Rf "$SOURCE_PATH/"* "$DEST_PATH/"

# 4. 错误检查 (检测上一条 cp 命令的退出代码)
if [ $? -ne 0 ]; then
    echo ""
    echo "=============================================="
    echo "[ERROR] 复制失败！"
    echo "----------------------------------------------"
    echo "可能原因："
    echo "1. Unity 正在运行且锁定了 DLL 文件。"
    echo "   -> 请尝试关闭 Unity 或者让 Unity 重新加载脚本。"
    echo "2. 目标路径只读或权限不足。"
    echo "=============================================="
    read -p "按任意键退出..." -n 1 -s
    exit 1
fi

echo ""
echo "=============================================="
echo "[SUCCESS] 部署完成！"
echo "=============================================="

# 稍微停顿
read -p "按任意键关闭..." -n 1 -s
echo ""