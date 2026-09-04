@echo off
@chcp 65001 >nul
setlocal
REM v0.23.5 文案导出。用法：双击，或仓库根执行 Tools\export_localization.cmd；可透传 --excel 等参数
set "PYTHONIOENCODING=utf-8"

pushd "%~dp0\.."
set "SCRIPT=Tools\export_localization.py"

REM 优先使用 PythonServer 的 uv 虚拟环境（多人协作统一依赖）。
set "VENV_PY=Src\PythonServer\.venv\Scripts\python.exe"

set "PY_CMD="
if exist "%VENV_PY%" (
    "%VENV_PY%" -c "import openpyxl" >nul 2>&1
    if %ERRORLEVEL%==0 set "PY_CMD=%VENV_PY%"
    if not defined PY_CMD (
        echo [INFO] venv 缺 openpyxl，尝试 uv sync 同步依赖...
        pushd Src\PythonServer
        uv sync >nul 2>&1
        popd
        "%VENV_PY%" -c "import openpyxl" >nul 2>&1
        if %ERRORLEVEL%==0 set "PY_CMD=%VENV_PY%"
    )
)

REM 兜底：系统 py / python（带 openpyxl 者）
if not defined PY_CMD (
    py -3.12 -c "import openpyxl" >nul 2>&1
    if %ERRORLEVEL%==0 set "PY_CMD=py -3.12"
)
if not defined PY_CMD (
    py -3 -c "import openpyxl" >nul 2>&1
    if %ERRORLEVEL%==0 set "PY_CMD=py -3"
)
if not defined PY_CMD (
    python -c "import openpyxl" >nul 2>&1
    if %ERRORLEVEL%==0 set "PY_CMD=python"
)

if not defined PY_CMD (
    echo.
    echo [ERROR] 未找到可用的 Python（需要能 import openpyxl）。
    echo 请先执行：cd Src\PythonServer ^&^& uv sync
    echo.
    popd
    pause
    exit /b 1
)

echo [INFO] 使用解释器: %PY_CMD%
%PY_CMD% "%SCRIPT%" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] 导出失败，请检查上方输出。
    echo.
    popd
    pause
    exit /b %ERRORLEVEL%
)

echo [OK] 导出完成。
echo.
echo 请确认以上导出结果，确认无误后按任意键关闭窗口。
popd
pause
