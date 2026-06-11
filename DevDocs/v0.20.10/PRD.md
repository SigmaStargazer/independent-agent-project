# PRD — v0.20.10 WorldEventLog 世界事件日志

> **状态**：已确认  
> **对应需求**：`requirements/WorldEventLog 系统需求文档.docx`  
> **最后更新**：2026-06-10

---

## 1. 背景与目标

当前 Agent 具备 **Observe**（一次性观察）、**MonitorTarget**（深度持续观察，最多 3 个目标）、**Timer**（定时反馈）三类观察能力。MonitorTarget 能为单个目标保留较长历史（每目标最多 20 条），但存在明显短板：

- 多目标记录**按目标割裂**，Agent 难以还原跨对象的时间先后与因果链；
- 必须先选定目标才能观察，容易**错过尚未意识到应关注对象**的关键变化；
- 若仅用全局日志替代 MonitorTarget，则单目标深度研究能力不足。

**本期目标**：在**保留 MonitorTarget 不变**的前提下，新增 **WorldEventLog** 全局事件时间线，与 MonitorTarget 形成「广度 + 深度」互补的观察体系。

---

## 2. 范围

### 2.1 本期包含

- 在 Unity `AIPlayer` 内维护滚动世界事件队列（容量 100 条）。
- 自动监听场景中所有 `SceneObj` 的状态变化、出现、消失事件。
- 记录 `AIPlayer` 自身状态变化（用于重建 Agent 相关因果链）。
- 新增 Agent 工具 `GetWorldEventLog()`，按约定格式返回全局事件记录。
- 完整走通 Python ↔ Unity 工具 RPC 链路（协议、base_tools、agent 注册）。

### 2.2 本期不包含

- 修改或移除现有 `MonitorTarget` / `GetMonitorRecords` 行为。
- 将 WorldEventLog 自动注入 `Observe`、Feedback、Timer 等默认环境文本。
- WorldEventLog 的持久化、跨场景存档、UI 展示。
- 基于 WorldEventLog 的自动因果推理或 LLM 侧分析。
- 容量、过滤规则的运行时可配置（本期固定 `MaxWorldEvents = 100`）。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| AI Agent | 同时观察平台、电梯、按钮，需判断「按钮按下 → 电梯上升 → 平台变向」 | 通过 `GetWorldEventLog` 按统一时间线查看跨对象事件顺序 |
| AI Agent | 尚未 Monitor 某按钮，但该按钮被按下触发连锁反应 | WorldEventLog 仍记录该变化，Agent 事后可查阅发现规律 |
| AI Agent | 长期研究单个平台运动周期 | 继续使用 `MonitorTarget` + `GetMonitorRecords` 获取该目标深度历史 |
| 开发者 | 调试场景机关联动 | 可通过工具输出验证事件是否按预期写入日志 |

---

## 4. 功能需求

### 4.1 数据结构（Unity）

新增 `WorldEventRecord`：

```csharp
public class WorldEventRecord
{
    public float Time;        // 记录时刻（Unity Time.time）
    public string ObjectName; // 对象名（SceneObj.Name 或 Agent Name）
    public string OldState;
    public string NewState;
    public string EventText;  // 完整记录正文（见 4.2）
}
```

`AIPlayer` 内维护 `Queue<WorldEventRecord> mWorldEventLog`，`MaxWorldEvents = 100`，超出时 `Dequeue()` 移除最旧记录。

### 4.2 事件来源与记录格式

**记录正文与 MonitorTarget 对齐**：每条事件不只存一行状态摘要，而是通过 `CreateMessageText(msg, includeObserveTagerts: false)` 写入**事件发生时刻**的完整上下文，包含：

- 事件摘要行（`msg`）
- `<你的状态>`（Agent 当时的状态、速度、动作序列等）
- `<当前场景>`
- `<环境>`（当时可见场景对象列表）

这样 Agent 查阅日志时，能还原「按钮按下时 Agent 在哪、周围还有什么」，便于跨对象因果推断。实现方式与 `MonitorTarget` 的 `runtime.Records.Enqueue(CreateMessageText(...))` 一致。

