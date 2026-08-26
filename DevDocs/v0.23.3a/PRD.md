# PRD — v0.23.3 Python 内嵌运行时

> **状态**：已确认（2026-08-24 用户确认开发并实测通过；2026-08-25 补充「开发/运行环境统一」决策）
> **对应需求**：`DevDocs/feature-design/打包方案.md`（§3.2 Python 运行时打包方式、§10 版本规划「v0.23.1 Python 内嵌运行时」）
> **引用方案**：`DevDocs/feature-design/打包方案.md`（§3.2 内嵌 python-build-standalone + 预装依赖、§5.3 requirements 生成）
> **最后更新**：2026-08-25

---

## 1. 背景与目标

打包方案把「Python 内嵌运行时」列为打包改造的第二版（原规划 v0.23.1，因实际版本目录 `v0.23.1` 已被「API Key 配置优化」占用，本版本顺延为 **v0.23.3** 立项）。

**现状**：开发期 Python 靠 `uv run python main.py`（`uv` 解析 `pyproject.toml` + 虚拟环境）启动，依赖 `uv` 与联网安装。玩家机器不能假设有 Python/uv。

**本版本目标**：
1. 把 Python 解释器 + 全部依赖打进 `PythonServer/` 目录，做成**可拷贝即用**的运行时——用 `python-build-standalone` 提供自带 pip 的解释器，把全部依赖**直接安装进解释器 site-packages**（自包含），运行时工作目录在 `PythonServer/` 即可。玩家机器无需装 Python/uv。
2. **开发环境与运行环境统一为同一套**：开发期也用项目内 `PythonServer/python/`（3.12）这套内嵌环境，不再维护 `uv venv`(3.11) 与内嵌运行时(3.12) 两套环境。

**边界**：本版本只做「运行时本身可用」，**仍由开发者手动启动**（用内嵌 `python.exe` 验证）；「Unity 双击 exe 自动拉起 Python」留 v0.23.4。纯 Python 侧验证，不依赖 Unity 联调。

## 2. 范围

### 2.1 本期包含

- 新增 `Tools/requirements.txt`：从 `pyproject.toml` 用 `uv pip compile` 导出完整依赖树（含 `pydantic`/`typing_extensions`/`openai` 等间接依赖），**排除 `graphiti_core`**（仓库内本地包，随业务代码拷贝，靠工作目录引入）。
- 新增 `Tools/build_python_runtime.cmd`：下载 python-build-standalone（Windows x64）到 `PythonServer/python/`；用该解释器 `pip install -r Tools/requirements.txt` **把依赖直接装进解释器 site-packages**（自包含，无独立 runtime/ 目录）。
- **开发环境统一**：声明 `.python-version = 3.12`，开发期也用 `PythonServer/python/python.exe` 这套内嵌环境（替代 `uv run` 的 3.11 venv），详见 §5 非功能需求。
- 验证：用内嵌 `python.exe` 从 `PythonServer/` 目录运行 `main.py`，能监听端口、写端口文件。

### 2.2 本期不包含

- **Unity 自动拉起 Python 子进程**（`PythonProcessLauncher`，v0.23.4）。
- **API Key 注入 / 初始化时序**（v0.23.0a/b 已做，运行时仍读 `api_config.json` / `.env`）。
- **源码保护（.pyc 编译）**（v0.23.6）。
- **一键打包脚本**（`build_package.cmd`，v0.23.5）。
- macOS 运行时（§12 另行立项）。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 打包机 | 首次构建运行时 | 下载内嵌 Python + 装依赖（进 site-packages），产出自包含 `PythonServer/python/` |
| 打包机 | 日常打包 | 复用已有 `python/`（无需重复下载），有依赖变更才刷新 |
| 开发者 | 开发期启动 Python | 直接用 `PythonServer/python/python.exe main.py`，行为与 `uv run` 一致，无需 PYTHONPATH |
| 开发者 | 日常开发/单测 | 用同一套 3.12 内嵌环境，不再维护两套版本 |
| 玩家（后续版本） | 运行打包版 | 不装 Python/uv 也能由 Unity 拉起内嵌 python.exe |

## 4. 功能需求

### 4.1 requirements.txt 生成

- 用 `uv pip compile Src/PythonServer/pyproject.toml -o Tools/requirements.txt --python-version 3.12`（打包方案 §5.3）。
- **排除 `graphiti_core`**：它是仓库内本地包（无 setup/pyproject，纯 Python，无 C 扩展），不进 `requirements.txt`；随业务代码放在 `PythonServer/`，靠工作目录（cwd 在 sys.path）引入。
- 导出结果应包含 `pyproject.toml` 直接依赖 + 全部间接依赖（如 `pydantic`、`typing_extensions`、`openai`、`tiktoken` 等 graphiti_core 依赖到的库），确保 `pip install` 能装全。

