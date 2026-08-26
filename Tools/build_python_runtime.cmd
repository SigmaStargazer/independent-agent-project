@echo off
setlocal enabledelayedexpansion
REM ============================================================
REM Build Python embedded runtime (v0.23.3)
REM Outputs:
REM   Src/PythonServer/python\   embedded CPython 3.12 (python-build-standalone)
REM                                + ALL third-party deps installed into its site-packages
REM   (no separate runtime\ dir; deps live inside python\ so it is self-contained)
REM Usage:
REM   First run : download interpreter + install dependencies
REM   Daily use : reuse existing python.exe if present, reinstall/reuse deps
REM Prereq: network access to github.com (download interpreter) and pypi.org (install deps)
REM Run:  cd Src/PythonServer && python\python.exe main.py
REM ============================================================

set "PBS_VERSION=20260602"
set "PBS_CPY=cpython-3.12.13+20260602-x86_64-pc-windows-msvc-install_only_stripped.tar.gz"
set "PBS_URL=https://github.com/astral-sh/python-build-standalone/releases/download/%PBS_VERSION%/%PBS_CPY%"

REM %~dp0 = this script's dir (Tools\)
set "TOOLS_DIR=%~dp0"
set "PROJECT_ROOT=%TOOLS_DIR%.."
set "PYTHON_SERVER_DIR=%PROJECT_ROOT%\Src\PythonServer"
set "PYTHON_DIR=%PYTHON_SERVER_DIR%\python"
set "REQ_FILE=%TOOLS_DIR%requirements.txt"

echo [build_python_runtime] project root: %PROJECT_ROOT%
if not exist "%PYTHON_SERVER_DIR%" (
    echo [build_python_runtime] [ERROR] PythonServer not found: %PYTHON_SERVER_DIR%
    exit /b 1
)

REM ---- 1. embedded interpreter ----
if not exist "%PYTHON_DIR%\python.exe" (
    if not exist "%PYTHON_DIR%" mkdir "%PYTHON_DIR%"
    echo [build_python_runtime] downloading python-build-standalone 3.12 (Windows x64)...
    echo [build_python_runtime] %PBS_URL%
    powershell -NoProfile -Command "Invoke-WebRequest -Uri '%PBS_URL%' -OutFile '%TEMP%\pbs_%PBS_VERSION%.tar.gz'"
    if errorlevel 1 (
        echo [build_python_runtime] [ERROR] download failed
        exit /b 1
    )
    echo [build_python_runtime] extracting interpreter...
    tar -xzf "%TEMP%\pbs_%PBS_VERSION%.tar.gz" -C "%PYTHON_DIR%" --strip-components=1
    if errorlevel 1 (
        echo [build_python_runtime] [ERROR] extract failed
        exit /b 1
    )
    del /q "%TEMP%\pbs_%PBS_VERSION%.tar.gz"
) else (
    echo [build_python_runtime] reusing existing interpreter: %PYTHON_DIR%
)

if not exist "%PYTHON_DIR%\python.exe" (
    echo [build_python_runtime] [ERROR] interpreter not ready: %PYTHON_DIR%\python.exe
    exit /b 1
)

REM ---- 2. install dependencies into interpreter site-packages ----
REM NOTE: install WITHOUT --target. --target + PYTHONPATH breaks namespace-package
REM .pth files (e.g. protobuf 3.20 'google'), causing ModuleNotFoundError at runtime.
echo [build_python_runtime] installing dependencies into interpreter site-packages...
"%PYTHON_DIR%\python.exe" -m pip install -r "%REQ_FILE%" --no-warn-script-location
if errorlevel 1 (
    echo [build_python_runtime] [ERROR] dependency install failed
    exit /b 1
)

REM ---- 3. verify key imports ----
echo [build_python_runtime] verifying key imports...
"%PYTHON_DIR%\python.exe" -c "import google.protobuf, kuzu, langchain_openai, langgraph, pydantic, openai, typing_extensions, tiktoken; print('OK: key imports work')"
if errorlevel 1 (
    echo [build_python_runtime] [ERROR] key import check failed
    exit /b 1
)

echo [build_python_runtime] done: python\ is self-contained and ready
endlocal