| 来源 | 触发 | `msg` 摘要行示例 |
|------|------|------------------|
| SceneObj 状态变化 | `OnStateChanged` | `[世界事件]对象:2. 按钮 状态:Idle -> Pressed` |
| SceneObj 出现 | `OnObjectEnabled`（**仅曾消失后再次出现时记录**；首次 OnEnable 的 Appearance 不写入） | 摘要行 + `[索引变化]` 段（见 4.2.1） |
| SceneObj 消失 | `OnObjectDisabled` | 摘要行 + `[索引变化]` 段（见 4.2.1） |
| AIPlayer 自身 | `ChangeState` 成功后 | `[世界事件]对象:小明 状态:Idle -> Move`（**不带编号**；Agent 不在 `GetSceneObjsExcluding` 列表内） |

#### 4.2.1 对象编号规则

- 编号取自 `SceneObjManager.Instance.GetSceneObjsExcluding(AIPlayer.gameObject)` 的**当下**列表下标，格式与 `RuntimeInfoRenderer` 一致：`{index}. {Name}`（0 起始，如 `2. 按钮`）。
- `SceneObj` 允许重名，**必须带编号**；Agent 自身事件**不带编号**。
- 若事件触发时物体已不在列表内（仅作兜底）：`{Name}(目前不在环境列表内)`。

#### 4.2.2 出现 / 消失时的索引变化说明

`Enable` / `Disable` 会改变 `mSceneObjs` 成员，导致 `GetSceneObjsExcluding` 内后续物体编号漂移。为保持 Agent 认知一致（**不引入 UUID 等机械化 ID**），出现/消失类事件须在 `msg` 中追加 **`[索引变化]`** 段，用文字说明本次变动对编号的影响：

**出现（Disappearance → Appearance）** 示例：

```
[世界事件]对象:4. 移动平台 状态:Disappearance -> Appearance

[索引变化]
新出现物体: 4. 移动平台（加入环境列表）
其余物体索引未变
```

（`Register` 当前为尾部 `Add`，新物体落在列表末尾，通常不影响既有编号。）

**消失（* → Disappearance）** 示例：

```
[世界事件]对象:2. 按钮 状态:Idle -> Disappearance

[索引变化]
消失物体: 2. 按钮（已从环境列表移除）
以下物体索引前移:
  原 3. 电梯 -> 现 2. 电梯
  原 4. 平台 -> 现 3. 平台
```

**实现前提**：`OnObjectDisabled` 须在 `UnRegister` **之前**触发（调整 `SceneObjBase.OnDisable` 顺序），否则消失瞬间已无法 `IndexOf` 该物体，也无法推算前移关系。

**已知取舍**：100 条 × 环境快照会使单次 `GetWorldEventLog` 返回体较大；与 MonitorTarget 深度记录的设计哲学一致，Agent 应**按需**调用，不宜每轮推理都拉全量。

#### 4.2.3 初始 Appearance 过滤

场景加载时，所有 SceneObj 首次 `OnEnable` 均触发 `OnObjectEnabled(Disappearance → Appearance)`，但这属于 Unity 初始化行为而非真实「消失后重现」。若全部写入，100 条容量会被瞬间填满。

**过滤规则**：以 AIPlayer 启用后的**第一帧**为分界线——同一帧内的所有 Appearance 都是初始化，跳过不写；下一帧起所有事件正常写入。

**实现**：`mWorldEventLogReady` 布尔标志 + `StartCoroutine(EnableWorldEventLogNextFrame())`（`yield return null` 后置 true）。Handler 内检查 `!mWorldEventLogReady` 则跳过。`OnDisable` / `ClearWorldEventLog` 时重置为 false。

此方案覆盖以下全部场景：

| 场景 | 帧位置 | `mWorldEventLogReady` | 行为 |
|------|--------|----------------------|------|
| 场景加载时 SceneObj 首次 OnEnable | 第 0 帧 | false | 跳过 ✅ |
| AIPlayer 先启用、SceneObj 同帧补注册 | 第 0 帧 | false | 跳过 ✅ |
| 新角色/物体运行中动态入场 | 第 N 帧 (N≥1) | true | 写入 ✅ |
| 运行中对象 Disable / 重新 Enable | 第 N 帧 (N≥1) | true | 写入 ✅ |

