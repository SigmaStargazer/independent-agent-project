@echo off
@chcp 65001 >nul
setlocal

:: 设置工具路径，方便管理
set PROTOGEN="protoc-3.2.0-win32/bin/protogen"
set PROTOC="protoc-3.2.0-win32/bin/protoc"

echo ==============================================
echo [1/2] Generating C# Code...
echo ==============================================
%PROTOGEN% --csharp_out=../Src/Lib/AgentProtocol/ message.proto

:: 检测上一条命令是否出错
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] C# 生成失败！请检查 message.proto 文件。
    echo 错误码: %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ==============================================
echo [2/2] Generating Python Code...
echo ==============================================
%PROTOC% --python_out=../Src/PythonServer/network/ message.proto

:: 检测上一条命令是否出错
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Python 生成失败！请检查 message.proto 文件。
    echo 错误码: %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ==============================================
echo [SUCCESS] 所有协议生成成功！
echo ==============================================

:: 如果成功，稍微停顿一下让你看到，或者可以直接去掉这行让窗口自动关闭
pause