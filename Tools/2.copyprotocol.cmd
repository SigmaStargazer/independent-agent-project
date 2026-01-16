@echo off
@chcp 65001 >nul
setlocal

:: ================= 配置区域 =================
:: 源路径 (C# 类库输出目录) - 去掉末尾的 \*，在命令里加
set SOURCE_PATH=..\Src\Lib\AgentProtocol\bin\Debug

:: 目标路径 (Unity 工程目录)
set DEST_PATH=..\Src\ShootingEditor2D\Assets\References

:: ================= 执行区域 =================

echo ==============================================
echo [INFO] 正在部署 DLL 到 Unity...
echo 源: %SOURCE_PATH%
echo 标: %DEST_PATH%
echo ==============================================

:: 1. 检查源目录是否存在 (防止没编译就运行脚本)
if not exist "%SOURCE_PATH%" (
    echo.
    echo [ERROR] 源目录不存在！
    echo 请先在 Visual Studio 中编译 AgentProtocol 项目。
    echo 路径: %SOURCE_PATH%
    pause
    exit /b 1
)

:: 2. 执行复制
:: /S: 复制目录和子目录(不包括空目录) -> 比 /E 更干净一点，除非你需要空目录
:: /I: 如果目标不存在，默认作为目录处理
:: /Y: 直接覆盖不提示
xcopy /S /I /Y "%SOURCE_PATH%\*" "%DEST_PATH%\"

:: 3. 错误检查 (关键！Unity 经常锁定 DLL)
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ==============================================
    echo [ERROR] 复制失败！
    echo ----------------------------------------------
    echo 可能原因：
    echo 1. Unity 正在运行且锁定了 DLL 文件。
    echo    -> 请尝试关闭 Unity 或者让 Unity 重新加载脚本。
    echo 2. 目标路径只读或权限不足。
    echo ==============================================
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ==============================================
echo [SUCCESS] 部署完成！
echo ==============================================

:: 稍微停顿，如果是双击运行可以看到结果
pause