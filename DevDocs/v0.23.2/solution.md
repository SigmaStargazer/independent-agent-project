# 技术方案 — v0.23.2 Unity 端口路径与连接重试

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **引用方案**：`DevDocs/feature-design/打包方案.md`（§3.3 方案 A、§10 v0.23.0 风险控制）
> **最后更新**：2026-08-24

---

## 1. 方案概述

Unity 侧两个问题的纯客户端修复：

1. **端口路径**：`GetPort()` 用 `#if UNITY_EDITOR` 拆分——编辑器保持上两级（`Src/Data/Config`），打包用 `Application.dataPath` 上一级（游戏根）`Data/Config`。
2. **连接重试**：新增 `AgentService.EnsureConnectedAsync()`（UniTask 协程）：先等端口文件就绪（存在且合法端口），再等 TCP 连接就绪，按固定间隔重试，总超时（30s）才抛异常；不再静默回退 8000。`InitializeStep` 开头 await 它，保证进游戏 Flow 在连接就绪后才继续。

不改 Python、不改协议、不改 Flow 结构；编辑器行为不变。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Unity | `Services/AgentService.cs` | 改：`GetPort()` 路径分支；`ConnectToServer()` 去回退；新增 `EnsureConnectedAsync()` / 连接等待 |
| Unity | `Services/AgentServiceAsyncExtensions.cs` | 新增：`EnsureConnectedAsync()`（UniTask 封装） |
| Unity | `Services/JsonConfigIO.cs` | 改：`ConfigDir()` 加 `#if UNITY_EDITOR` 分支（配置目录与端口文件同一规则） |
| Unity | `ViewController/Gameplay/GameFlow/Steps/InitializeStep.cs` | 改：开头 `await EnsureConnectedAsync()` |
| 协议 | `Tools/message.proto` | 无 |
| Python | 无 | - |
| 文档 | `DevDocs/v0.23.2/` | 新增 PRD/solution |

## 3. 详细设计

### 3.1 端口路径（`AgentService.GetPort()`）

```csharp
// 端口文件目录：统一规则（与 JsonConfigIO.ConfigDir() 一致）
static string PortConfigDir()
{
#if UNITY_EDITOR
    // 编辑器：Src/Data/Config（上两级）
    string root = Directory.GetParent(Application.dataPath)?.Parent?.FullName;
#else
    // 打包：<游戏根>/Data/Config（上一级）
    string root = Directory.GetParent(Application.dataPath)?.FullName;
#endif
    if (string.IsNullOrEmpty(root))
        throw new DirectoryNotFoundException("无法定位端口文件目录（Application.dataPath 层级异常）。");
    return Path.Combine(root, "Data", "Config");
}

int GetPort()
{
    string filePath = Path.Combine(PortConfigDir(), "agent_server_port.txt");
    Debug.Log($"GetPort filePath: {filePath}");
    if (!File.Exists(filePath))
        throw new FileNotFoundException("端口文件不存在（Python 服务端未启动或尚未写入端口）。", filePath);
    string portStr = File.ReadAllText(filePath).Trim();
    if (string.IsNullOrEmpty(portStr))
        throw new InvalidDataException("端口文件内容为空（Python 尚未写入端口）。");
    int port = int.Parse(portStr);
    if (port <= 0)
        throw new InvalidDataException($"端口文件内容非法（port={port}）。");
    return port;
}
```

> **语义变化**：`GetPort()` 不再返回 8000 兜底；读不到/非法时抛异常，由 `EnsureConnectedAsync()` 的等待/重试逻辑处理。

### 3.2 异步连接等待（`AgentService`）

新增常量与字段：

```csharp
const int PORT_WAIT_INTERVAL_MS = 500;   // 轮询间隔
const int CONNECT_TIMEOUT_SEC = 30;      // 总超时
```

新增 `EnsureConnectedAsync()`（等待连接就绪，超时抛异常）：

