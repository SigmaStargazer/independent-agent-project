# PRD — v0.23.3b Python 端 exe 化 + Unity 托管进程

> **状态**：已实现（2026-08-27 Python 侧开发完成并自测通过；Unity 侧待联调）
> **对应需求**：`DevDocs/feature-design/打包方案.md`（§3.4 Unity 托管 Python 进程、§3.5 启动器选型、§3.6 源码保护、§4 改造点清单）
> **引用调研**：`DevDocs/v0.23.3b/验证报告.md`（PyInstaller 可行性实测）
> **最后更新**：2026-08-27

---

## 1. 背景与目标

### 1.1 现状

- **开发/运行**：Python 端 `main.py` 是常驻 TCP 服务，目前**由开发者手动启动**（`uv run python main.py` 或内嵌 `python/python.exe main.py`），Unity 再连接。
- **v0.23.3a** 用「Python 内嵌运行时」（`python-build-standalone` + 依赖装进 `python/`）解决「玩家机器无 Python/uv」，但仍存在：交付形态含整个 Python 环境、业务代码明文暴露、需手动启动。
- **v0.23.3b 调研（已验证）**：PyInstaller 可将 `main.py` 打成 **exe**（onedir 108MB / 冷启动到监听 2.6s），全链路可行（启动→加载依赖→监听端口→写端口→TCP 可连）；`--noconsole` 可**无窗口**运行（日志仍落盘）。

### 1.2 目标

1. **Python 端 exe 化**：把 `main.py` + 全部依赖 + 业务代码打成自包含 `agent_server.exe`，玩家机器**无需安装 Python/uv、不提供整个 Python 环境**。
2. **Unity 托管进程**：玩家「点开 Unity 即用」——Unity 自动拉起 exe（无窗口）、等端口就绪、退出时清理进程，全程**无命令行窗口**（消除玩家误关风险）。
3. **开发/运行环境解耦**：开发期继续用 uv + `.venv`（3.12）；运行/交付用 exe。
4. **源码保护**：业务代码编译进 PyInstaller PYZ 归档（非明文），源码保护级别从「明文 .py」提升到「中」。

### 1.3 非目标

- **不做** API Key 注入/初始化时序改造（v0.23.0a/b 已定，沿用现状）。
- **不做** 强源码保护（Cython/PyArmor），本期用 PYZ 归档即可（`打包方案.md` §3.6 分阶段结论）。
- **不做** macOS 运行时（另行立项）。
- **不做** 一键安装包/多版本管理（后续）。
- **不做** 玩家 API Key 加密存储改造（`打包方案.md` §4.2，本期沿用现有配置读取）。

---

## 2. 范围

### 2.1 本期包含

| # | 项 | 说明 |
|---|-----|------|
| S1 | **Python 路径重定位** | `main.py` 在打包态下定位 `Data/Config`、`Lib/proto`、`db/`、`logs/`、`.env` 的逻辑，兼容开发态（venv/python.exe）与打包态（exe） |
| S2 | **打包脚本** | 固化为 `Tools/build_python_exe.cmd`：PyInstaller onedir + `--noconsole` + `--collect-all graphiti_core`，输出 `agent_server.exe`，并复制外部资源 |
| S3 | **Unity PythonProcessLauncher** | `BootstrapEntry` 启动时拉起 `agent_server.exe`（无窗口、隐藏），复用已有 `EnsureConnectedAsync` 等端口就绪 |
| S4 | **Unity 退出清理** | `OnApplicationQuit` 时结束 Python 子进程，避免残留 |
| S5 | **数据目录外置** | `db/`、`logs/` 等运行时写文件定位到玩家可写目录（打包态），避免只读位置失败 |
| S6 | **进程异常兜底** | Python 启动失败/崩溃时 Unity 侧提示与重试 |

### 2.2 本期不包含

