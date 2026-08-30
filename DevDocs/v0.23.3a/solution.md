# 技术方案 — v0.23.3 Python 内嵌运行时

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **引用方案**：`DevDocs/feature-design/打包方案.md`（§3.2 内嵌 python-build-standalone + 预装依赖、§5.3 requirements 生成）
> **最后更新**：2026-08-25

---

## 1. 方案概述

用 **python-build-standalone**（astral 出品，自带 pip、可重定位的完整 CPython 3.12）作为内嵌解释器，把全部第三方依赖**直接安装进解释器自身的 site-packages**（`pip install -r requirements.txt`，不使用 `--target`），产物为 `PythonServer/python/`（解释器 + 全部依赖，自包含）。业务代码与本地包 graphiti_core 留在 `PythonServer/` 原处，运行时靠**工作目录在 PythonServer**（或 `PYTHONPATH=PythonServer`）引入。整个 `PythonServer/` 可整体拷贝到无 Python/uv 的机器运行。

**开发/运行环境统一**：开发期也用这套 `PythonServer/python/`（3.12）环境启动与运行（不再用 `uv venv` 的 3.11）。`Src/PythonServer/.python-version = 3.12` 声明供 uv 解析，uv 仅保留生成 `requirements.txt` 的职责（`uv pip compile`），不再用于运行。维护一套环境、一套依赖版本。

> **设计调整（实现时发现）**：初版方案用 `pip install --target` 装到 `runtime/` + `PYTHONPATH` 引入，实测发现 `protobuf 3.20.3` 的 `google` **命名空间包**依赖 `.nspkg.pth` 文件，而 `--target` + PYTHONPATH 不会加载 `.pth`，导致 `ModuleNotFoundError: No module named 'google'`。改为直接装进解释器 site-packages 后 `.pth` 正常生效，方案更标准、更不易踩坑。

两个脚本产物：
- `Tools/requirements.txt` —— `uv pip compile` 导出的完整依赖树（排除本地包 graphiti_core）。
- `Tools/build_python_runtime.cmd` —— 下载内嵌解释器 + 装依赖（进 site-packages）+ 校验。

纯 Python 侧验证，不依赖 Unity；本期仍手动启动（v0.23.4 才做 Unity 自动拉起）。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Tools | `Tools/requirements.txt` | 新增：`uv pip compile` 导出的依赖清单 |
| Tools | `Tools/build_python_runtime.cmd` | 新增：下载内嵌 Python + 装依赖（进 site-packages）+ 校验 |
| Python | `Src/PythonServer/`（业务代码） | 无改动（仅验证拷贝后能跑） |
| 产物 | `Src/PythonServer/python/` | 运行时生成（解释器+依赖，自包含；**不入 git**，见 §5 风险） |
| 协议 | `Tools/message.proto` | 无 |
| Unity | 无 | 本期不碰 |

## 3. 详细设计

### 3.1 requirements.txt 生成（Tools/requirements.txt）

```bash
cd Src/PythonServer
uv pip compile pyproject.toml -o ../../Tools/requirements.txt --python-version 3.12
```

要点：
- 用 `uv pip compile` 而非手动抄 `pyproject.toml` 的 `dependencies`——能导出**完整解析树**（含 `pydantic`、`typing_extensions`、`openai`、`tiktoken` 等间接依赖）。graphiti_core 依赖这些库但没在 pyproject 显式声明，手动抄会漏。
- `pyproject.toml` 的 `[dependency-groups].dev`（pytest 等）**不导出**（运行时不需要）。
- **排除 graphiti_core**：它是仓库内本地包（无 setup/pyproject、纯 Python、无 C 扩展，已实测确认），不进 requirements；随业务代码拷贝到 `PythonServer/`，靠工作目录引入。
- 验证：生成的 requirements.txt 应含 `kuzu==0.11.3`（或 >=0.11.3）与 `langchain-openai`、`langgraph`、`pydantic`。

> 注：`uv pip compile` 需要先 `uv` 可用（打包机已装 uv 0.6.6）。若打包机无 uv，可改用 `pip freeze` 从当前 venv 导出 + 手工核对，但不推荐（可能混入无关包）。本方案以 uv 为准。

### 3.2 build_python_runtime.cmd（Tools/build_python_runtime.cmd）

```bat
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
```

**下载 URL 说明**：
- 源：GitHub `astral-sh/python-build-standalone` Releases。
- 文件名格式：`cpython-3.12.x+YYYYMMDD-x86_64-pc-windows-msvc-install_only_stripped.tar.gz`（install_only_stripped 版，无调试符号、体积最小，含 pip；已实测 ~21MB）。
- 具体版本号已实测确认：`20260602` / `cpython-3.12.13`，固定写入脚本变量 `PBS_VERSION` / `PBS_CPY` 以便复用缓存。

### 3.3 内嵌运行时启动验证（手动，本期验收）

```bat
cd Src/PythonServer
python\python.exe main.py              :: 工作目录即业务代码；无 Key 启动，监听端口
```

或验证 `--auto-init`：
```bat
python\python.exe main.py --auto-init   :: 读 api_config.json / .env 初始化
```

- 依赖已装进解释器 site-packages，**无需 PYTHONPATH 指向 runtime**；只需工作目录在 `PythonServer/`（业务代码 + graphiti_core 在 cwd 即 sys.path 中）。
- `main.py` 的 `PORT_CONFIG_FILE` 路径：`PROJECT_ROOT(../..)/Data/Config/agent_server_port.txt` —— 工作目录在 `PythonServer/` 时路径不变，无需改代码。
- `DBConnectionService` 的 `DB_ROOT="db"` 相对路径，工作目录是 `PythonServer/` 即生效，无需改代码。

