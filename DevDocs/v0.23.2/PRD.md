# PRD — v0.23.2 Unity 端口路径与连接重试

> **状态**：已实现
> **对应需求**：`DevDocs/feature-design/打包方案.md`（§3.3 端口发现机制改造、§10 版本规划 v0.23.0「Unity 端口路径与连接重试」）
> **引用方案**：`DevDocs/feature-design/打包方案.md`（§3.3 方案 A、§10 风险控制中「连接重试需异步化」）
> **最后更新**：2026-08-24

---

## 1. 背景与目标

打包方案（`DevDocs/feature-design/打包方案.md`）把「Unity 端口路径与连接重试」定为打包改造的第一个版本（原规划 v0.23.0，因 v0.23 提前做了 Title API 配置/生命周期优化，本版本顺延为 v0.23.2 立项）。

**现状两个问题**：

1. **打包后端口路径失效**：`AgentService.GetPort()` 用 `Directory.GetParent(Application.dataPath).Parent`（编辑器下 = 上两级 = `Src/`）。打包后 `Application.dataPath = <游戏根>/<产品名>_Data`，`Parent` 是游戏根，再 `Parent` 是**游戏根的上级**——端口文件被定位到错误目录，抛 `FileNotFoundException` 后静默回退 8000。
2. **连接失败不回退即卡死/错误**：`ConnectToServer()` 连不上（端口文件未生成、Python 未就绪）时直接回退 8000 重连，`ClientBase.DoConnect()` 用 `BeginConnect + WaitOne(10000)` **同步阻塞主线程** 10 秒，且多次重试会连续卡顿（打包方案 §10 已指出：3 次重试卡顿 30 秒）。打包后 Python 由 Unity 子进程拉起，存在明显启动延迟，Unity 必须先「等端口文件/连接就绪」再进 Title/游戏，而不是连失败就回退。

**本版本目标**：

1. Unity 端口查找路径按 `#if UNITY_EDITOR` 区分，打包后指向游戏根 `Data/Config/`。
2. 连接改为**异步轮询等待**（等端口文件就绪 + 等连接成功），超时才报错，不再静默回退 8000。
3. **不碰 Python、不碰自动拉起 Python 进程**（后者属 v0.23.3）；本版本 Python 仍由开发者手动启动。
4. **编辑器行为保持现状**（读 `Src/Data/Config/agent_server_port.txt`），不影响开发工作流。

## 2. 范围

### 2.1 本期包含

- `AgentService.GetPort()`：`#if UNITY_EDITOR` 拆分，打包分支用 `Application.dataPath` 的 `Parent`（游戏根）→ `Data/Config/agent_server_port.txt`。
- `AgentService.ConnectToServer()`：改造为**异步等待**——先等端口文件就绪（存在且非 0 / 合法端口），再发起连接，连接失败按间隔重试，达到上限（或超时）才报错。
- 新增 `AgentService.EnsureConnectedAsync()`（异步等待连接就绪），供进游戏前/发消息前等待连接；超时抛异常。
- `JsonConfigIO.ConfigDir()`（v0.23.0 已抽象配置目录）同步加 `#if UNITY_EDITOR` 分支，保证打包后 `api_config.json` / `agent_server_port.txt` 落到同一目录体系。
- 文档：更新 `场景绑定指引.md`（若有受影响项）。

### 2.2 本期不包含

- **Python 侧任何改动**（`main.py` / `servers.py` / 端口文件路径）——另见后续版本。
- **Unity 自动拉起 Python 子进程**（`PythonProcessLauncher`，打包方案 v0.23.2，本版本之后）。
- **API Key 注入/初始化时序**（v0.23.0a/b 已做）。
- 固定端口策略（打包方案 §3.7 已定：保留随机端口）。
- 编辑器下连接失败时的行为变化（编辑器保持现状逻辑）。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 开发者 | 编辑器下 Play，Python 手动启动 | 仍读 `Src/Data/Config/agent_server_port.txt`，行为不变 |
| 玩家（打包版） | 双击 exe，Python 由子进程（后续版本）拉起，启动有延迟 | Unity 轮询等待端口文件/连接就绪，Python 就绪后连上，不再连失败回退 8000 |
| 玩家/开发者 | Python 启动失败或端口文件缺失 | Unity 等待一段时间后明确报错（而非静默回退 8000 / 阻塞卡顿） |
| 玩家（打包版） | 首次进游戏（InitializeStep 触发连接） | 异步等待连接成功后再继续 Flow，不卡死主线程 |

## 4. 功能需求

### 4.1 端口查找路径（打包适配）

`AgentService.GetPort()` 拆分：

```csharp
#if UNITY_EDITOR
    // 编辑器：上两级 = Src/，端口文件在 Src/Data/Config/（保持现状）
    string projectRoot = Directory.GetParent(Application.dataPath)?.Parent?.FullName;
#else
    // 打包：上一级 = 游戏根，端口文件在 <游戏根>/Data/Config/
    string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
#endif
    string filePath = Path.Combine(projectRoot, "Data", "Config", "agent_server_port.txt");
```