```csharp
public async UniTask EnsureConnectedAsync()
{
    float deadline = Time.realtimeSinceStartup + CONNECT_TIMEOUT_SEC;

    // 阶段一：等端口文件就绪（存在且合法端口）
    int port;
    while (true)
    {
        try { port = GetPort(); break; }
        catch (Exception e)
        {
            if (Time.realtimeSinceStartup >= deadline)
                throw new TimeoutException($"等待端口文件超时（>{CONNECT_TIMEOUT_SEC}s）：{e.Message}");
            await UniTask.Delay(PORT_WAIT_INTERVAL_MS);
        }
    }

    // 阶段二：等连接就绪（重复连接直到 Connected 或超时）
    while (true)
    {
        if (AgentClient.Instance.Connected)
            return;
        if (Time.realtimeSinceStartup >= deadline)
            throw new TimeoutException($"连接 Python 服务端超时（>{CONNECT_TIMEOUT_SEC}s）：127.0.0.1:{port}");
        if (!this.connecting)
        {
            AgentClient.Instance.Init("127.0.0.1", port);
            AgentClient.Instance.Connect();   // 内部 BeginConnect 异步
        }
        await UniTask.Delay(PORT_WAIT_INTERVAL_MS);
    }
}
```

> 说明：
> - `AgentClient.Connect()` 内部已是异步 `BeginConnect`（`ClientBase.cs`），不会阻塞主线程；`EnsureConnectedAsync` 仅轮询 `Connected` 状态。
> - 不再调用 `ConnectToServer()` 里「失败回退 8000」的旧路径。

`ConnectToServer()` 改造（保持「未连接则发起连接」的触发语义，去掉回退）：

```csharp
public void ConnectToServer()
{
    if (this.connected || this.connecting)
        return;
    this.connecting = true;
    int port = GetPort();                       // 抛异常由调用方（EnsureConnectedAsync）处理
    AgentClient.Instance.Init("127.0.0.1", port);
    AgentClient.Instance.Connect();
}
```

> 现状所有 `SendXxx()` 在未连接时都会调 `ConnectToServer()`。本版本**不改这些发送方法的触发语义**（仍会在未连接时尝试发起连接），只是「连接是否成功」由 `EnsureConnectedAsync()` 统一等待判定。进游戏路径先 `await EnsureConnectedAsync()`，可避免 `SendInit` 在连接未就绪时把消息丢进 pending 队列。

### 3.3 `AgentServiceAsyncExtensions.EnsureConnectedAsync()`

```csharp
/// <summary>v0.23.2：等待 Python 服务端连接就绪（端口文件就绪 + TCP 连接成功），超时抛异常。</summary>
public static UniTask EnsureConnectedAsync()
{
    return AgentService.Instance.EnsureConnectedAsync();
}
```

### 3.4 `JsonConfigIO.ConfigDir()` 同步拆分

与 `GetPort()` 同一规则，保证 `api_config.json` 与 `agent_server_port.txt` 打包后同目录：

```csharp
public static string ConfigDir()
{
    DirectoryInfo assetsDir = new DirectoryInfo(Application.dataPath);
    DirectoryInfo projectRoot = assetsDir.Parent;
#if UNITY_EDITOR
    DirectoryInfo configRoot = projectRoot != null ? projectRoot.Parent : null;   // Src/
#else
    DirectoryInfo configRoot = projectRoot;                                       // 游戏根
#endif
    if (configRoot == null)
    {
        Debug.LogWarning("[JsonConfigIO] 无法定位配置目录（构建环境可能不支持外部路径）。");
        return Path.Combine(Application.persistentDataPath, "Data", "Config");
    }
    return Path.Combine(configRoot.FullName, "Data", "Config");
}
```

### 3.5 `InitializeStep` 开头等待连接

```csharp
public async UniTask Execute()
{
    await AgentServiceAsyncExtensions.EnsureConnectedAsync();   // v0.23.2：连接就绪后再初始化
    await AgentServiceAsyncExtensions.InitAsync();
}
```