## 4. 实现步骤

1. 生成 `Tools/requirements.txt`（`uv pip compile`，--python-version 3.12）。✅
2. 实测确认 python-build-standalone 3.12 具体下载 URL（`20260602` / `cpython-3.12.13`）。✅
3. 编写 `Tools/build_python_runtime.cmd`（纯 ASCII 注释，装依赖进解释器 site-packages）。✅
4. 执行脚本，生成 `python/`（解释器 + 依赖自包含）。✅
5. 用内嵌解释器从 `PythonServer/` 目录手动运行 `main.py` 验证（监听端口 / 写端口文件）。✅
6. 复制 `PythonServer/` 到无 Python/uv 环境（或临时改名系统 python），再次验证。
7. 更新文档状态。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| python-build-standalone 3.12 下载 URL 变更 | 版本号固定为变量 `PBS_VERSION` / `PBS_CPY`；首次执行后本地缓存，日常复用。 |
| kuzu cp312-win_amd64 wheel 兼容 | 已确认 uv.lock 有该 wheel（0.11.3）；脚本第 3 步校验 `import kuzu`。 |
| graphiti_core 本地包引入 | 纯 Python 无 C 扩展（已实测）；工作目录在 PythonServer 即 cwd 在 sys.path，可正常 import。 |
| **命名空间包 `.pth` 失效（--target 坑）** | **已规避**：改为直接装进解释器 site-packages，`.pth` 正常加载（实测 `import google.protobuf` 通过）。 |
| requirements 漏间接依赖 | 用 `uv pip compile` 导出完整树（含 pydantic/openai/typing_extensions 等），而非手抄。 |
| 3.12 与开发环境版本统一 | **2026-08-25 决策：开发/运行统一为 3.12**——开发期也用内嵌 3.12 环境，不再维护 3.11 venv；依赖清单已实测在 3.12 下装全可跑。 |
| `python/` 体积大（~300-500MB） | 已评估可接受（打包方案 §3.2 结论）；**不入 git**，已加 `.gitignore` 排除 `Src/PythonServer/python/`。 |
| .cmd 编码（中文注释） | .cmd 用 ASCII 注释（实测发现中文注释在 GBK 下乱码导致解析失败）；文档用 UTF-8。 |

## 6. 测试建议（验收方法）

**前置**：打包机已装 `uv`（0.6.6 已确认）与能联网下载 python-build-standalone。

**T1. requirements 生成**
- 执行 `uv pip compile pyproject.toml -o Tools/requirements.txt --python-version 3.12`。
- 断言：含 `kuzu`、`langchain-openai`、`langgraph`、`pydantic`；不含 `graphiti_core`；不含 pytest 等 dev 依赖。

**T2. 脚本执行**
- 执行 `Tools/build_python_runtime.cmd`。
- 断言：生成 `Src/PythonServer/python/python.exe`；脚本第 3 步校验打印 `OK: key imports work`。✅ 已实测通过

**T3. 内嵌解释器 import 校验**
- `cd Src/PythonServer && python\python.exe -c "import google.protobuf, kuzu, langchain_openai, langgraph, pydantic, openai, typing_extensions, tiktoken"`。
- 断言：无 ModuleNotFoundError；kuzu 原生扩展正常加载（`kuzu 0.11.3`）。✅ 已实测通过

**T4. main.py 手动启动（核心）**
- `cd Src/PythonServer && python\python.exe main.py`。
- 断言：监听端口；写 `Src/Data/Config/agent_server_port.txt`（非 0）；无 Traceback。✅ 已实测通过（两次分别监听 57294 / 51445，端口文件同步写入）
- `--auto-init` 模式：能读 `api_config.json`/`.env` 初始化 LLM/Embedder（可选，验证 Key 注入链路不回归）。

**T5. 无 Python/uv 机器验证**
- 把 `PythonServer/`（含 `python/` 自包含运行时 + 业务代码）复制到无 Python/uv 的 Windows 机器。
- 断言：`python\python.exe main.py` 同样跑通（打包方案 §10 验收项）。

**T6. 回归**
- 与 `uv run python main.py`（历史）行为一致（无 Key 启动监听 / `--auto-init` 初始化）；开发期统一用内嵌 `python/python.exe main.py`，`uv pip compile` 仅用于生成 requirements，均按 3.12 解析。

> 注：本期全部为 Python 侧自测，无需 Unity 联调。符合「可自测的功能必须自测完成后交付」纪律。

---

## 7. 实现记录

| 日期 | 说明 |
|------|------|
| 2026-08-24 | 生成方案（PRD/solution），用户确认「先做一版看一下」 |
| 2026-08-24 | 实现：生成 requirements.txt（uv pip compile 3.12）；实测下载 URL 20260602/cpython-3.12.13；编写 build_python_runtime.cmd（纯 ASCII）；实测发现 --target+PYPATH 破坏 protobuf 命名空间包 `.pth`，改为装进解释器 site-packages；生成自包含 python/（3.12.13 + 117 site-packages）；`.gitignore` 排除 python/；内嵌解释器运行 main.py 两次验证监听+写端口文件通过（57294/51445） |
| 2026-08-25 | 决策补充：开发/运行环境统一为 3.12（开发期用项目内 python/ 内嵌环境，声明 `.python-version=3.12`，不再维护 3.11 venv），PRD/solution 同步更新 |

---

*本文档由 Cursor Agent 根据 PRD 生成；实现前经用户确认，实测通过后记录。*