- 打包后端口文件落在 `<游戏根>/Data/Config/agent_server_port.txt`，与打包方案 §3.1 目录结构一致。
- 读不到文件 / 内容非法时**不再返回 8000**，改为抛异常（由上层等待/重试逻辑处理）。

### 4.2 异步连接等待与重试

新增 `EnsureConnectedAsync()`（异步等待连接就绪）：

- **阶段一：等端口文件就绪**——`GetPort()` 抛异常（文件不存在 / 非 0 / 非法）时，按固定间隔（如 0.5s）重试读取，直到读成功或超时。
- **阶段二：等连接就绪**——发起连接后，等待 `AgentClient.Instance.Connected` 为 true；连接失败/断开时按间隔重试，直到连上或超时。
- **超时**：总超时（建议 30s，可配常量），超时抛异常并给出明确错误信息（区分「端口文件未生成」与「连接失败」）。
- **异步实现**：用 `UniTask` 协程化（项目已用 Cysharp.Threading.Tasks），**避免复用 `ClientBase.DoConnect()` 的同步阻塞路径**（打包方案 §10 明确要求）。

`ConnectToServer()` 改造：

- 保留现有「未连接则连接」的触发语义，但改为**非阻塞启动连接**（`AgentClient.Connect()` 内部已是异步 BeginConnect），不再在调用处同步等待。
- 连接结果的等待统一收敛到 `EnsureConnectedAsync()`。

### 4.3 进游戏前等待连接（复用现有 Flow）

- 现有首次连接由 `InitializeStep → InitAsync → SendInit → ConnectToServer` 触发。
- 本版本在 `InitializeStep.Execute()` 开头增加 `await AgentServiceAsyncExtensions.EnsureConnectedAsync()`，确保 Flow 继续前连接已就绪；连接超时抛异常 → `FlowExecutor` 按 `FailPolicy` 报错回 Title。
- `AgentServiceAsyncExtensions` 新增 `EnsureConnectedAsync()`（UniTask 封装）。

## 5. 非功能需求

- **不阻塞主线程**：等待逻辑全部走 `UniTask` 协程，不调用 `WaitOne`/`Thread.Sleep` 同步等待。
- **编辑器兼容**：端口路径用 `#if UNITY_EDITOR` 隔离；编辑器下连接失败行为与现状等价（本版本不引入编辑器行为变更）。
- **超时可配置**：等待间隔 / 总超时用常量集中定义，便于调参。
- **UTF-8**：所有改动文件按 UTF-8（`.cs` 用 UTF-8 无 BOM），见 `.cursor/rules/file-encoding.mdc`。
- **改动面最小**：只动 Unity 侧连接相关代码，不碰 Python、不碰协议、不碰 Flow 结构。

## 6. 验收标准

> 详细操作步骤见 `solution.md` §6「测试建议（验收方法）」，含用例编号 A1~A4（编辑器）、B0~B3（打包）、前置条件、步骤、预期、判定依据。以下为验收要点。

- [ ] 编辑器下：`GetPort()` 仍读 `Src/Data/Config/agent_server_port.txt`；Python 手动启动后 Play 行为不变（A1）。
- [ ] 本地 Unity Build（Windows Standalone）后，手动起 Python、在 `<游戏根>/Data/Config/` 放端口文件，打包版 Unity 能读到端口并连上 Python、进游戏（B1）。
- [ ] 故意不启动 Python / 不放端口文件：Unity 进入等待，超时后**明确报错**（而非立即回退 8000），且不卡主线程（A2 / B3）。
- [ ] Python 启动慢（如延迟数秒建库）：Unity 轮询等待期间不阻塞主线程（无卡顿），Python 就绪后自动连上，无需重启/重试（A3）。
- [ ] 端口文件为 0 / 非法内容时：等待后报「端口文件非法」类错误，不误连（A4）。
- [ ] 进游戏 Flow（InitializeStep）在连接就绪前不继续执行后续步骤；连接失败时按 FailPolicy 报错回 Title。
- [ ] 打包版 `api_config.json` 与 `agent_server_port.txt` 经 `JsonConfigIO.ConfigDir()` / `GetPort()` 落在同一 `Data/Config/` 目录（B2）。

## 7. 待确认问题

- [x] **连接等待归属**：等待逻辑收敛到 `AgentService.EnsureConnectedAsync()`，由 `InitializeStep` 在 Flow 开头 await（打包方案 §10 要求异步化，已定）。
- [x] **超时与间隔**：建议总超时 30s、重试间隔 0.5s（可调常量），打包后 Python 子进程冷启动（内嵌解释器 + Kuzu 建库）数秒级，30s 足够。
- [x] **GetPort 失败语义**：不再回退 8000，抛异常由等待逻辑处理（与打包方案「不再静默回退」一致）。
- [x] **`JsonConfigIO` 同步改造**：配置目录与端口文件必须同一规则，故本期一并 `#if UNITY_EDITOR` 拆分。

---

*本文档由 Cursor Agent 根据 `DevDocs/feature-design/打包方案.md` §3.3/§10 生成，确认前请勿直接据此改代码。*