- 玩家 API Key 加密（`打包方案.md` §4.2，后续）。
- 一键安装包/自动更新。
- 强源码保护（Cython/PyArmor）。
- Unity 场景、玩法逻辑改动（除 Bootstrap/退出生命周期外）。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家 | 双击游戏图标 | Unity 自动拉起 Python（无窗口），等就绪后进 Title，全程无命令行窗口、无需装 Python |
| 玩家 | 游戏中/退出游戏 | Python 进程随游戏退出被清理，不残留占端口/内存 |
| 玩家 | 低配机/慢速启动 | 有等待提示，不出现「连接失败」报错（等端口轮询 30s 已存在） |
| 开发者 | 开发期调试 | 仍用 `uv run python main.py`，不受打包改造影响；`#if !UNITY_EDITOR` 隔离 |
| 打包机 | 出包 | 跑 `build_python_exe.cmd` 产出 exe + 外部资源，随游戏分发 |

---

## 4. 功能需求

### 4.1 Python 路径重定位（S1）

**现状**：`main.py` 用 `os.path.dirname(__file__)` 定位 `PROJECT_ROOT`（第 28 行），打包后 `__file__` 指向 exe 内临时目录，路径失效（已验证：exe 启动后找不到 `Data/Config/agent_server_port.txt`）。

**需求**：
- 新增一个「运行根目录」解析逻辑，**打包态**返回 exe 同级目录（或约定数据目录），**开发态**返回项目根。
- 判定方式：优先用环境变量（如 `AGENT_SERVER_ROOT`）显式指定；否则用 `sys.frozen`（PyInstaller 标志）判断打包态 → exe 同级；否则回退 `__file__` 推导。
- 覆盖路径：`Data/Config/agent_server_port.txt`、`Lib/proto`（sys.path）、`db/`、`logs/`、`.env`/`api_config.json`。
- **不破坏开发态**：编辑器/venv 下行为与现在完全一致。

### 4.2 打包脚本（S2）

- 新增 `Tools/build_python_exe.cmd`：
  - 用 `.venv` 的 pyinstaller，`--onedir --noconsole --name agent_server --collect-all graphiti_core --paths <Src/PythonServer>` 打包 `main.py`。
  - 复制外部资源：`Data/`、`Lib/`、`config/`（idle_wakeup 等）、`db/`（默认技能）、`.env`（如有）到 exe 同级。
  - 输出到 `Build/PythonServer/`（或约定目录），随 Unity 包分发。
  - 校验：打包后 `agent_server.exe` 能启动、写端口文件。

### 4.3 Unity PythonProcessLauncher（S3）

- 新增 `PythonProcessLauncher`（或并入 BootstrapEntry）：
  - `ProcessStartInfo`：`FileName = <游戏根>/PythonServer/agent_server.exe`，`UseShellExecute=false`，`CreateNoWindow=true`，`WindowStyle=Hidden`。
  - 编辑器下（`#if UNITY_EDITOR`）不拉起，仍由开发者手动起 Python。
  - 启动后调用已有 `EnsureConnectedAsync()` 等端口就绪。
  - 记录 `Process` 引用供退出清理。

### 4.4 Unity 退出清理（S4）

- **返回 Title 场景不 Kill**：返回 Title 仅 `SceneStop`（Agent 被打断但进程仍存活），Python 端 `astart_all` 还可继续跑。**绝不能在返回 Title 时 Kill**，否则玩家点「继续游戏」时 Python 已不在。
- **Unity 进程退出时清理**：`BootstrapEntry`/全局单例实现 `OnApplicationQuit`（进程级回调，正常/异常/强制退出都会触发）。流程：先发 `CloseRequest` 让 Python 收尾（停止 Agent、flush 记忆、释放 Kuzu 文件锁），等有限短超时（≤2s），若进程仍存活则 `Kill()` 兜底。
- 兜底：进程异常退出（非主动 Kill）时，Unity 能感知并提示/重试。

### 4.5 数据目录（S5）

**已定：zip 分发 + 数据放游戏根（方案 B，单一运行根）**