### 4.2 build_python_runtime.cmd 脚本

```
1. 定位/下载 python-build-standalone 3.12（Windows x64）到 PythonServer/python/
   - 已有则跳过下载（缓存）
2. 用 PythonServer/python/python.exe 执行
   pip install -r Tools/requirements.txt
   - 直接装进解释器 site-packages（自包含），无独立 runtime/ 目录、无 PYTHONPATH
3. 校验：python.exe -c "import google.protobuf, kuzu, langchain_openai, langgraph, pydantic, openai" 应成功
```

- 下载源：GitHub `astral-sh/python-build-standalone` 的 releases（`cpython-3.12.13+20260602-x86_64-pc-windows-msvc-install_only_stripped.tar.gz`）。
- 输出：`PythonServer/python/`（解释器 + 全部依赖，自包含）。
- **为何不用 `--target`**：`protobuf 3.20.3` 的 `google` 命名空间包依赖 `.nspkg.pth`，`--target` + PYTHONPATH 不加载 `.pth`，实测报 `No module named 'google'`；直接装进 site-packages 后 `.pth` 正常生效。

### 4.3 内嵌运行时启动验证

- 从 `PythonServer/` 目录运行 `python/python.exe main.py`（工作目录即业务代码，无需 PYTHONPATH）：
  - 能监听端口、写 `Src/Data/Config/agent_server_port.txt`（`main.py` 的 `PORT_CONFIG_FILE` 路径不变）。
  - 与 `uv run python main.py` 行为一致（无 Key 启动 / `--auto-init`）。

## 5. 非功能需求

- **Python 版本（开发/运行统一为 3.12）**：内嵌运行时用 **3.12**（`kuzu 0.11.3` 有 `cp312-win_amd64.whl`，uv.lock 也解析到 3.12）。开发环境**不再单独维护 3.11 venv**，改为：
  - 在 `Src/PythonServer/` 声明 `.python-version = 3.12`（供 uv 解析；uv 仅保留生成 `requirements.txt` 的职责，不再用于运行）；
  - 实际运行/开发统一用 `PythonServer/python/python.exe`（内嵌 3.12）这套环境。
- **可复用/缓存**：`python/` 只建一次，日常打包与开发复用；有依赖变更才重装。
- **UTF-8**：`.cmd` 与文档 UTF-8（`.cmd` 内容注释用英文避免编码问题，实测中文注释在 GBK 下乱码导致解析失败）。
- **不改业务代码**：`main.py` 等零改动（路径已验证兼容）。

## 6. 验收标准

- [ ] 执行 `Tools/build_python_runtime.cmd`，生成自包含 `PythonServer/python/`（解释器 + 依赖进 site-packages）。
- [ ] 内嵌 `python.exe` 能 `import google.protobuf, kuzu, langchain_openai, langgraph, pydantic, openai, typing_extensions, tiktoken`。
- [ ] 从 `PythonServer/` 目录运行 `python/python.exe main.py`，能监听端口、写端口文件（无需 PYTHONPATH）。
- [ ] `kuzu` C 扩展在内嵌解释器下正常加载（平台 wheel 正确，cp312-win_amd64）。
- [ ] `graphiti_core` 本地包经工作目录引入正常（无 setup 也能 import）。
- [ ] 把 `PythonServer/`（含 `python/` + 业务代码）复制到另一台**无 Python/uv** 的机器，`python.exe main.py` 同样跑通（打包方案 §10 验收项）。
- [ ] 行为与 `uv run python main.py` 一致（无 Key 启动监听 / `--auto-init` 初始化）。
- [ ] 开发环境声明 `.python-version = 3.12`，`uv pip compile` 按 3.12 解析（运行统一用内嵌 `python/python.exe`）。

## 7. 待确认问题

- [x] **Python 版本 3.12 vs 3.11**：打包方案 §10 原规划 3.12；开发 venv 原为 3.11。**2026-08-25 决策：统一为 3.12**——开发环境也用项目内内嵌 3.12 环境，不再维护两套。
- [x] **graphiti_core 处理**：纯 Python 本地包、无 C 扩展（已实测），不进 requirements，随业务代码放在 `PythonServer/`，靠工作目录引入。
- [x] **requirements 生成方式**：用 `uv pip compile` 导出完整依赖树（含间接依赖），而非手动抄 pyproject dependencies。
- [x] **依赖安装方式**：初版用 `--target` + runtime/ + PYTHONPATH，实测 protobuf 命名空间包 `.pth` 失效；改为直接装进解释器 site-packages（自包含）。

---

*本文档由 Cursor Agent 根据 `DevDocs/feature-design/打包方案.md` §3.2/§5.3/§10 生成，确认前请勿直接据此改代码。*