### 4.3 自动注册机制

- `AIPlayer.OnEnable` 时订阅 `SceneObjManager.OnSceneObjCreated`。
- 收到新对象时，为其注册 `OnStateChanged` / `OnObjectEnabled` / `OnObjectDisabled` 监听。
- 初始化时遍历 `SceneObjManager.GetSceneObjsExcluding(AIPlayer)`，为已存在对象补注册。
- 保证「先 AIPlayer 后 SceneObj」与「先 SceneObj 后 AIPlayer」两种创建顺序均有效。

### 4.4 Agent 工具 `GetWorldEventLog`

- 工具名：`get_world_event_log_cmd`（Python）/ Unity 处理名 `GetWorldEventLog`。
- 类型：同步 RPC（与 `get_monitor_records_cmd` 相同，经 `SendToolResultMessage` 返回）。
- 返回格式（示意）：

```
[世界事件记录]
总记录数: 87

==========事件1==========
时间: 45.2秒前
[世界事件]对象:2. 按钮 状态:Idle -> Pressed

<你的状态>
...
</你的状态>

<当前场景>
...
</当前场景>

<环境>
...
</环境>

==========事件2==========
时间: 43.1秒前
...
```

- 列表按**时间正序**展示（**事件1 = 最早**，事件编号递增到最新），符合时间线阅读习惯。
- 每条先给「X秒前」相对时间头，再输出 `EventText` 全文（已含摘要 + 状态 + 场景 + 环境）。
- 「X秒前」相对当前调用时刻计算，与 MonitorTarget 使用相同的 `Time.time` 语义。

### 4.5 与 MonitorTarget 的关系

- **并存，不替代**：MonitorTarget 继续负责单目标深度观察；WorldEventLog 负责全局广度时间线。
- 同一 SceneObj 事件可能**同时**写入 MonitorTarget 记录（若已被观察）和 WorldEventLog，属预期行为。

---

## 5. 非功能需求

- **性能**：仅注册事件委托，不做每帧轮询；队列固定上限，内存有界。
- **生命周期**：`AIPlayer.OnDisable` 时取消所有 SceneObj 订阅，并**清空** `mWorldEventLog`（与现有 `mTimerRuntimes.Clear()` 一致；`SceneStop` 后不应残留上一场景事件）。
- **编码**：所有新增/修改源文件 UTF-8。
- **协议**：仅改 `Tools/message.proto` 并走代码生成流程，禁止手改 `message_pb2.py` / `message.cs`。

---

## 6. 验收标准

- [ ] 场景中任意 `SceneObj` 状态变化、出现、消失均自动写入 WorldEventLog，无需 Agent 调用 MonitorTarget。
- [ ] `AIPlayer` 状态变化（如 Idle → Move）写入 WorldEventLog，对象名为 Agent 名称且**不带列表编号**。
- [ ] SceneObj 事件摘要含 `{index}. {Name}`；出现/消失事件含 `[索引变化]` 说明。
- [ ] 日志超过 100 条后，最旧记录被移除，总数不超过 100。
- [ ] Agent 调用 `GetWorldEventLog` 可拿到按时间正序（旧→新）格式化的完整可读文本，每条含当时环境快照。
- [ ] AIPlayer 先于/后于 SceneObj 创建，均能正确注册并记录事件。
- [ ] `MonitorTarget` / `GetMonitorRecords` 行为与改动前一致。
- [ ] Python 工具已注册到 `agent_interuptible.tools`，端到端联调可用。

---

## 7. 确认记录

| 议题 | 结论 |
|------|------|
| 返回顺序 | **旧→新**正序，事件1 为最早记录 |
| Agent 自身对象名 | 使用 `Name`（如「小明」） |
| 首次 Appearance | **写入** WorldEventLog |
| 记录正文 | 使用 `CreateMessageText(..., includeObserveTagerts: false)`，与 MonitorTarget 一致 |
| OnDisable | **清空**队列并取消订阅 |
| 对象编号 | SceneObj 用 `GetSceneObjsExcluding` 下标；Agent 自身不带编号 |
| 出现/消失 | 追加 `[索引变化]` 段；`OnObjectDisabled` 先于 `UnRegister` |

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