> 连接超时抛异常 → `FlowExecutor.Execute` catch → 按 `FailPolicy.ReturnTitle` 回 Title 并 `TransitionUI.ShowError(e.Message)`。

## 4. 实现步骤

1. `AgentService.cs`：
   - `GetPort()` 加 `PortConfigDir()`（`#if UNITY_EDITOR` 分支），去掉 8000 回退（改抛异常）。
   - `ConnectToServer()` 去掉「回退 8000」相关逻辑。
   - 新增 `EnsureConnectedAsync()`（UniTask）+ 常量 `PORT_WAIT_INTERVAL_MS` / `CONNECT_TIMEOUT_SEC`。
2. `AgentServiceAsyncExtensions.cs`：新增 `EnsureConnectedAsync()`。
3. `JsonConfigIO.cs`：`ConfigDir()` 加 `#if UNITY_EDITOR` 分支。
4. `InitializeStep.cs`：`Execute()` 开头 `await AgentServiceAsyncExtensions.EnsureConnectedAsync()`。
5. 更新文档状态：PRD/solution 待确认 →（用户确认后）已确认。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `GetPort()` 改抛异常影响其他调用点 | 仅 `ConnectToServer()` 与 `EnsureConnectedAsync()` 调它；`ConnectToServer` 的调用方（各 `SendXxx`）已有 `connecting` 保护，异常由 `EnsureConnectedAsync` 捕获等待。 |
| 编辑器下连接失败行为变化 | 端口路径 `#if UNITY_EDITOR` 保持现状；`EnsureConnectedAsync` 超时在编辑器下同样适用（Python 未启动则超时报错，与「不再回退 8000」一致）。 |
| 主线程卡顿（打包方案 §10 已指出） | 等待全部走 `UniTask.Delay` 协程，不调用 `WaitOne`/`Thread.Sleep`；`ClientBase.DoConnect` 的同步 `WaitOne(10000)` 属既有路径，本版本不新增对它的依赖（连接状态仅轮询 `Connected`）。 |
| Python 冷启动超时 | 总超时 30s（内嵌解释器 + Kuzu 建库秒级足够）；常量可调。 |
| 连接事件时序（`OnGameServerConnect` 回调） | `EnsureConnectedAsync` 不依赖该回调，直接轮询 `AgentClient.Instance.Connected`，避免与 `AgentService.OnGameServerConnect` 的 `connected/connecting` 状态竞争。 |
| `api_config.json` 打包后路径 | `JsonConfigIO.ConfigDir()` 同规则拆分，两文件同目录；本版本仅改路径规则，实际打包验证留 v0.23.5。 |

## 6. 测试建议（验收方法）

> 分两个阶段：**阶段 A 编辑器回归**（不打包、Python 手动起）→ **阶段 B 打包验证**（Windows Standalone Build）。
> 每个用例给出：前置条件 → 步骤 → 预期 → 判定依据（对照 §3 实现细节）。
> 打包版「Unity 自动拉起 Python」属 v0.23.3，本期验证时 Python 仍手动启动。

### 6.1 前置准备（通用）

- Python 启动方式：`cd Src/PythonServer && uv run python main.py`，端口写入 `Src/Data/Config/agent_server_port.txt`。
- 打包产物（阶段 B 用）：Unity Build → Windows x64 → 输出到 `<游戏根>/`，得到 `<游戏根>/ShootingEditor2D.exe` + `<游戏根>/ShootingEditor2D_Data/`（`productName=ShootingEditor2D`，见 ProjectSettings.asset）。

### 6.2 阶段 A — 编辑器回归（不打包）

**A1. 正常路径（回归，不应被破坏）**

- 前置：Python 手动运行中，`Src/Data/Config/agent_server_port.txt` 内容为当前监听端口（非 0）。
- 步骤：
  1. Play → 进 Title → 配置 API Key（或已有配置）→ 点「新游戏」。
  2. 观察 Console 与 Python 端日志。