- 分发方式为 **zip 解压即玩**（`打包方案.md` §1.2/§11.4/§11.6）：玩家下载 zip → 解压到**自选可写目录** → 双击 exe。非 installer 安装模式，无「强制装到 Program Files」场景。
- **打包态**：`db/`（Kuzu 图库）、`logs/`、`Data/Config/`（端口文件、api_config.json）全部放 **exe 同级游戏根**，单一直观，与 `打包方案.md` §3.1/§8 已定结构一致。游戏目录整个可写。
- **开发态**：保持项目内 `db/`、`logs/`（现状不变）。
- **只读风险兜底**：打包态启动时自检 `db/` 可写（试写临时文件），不可写则提示「请解压到可写目录（勿放 Program Files）」，避免静默崩溃。
- 放弃 `%LOCALAPPDATA%` 外置的理由：zip 无安装/卸载程序，数据放游戏根可随目录整体迁移、删除即清理；外置反而造成「数据与游戏分离、卸载残留」。
- Python 侧通过「运行根目录」统一解析；Unity 侧端口发现已支持打包路径（`PortConfigDir()` 已实现）。

### 4.6 进程异常兜底（S6）

- Python 启动失败（如端口文件 30s 超时）→ Unity 提示「Python 服务启动失败」并给出重试/退出选项。
- Python 运行中崩溃 → Unity 连接断开时提示并可选重启。
- **多开互斥**：禁止同时存在两个 Python 实例。Unity 启动前 TCP 探测端口（已通则复用、不再拉起新 exe）；`agent_server.exe` 启动时绑定端口失败（已被占）立即退出。双重防线。

---

## 5. 非功能需求

| 项 | 要求 |
|----|------|
| **无窗口** | exe 以 `--noconsole` 打包，Unity `CreateNoWindow=true`，全程无命令行窗口 |
| **启动性能** | onedir 冷启动到监听 ≤ 3s（已验证 2.6s），Unity 等端口轮询 30s 兜底 |
| **体积** | onedir 约 108MB，可接受 |
| **兼容性** | Python 3.12 统一（`.venv` 与 exe 一致）；中文路径下运行正常（需实测） |
| **可观测性** | `logs/console/` 日志落盘（noconsole 下不丢）；Unity 侧关键状态可透出 |
| **不破坏开发** | 开发期 `uv run python main.py` 行为不变；`#if !UNITY_EDITOR` 隔离托管逻辑 |

---

## 6. 验收标准

- [ ] `Tools/build_python_exe.cmd` 一键产出 `agent_server.exe`（onedir + noconsole）及外部资源。
- [ ] 打包态 exe 无窗口启动，能监听端口、写端口文件、响应最小请求（复用验证 3 方法）。
- [ ] 打包态下 `db/`、`logs/` 写入玩家可写目录（非 Program Files 只读位置）。
- [ ] Unity 打包版：启动即自动拉起 Python（无窗口），`EnsureConnectedAsync` 等端口就绪后进 Title。
- [ ] Unity 退出时 Python 进程被清理，无残留（端口/内存释放）。
- [ ] 开发态（编辑器 + `uv run python main.py`）行为与现状完全一致，未受影响。
- [ ] 中文路径下打包版 exe 正常运行（E9 验证）。

---

## 7. 已确认决策

- [x] **数据目录位置**：**方案 B —— 数据放游戏根**（zip 分发 + 单一可写游戏根）。`db/`、`logs/`、`Data/Config/` 全放 exe 同级；放弃 `%LOCALAPPDATA%` 外置；加启动时 `db/` 可写自检兜底。理由见 §4.5。
- [x] **Python 进程优雅关闭**：**返回 Title 不 Kill**（仅 SceneStop）；**Unity 进程退出（OnApplicationQuit，正常/异常/强制均触发）时先发 `CloseRequest` 优雅关闭，≤2s 超时，仍存活则 `Kill()` 兜底**。Kuzu 文件锁敏感，见 `Doc/kuzu被文件锁时处理办法.md`。
- [x] **exe 与 Unity 的目录约定**：`agent_server.exe` 放游戏根 `PythonServer/` 子目录（onedir 结构，`_internal/` 随附）；外部只读资源 `Data/`、`Lib/`、`config/` 复制到 `PythonServer/` 同级（exe 所在游戏根）；`db/`、`logs/` 由运行时生成于 `PythonServer/`。
- [x] **多开互斥**（E7）：**处理**。Unity 启动前 TCP 探测端口（已通则复用、不拉起新 exe）+ `agent_server.exe` 绑定端口失败立即退出，双重防线。Mutex 暂不做。

---

*本文档由 Cursor Agent 根据调研结论 + `打包方案.md` 生成；确认前请勿据此改代码。*
