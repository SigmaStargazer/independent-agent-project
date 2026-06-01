@echo off
@chcp 65001 >nul
setlocal enabledelayedexpansion

:: ================= 配置区域 =================
:: 目标路径
set DEST_PATH=..\Src\IndependentAgentProject\Assets\References

:: 源路径列表
set SOURCES="..\Src\Lib\AgentProtocol\bin\Debug" "..\Src\Lib\Common\bin\Debug"

:: 要排除的文件（支持通配符，多个文件用空格隔开）
set EXCLUDE_FILES=UnityEngine.dll UnityEngine.*.dll UnityEditor.dll
:: ============================================

echo ==============================================
echo [INFO] 正在部署 DLL 到 Unity (已排除系统库)...
echo ==============================================

:: 确保目标目录存在
if not exist "%DEST_PATH%" mkdir "%DEST_PATH%"

for %%S in (%SOURCES%) do (
    set "SRC=%%~S"
    echo [PROCESS] 正在同步: !SRC!
    
    if not exist "!SRC!" (
        echo [ERROR] 源目录不存在: !SRC!
        goto :ERROR_EXIT
    )

    :: 使用 robocopy 代替 xcopy
    :: /S: 复制子目录 (不含空目录)
    :: /XF: 排除指定文件 (Exclude Files)
    :: /R:3: 失败重试3次 (防止瞬间锁定)
    :: /W:1: 重试等待1秒
    :: /NJH /NJS: 减少日志输出，保持界面整洁
    robocopy "!SRC!" "%DEST_PATH%" * /S /XF %EXCLUDE_FILES% /R:3 /W:1 /NJH /NJS
    
    :: robocopy 的退出码比较特殊：0-7 都算成功 (有文件复制或没有变化)
    if !ERRORLEVEL! GEQ 8 (
        echo [ERROR] 复制过程中出错，错误码: !ERRORLEVEL!
        goto :ERROR_EXIT
    )
)

echo.
echo ==============================================
echo [SUCCESS] 部署完成！
echo 已排除: %EXCLUDE_FILES%
echo ==============================================
pause
exit /b 0

:ERROR_EXIT
echo.
echo [FAIL] 部署失败，请检查 Unity 是否锁定文件。
pause
exit /b 1