- 预期：进游戏 Flow 顺利执行（InitializeStep 通过 → CreateAgent / LoadAgent / SceneStart 正常），Python 端出现对应处理日志；Console 无「端口文件不存在」「回退 8000」「Cannot connect」类报错。
- 判定依据：`EnsureConnectedAsync` 阶段一读到端口文件、阶段二 `Connected=true` 后立即放行，与「已连上」路径等价，不应有任何行为差异。

**A2. 端口文件缺失 → 等待后明确报错（不静默回退 8000）**

- 前置：不启动 Python；删除 `Src/Data/Config/agent_server_port.txt`。
- 步骤：
  1. Play → 点「新游戏」。
  2. 观察加载界面 / Console。
- 预期：进入等待（加载界面停留，不立即报错）；约 30s（`CONNECT_TIMEOUT_SEC`）后 `FlowExecutor.ShowError` 显示「等待端口文件超时…」，回 Title；**全程无 10s 级主线程卡顿**（窗口可拖动、UI 不冻结）。
- 判定依据：阶段一持续 `GetPort` 抛 `FileNotFoundException` → 轮询 → 超时抛 `TimeoutException`。若看到回退 8000 或立即报错，则不符合。

**A3. Python 慢启动 → 轮询等待后自动连上（核心用例）**

- 前置：模拟慢启动（例如在 `main.py` 的 `start_console_logging` 前临时 `import time; time.sleep(8)` 再启动，或直接「Unity 已进入等待后才启动 Python」）。
- 步骤：
  1. Play → 点「新游戏」（此刻 Python 尚未就绪 / 端口文件未写）。
  2. 等待期间观察主线程是否卡顿；随后启动 Python（或 Python 完成初始化写出端口文件）。
  3. 观察是否自动连上并继续 Flow。
- 预期：Unity 等待期间不卡死；Python 就绪后（端口文件出现 → TCP 可连）`EnsureConnectedAsync` 放行，Flow 自动继续到场景加载，**玩家无需重启 / 重试**。
- 判定依据：阶段一读端口文件成功、阶段二 `Connected=true`，无人工干预即继续。

**A4. 端口文件为 0 / 非法（模拟 Python 崩溃中途）**

- 前置：手动创建 `Src/Data/Config/agent_server_port.txt`，内容为 `0`（或 `abc`）。
- 步骤：Play → 点「新游戏」。
- 预期：与 A2 类似，等待约 30s 后报「端口文件内容非法 / 为空」类错误，回 Title，不卡主线程。
- 判定依据：`GetPort` 对 `port <= 0` / `int.Parse` 失败抛异常。

### 6.3 阶段 B — 打包验证（Windows Standalone Build）

**B0. 准备打包产物**

- Build Windows x64 → `<游戏根>/ShootingEditor2D.exe` + `ShootingEditor2D_Data/`。
- 手动在 `<游戏根>/Data/Config/` 放 `agent_server_port.txt`（内容为手动起的 Python 端口）。
- 说明：本版本不验证「自动拉起 Python」（v0.23.3），Python 始终手动启动。

**B1. 打包版端口路径正确（核心：`#if !UNITY_EDITOR` 分支）**

- 前置：手动起 Python（端口写入 `Src/Data/Config/agent_server_port.txt`）→ 复制该文件到 `<游戏根>/Data/Config/agent_server_port.txt`。
- 步骤：双击 `ShootingEditor2D.exe` → 进 Title → 点「新游戏」。
- 预期：读到 `<游戏根>/Data/Config/agent_server_port.txt` 的端口、连上 Python、进游戏。
- 判定依据：
  - **验证点 1**：Console / 日志中 `GetPort filePath:` 打印的是 `<游戏根>/Data/Config/agent_server_port.txt`（而非 `Application.dataPath` 上两级）。
  - **验证点 2（反证）**：故意删除 `<游戏根>/Data/Config/agent_server_port.txt` → 应报「端口文件不存在」并超时，证明打包分支只认游戏根目录；若走旧路径（`Src/` 上两级）则会在别处找到文件或不报错。

