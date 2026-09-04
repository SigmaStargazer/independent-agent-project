@echo off
REM ============================================================
REM build_python_exe.cmd - Package Python backend as agent_server.exe (v0.23.3b)
REM
REM Output: Build/PythonServer/agent_server.exe (onedir + noconsole) + external resources
REM Located under same root as Unity build output (Build/), forming the game root:
REM   Build/
REM     IndependentAgentProject.exe     <- Unity player entry
REM     IndependentAgentProject_Data/
REM     Data/Config/                    <- port file + api_config.json (shared by Unity & Python)
REM     PythonServer/                   <- produced by this script
REM       agent_server.exe
REM       _internal/
REM       config/                       <- idle_wakeup.json etc
REM       db/default_skills/            <- default skills
REM
REM Prereq: Src/PythonServer/.venv exists with pyinstaller installed
REM         (uv sync && uv pip install pyinstaller)
REM ============================================================
setlocal

REM Repo root (this script lives in <repo>/Tools/)
set "REPO_ROOT=%~dp0.."
set "PS_DIR=%REPO_ROOT%\Src\PythonServer"
set "OUT_DIR=%REPO_ROOT%\Build\PythonServer"

echo [build_python_exe] REPO_ROOT: %REPO_ROOT%
echo [build_python_exe] PS_DIR: %PS_DIR%
echo [build_python_exe] OUT_DIR: %OUT_DIR%

REM ---- 0. Pre-checks ----
if not exist "%PS_DIR%\.venv\Scripts\pyinstaller.exe" (
    echo [build_python_exe] ERROR: pyinstaller not found. Run:
    echo     cd Src/PythonServer ^&^& uv sync ^&^& uv pip install pyinstaller
    exit /b 1
)
if not exist "%PS_DIR%\main.py" (
    echo [build_python_exe] ERROR: main.py not found at %PS_DIR%\main.py
    exit /b 1
)

REM ---- 1. Clean old output, then package ----
if exist "%OUT_DIR%" rmdir /s /q "%OUT_DIR%"
if exist "%PS_DIR%\build\agent_server" rmdir /s /q "%PS_DIR%\build\agent_server"

echo [build_python_exe] Packaging with PyInstaller (onedir + noconsole)...
"%PS_DIR%\.venv\Scripts\pyinstaller.exe" -y --onedir --noconsole ^
    --name agent_server ^
    --paths "%PS_DIR%" ^
    --collect-all graphiti_core ^
    --collect-all tiktoken_ext ^
    --hidden-import tiktoken_ext ^
    --hidden-import tiktoken_ext.openai_public ^
    --collect-all certifi ^
    --distpath "%REPO_ROOT%\Build" ^
    --workpath "%PS_DIR%\build" ^
    --specpath "%PS_DIR%\build" ^
    "%PS_DIR%\main.py"
if errorlevel 1 (
    echo [build_python_exe] PyInstaller packaging FAILED
    exit /b 1
)

REM PyInstaller onedir puts output under distpath/<name>/.
REM Move agent_server/ up into OUT_DIR so layout is <OUT_DIR>/agent_server.exe + _internal/.
if exist "%REPO_ROOT%\Build\agent_server" (
    echo [build_python_exe] Moving agent_server/ into %OUT_DIR%...
    move /y "%REPO_ROOT%\Build\agent_server" "%OUT_DIR%" >nul
)

REM ---- 2. Copy external resources next to the exe ----
echo [build_python_exe] Copying external resources...
if exist "%PS_DIR%\config" xcopy /E /I /Y "%PS_DIR%\config" "%OUT_DIR%\config" >nul
if exist "%PS_DIR%\db\default_skills" xcopy /E /I /Y "%PS_DIR%\db\default_skills" "%OUT_DIR%\db\default_skills" >nul

REM NOTE: Do NOT copy Lib/ (Src/Lib/proto only has message.proto source, unused at runtime;
REM       Python actually uses network/message_pb2.py, auto-collected by PyInstaller).
REM NOTE: Do NOT copy .env (player supplies own keys; packaged build starts keyless,
REM       see 打包方案.md 4.4 - reference only, keep ASCII here).

REM ---- 3. Sanity check: port file dir must exist (Python writes port file on start) ----
if not exist "%REPO_ROOT%\Build\Data\Config" (
    echo [build_python_exe] WARN: Build\Data\Config does not exist (created after Unity build / port write).
)

echo [build_python_exe] DONE. Artifact at:
echo     %OUT_DIR%\agent_server.exe
echo [build_python_exe] Verify: start the exe, then check Build\Data\Config\agent_server_port.txt for a port.

endlocal
exit /b 0