**B2. `JsonConfigIO.ConfigDir()` 打包分支（与端口文件同目录）**

- 前置：同 B1；Title 中已保存过 API 配置（生成 `<游戏根>/Data/Config/api_config.json`）。
- 步骤：进游戏后检查两个文件所在位置。
- 预期：`api_config.json` 与 `agent_server_port.txt` 均在 `<游戏根>/Data/Config/` 下（同一目录）。
- 判定依据：`ConfigDir()` 打包分支指向游戏根；若 `api_config.json` 落到 `persistentDataPath` 或 `Src/`（与端口文件不同目录），说明拆分未生效。

**B3. 打包版超时报错（不带 Python）**

- 前置：不启动 Python；`<游戏根>/Data/Config/` 无端口文件。
- 步骤：双击 exe → 点「新游戏」。
- 预期：等待约 30s → 错误弹窗回 Title，**不卡主线程、不静默进游戏**。
- 判定依据：与 A2 一致，但在打包分支路径下验证。

### 6.4 判定汇总

| 用例 | 核心断言 |
|------|----------|
| A1 | 编辑器正常路径无回归 |
| A2 / B3 | 端口文件缺失 / 超时 → 明确报错回 Title，不静默回退 8000，不卡主线程 |
| A3 | Python 慢启动 → 异步等待后自动连上，无需重试 |
| A4 | 端口文件非法（0 / abc）→ 报错，不误连 |
| B1 | 打包分支端口文件路径 = `<游戏根>/Data/Config/` |
| B2 | `api_config.json` 与端口文件同目录 |

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-24 | 生成方案（PRD/solution），待用户确认 |
| 2026-08-24 | 用户确认，开始开发。Unity 侧改动完成：`AgentService.GetPort()` 加 `PortConfigDir()`（`#if UNITY_EDITOR` 拆分 + 去 8000 回退改抛异常）、`ConnectToServer()` 去回退、新增 `EnsureConnectedAsync()`（UniTask 协程：等端口文件就绪 + 轮询 `Connected`）；`AgentServiceAsyncExtensions` 新增 `EnsureConnectedAsync()`；`JsonConfigIO.ConfigDir()` 加 `#if UNITY_EDITOR` 分支；`InitializeStep.Execute()` 开头 `await EnsureConnectedAsync()`。lint 通过。待 Unity 联调验收（见 §6 用例 A1~A4 / B0~B3）。 |
| 2026-08-24 | 打包编译修复（Build 报 8 个 CS0234）：删除历史遗留的坏 `using UnityEditor.*` 导入——`AgentService.cs`（`Experimental.GraphView`/`MemoryProfiler`/`PackageManager`/`VersionControl`）、`ActionSequenceRuntime.cs`/`RuntimeInfoRenderer.cs`/`AIPlayer.cs`（`UnityEditor.U2D.Path.GUIFramework`）、`AIPlayer.cs`（`Unity.VisualScripting`）。这些在打包（Player）编译时命名空间不存在导致 Build 失败，编辑器 Play 不报错；`SceneObjAnimator.cs` 的 `UnityEditor.Animations.AnimatorController` 已确认被 `#if UNITY_EDITOR` 保护无需改。lint 通过，待重新 Build 验证。 |
| 2026-08-24 | 联调验收通过：Unity 编辑器内直接运行（读 `Src/Data/Config`，A1/A2/A3/A4）与打包客户端（读 `<构建根>/Data/Config`，B1）均能连接到 Python 端。打包分支 `GetPort filePath:` 指向 `<构建根>/Data/Config/agent_server_port.txt` 生效。Build 编译错误已清零，重新 Build 成功。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
