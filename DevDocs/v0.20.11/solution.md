# 技术方案 — v0.20.11 MonitorTarget / FollowTarget 目标消失异常停止

> **状态**：待确认
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-06-10

---

## 1. 方案概述

在 Unity `AIPlayer` 中，当被 Monitor / Follow 的目标从场景消失时，自动结束对应的长时任务、清理运行时状态与事件监听，并通过 `SendFeedbackToAgent` 通知 Agent（Feedback 本身即打断推理，无需额外指定 `forceInterrupt`）。MonitorTarget 消失时，将目标迄今所有观察记录随 Feedback 一并发送。同时移除 `RuntimeInfoRenderer` 中误导性的「目前不在视线内」文案，统一 `GetMonitorRecords` 详情头编号格式，增加编号漂移提示。纯 Unity 侧改动，不涉及协议或 Python 变更。

---

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Unity | `AIPlayer.cs` | 核心：MonitorTarget `StateChangedHandler` 增加 Disappearance 分支；新增 `HandleObserveTargetDisappeared` 方法（含历史记录输出）；`ErrorConditionFunc` 增加 Follow 目标消失检测；Override `OnFollowFixedUpdate` |
| Unity | `RuntimeInfoRenderer.cs` | 移除「目前不在视线内」；`RenderObserveTargetRuntime` 增加编号参数和编号漂移提示 |
| Unity | `AIPlayer.cs` `GetMonitorRecords` | 传入 `sceneObjs` 给渲染器以支持编号 |
| Unity | `ActionRuntime.cs` | 新增 `TargetName` 字段 |
| 协议 | `Tools/message.proto` | 无变更 |
| Python | `base_tools.py` / `agent_interuptible.py` | 无变更（可选：更新 docstring 说明新行为） |

---

## 3. 详细设计

### 3.1 MonitorTarget — 目标消失处理

#### 3.1.1 改动点：`StateChangedHandler` 增加 Disappearance 分支

当前 `StateChangedHandler`（`AIPlayer.cs` 第 621-646 行）对所有状态变化一视同仁：递增计数、拼接消息、入队 Records、更新 LastStateName。目标消失时需要**先走常规记录流程**（写入 Disappearance Record），再执行清理 + 发送 Feedback。

**修改方案**：在 `StateChangedHandler` 回调末尾，当 `newState == "Disappearance"` 时追加清理逻辑：

```csharp
runtime.StateChangedHandler = (obj, oldState, newState) =>
{
    // 0.更新状态变化次数
    runtime.StateChangeNum++;
    // 1.消息拼接
    var curTime = Time.time;
    var elapsed = curTime - runtime.LastChangeTime;
    string elapsedKey = runtime.StateChangeNum == 1 ? $"距离开始观察" : $"距离上次状态改变";
    var observeTime = curTime - runtime.ObserveStartTime;
    string msg =
        $"[第{runtime.StateChangeNum}次状态变化]\n" +
        $"观察时长:{observeTime:F1}秒\n" +
        $"状态变化:{oldState} -> {newState}\n" +
        $"{elapsedKey}:{elapsed:F1}秒前";
    // 2.记录
    string record = this.CreateMessageText(msg: msg, includeObserveTagerts:false);
    runtime.Records.Enqueue(record);
    while (runtime.Records.Count > ObserveRuntime.MaxRecords)
    {
        runtime.Records.Dequeue();
    }
    runtime.UnreadCount++;
    // 3.更新状态
    runtime.LastStateName = newState;
    runtime.LastChangeTime = curTime;

    // === 新增：目标消失时，结束该路观察并发 Feedback（含历史记录） ===
    if (newState == "Disappearance")
    {
        HandleObserveTargetDisappeared(runtime, obj);
    }
};
```

**设计要点**：Disappearance 与其他状态变化走**完全相同的记录流程**，区别仅在最后追加清理逻辑。这样确保 Records 中包含完整的状态变化序列（含最终消失事件）。

#### 3.1.2 新增方法 `HandleObserveTargetDisappeared`

```csharp
/// <summary>
/// 观察目标消失时的处理：输出历史观察记录、移除 ObserveRuntime、取消监听、发送 Feedback
/// </summary>
private void HandleObserveTargetDisappeared(ObserveRuntime runtime, SceneObjBase obj)
{
    // 1. 取消事件监听
    if (runtime.Target != null && runtime.StateChangedHandler != null)
    {
        runtime.Target.OnStateChanged -= runtime.StateChangedHandler;
        runtime.Target.OnObjectEnabled -= runtime.StateChangedHandler;
        runtime.Target.OnObjectDisabled -= runtime.StateChangedHandler;
    }

    // 2. 获取消失前编号（OnObjectDisabled 在 UnRegister 之前触发，此时 sceneObjs 仍含该对象）
    var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);
    int index = sceneObjs.IndexOf(obj);
    string targetLabel = index >= 0 ? $"{index}. {runtime.TargetName}" : runtime.TargetName;

    // 3. 渲染该目标迄今所有观察记录（含刚写入的 Disappearance Record）
    var renderer = new RuntimeInfoRenderer();
    string observeRecordsDetail = renderer.RenderObserveTargetRuntime(runtime, sceneObjs);

    // 4. 从 mObserveRuntimes 中移除
    mObserveRuntimes.Remove(runtime);

    // 5. 发送 Feedback（Feedback 本身即打断，无需额外 forceInterrupt 参数）
    string feedbackMsg =
        $"[持续观察中断]\n" +
        $"原因: 观察目标已从场景中消失\n" +
        $"对象: {targetLabel}\n" +
        $"说明: 该目标的持续观察任务已自动结束，注意力已释放\n\n" +
        $"==========观察记录汇总==========\n" +
        observeRecordsDetail;

    SendFeedbackToAgent(feedbackMsg, forceInterrupt: false, includeObserveTagerts: true);
}
```

**关键设计决策**：

- **写入 Disappearance Record 后再调用清理**：`StateChangedHandler` 中先完成常规记录，`HandleObserveTargetDisappeared` 再读取 `runtime.Records` 渲染到 Feedback。顺序保证 Records 中包含最终消失事件。
- **Feedback 包含完整历史记录**：复用 `RenderObserveTargetRuntime` 渲染全部 Records，Agent 在观察中断时一次性获得完整历史，避免此前观察成果丢失。
- **`sceneObjs.IndexOf(obj)` 在 `OnObjectDisabled` 回调中有效**：因为 v0.20.10 已调整 `OnDisable` 顺序为「先 `OnObjectDisabled` 再 `UnRegister`」。
- **`forceInterrupt: false`**：Feedback 消息经 Python `asend_feedback` → `_asend_message(is_feedback=True)` 处理时，`is_feedback=True` 本身即打断 Agent 推理，无需额外指定 `forceInterrupt`。
- **防重复触发**：`mObserveRuntimes.Remove(runtime)` 后，即使 `OnObjectDisabled` 事件因某种原因再次触发，该 runtime 已不在列表中。回调内 `runtime` 是闭包捕获的特定实例，`Remove` 操作确保只处理一次。

---

### 3.2 FollowTarget — 目标消失处理

FollowTarget 目标消失通过两层检测覆盖：

| 检测层 | 触发时机 | 覆盖场景 | 编号可用性 |
|--------|----------|----------|------------|
| `ErrorConditionFunc` | 下一帧 `Update()` | 目标 `OnDisable` 后 `activeInHierarchy == false` | 不可用（`UnRegister` 已执行），退化为 `TargetName` |
| `OnFollowFixedUpdate` override | 下一帧 `FixedUpdate()` | 目标对象被 Destroy，`TargetFollowing` 被 Unity 判为 null | 不可用（引用已失效），退化为 `TargetName` |

**关于编号**：Follow 消失时无法保证获取消失前编号（检测发生在 `UnRegister` 之后），Feedback 中 `对象` 行使用 `TargetName`（无编号）。这是可接受的退化——Agent 已知跟随目标名称，编号缺失不影响理解。

#### 3.2.1 改动点 1：`ErrorConditionFunc` 增加目标消失检测

当前 `FollowTarget` 的 `ErrorConditionFunc`（`AIPlayer.cs` 第 776-792 行）仅检查碰撞。增加目标消失检测：

```csharp
this.mCurActionRuntime.ErrorConditionFunc = () =>
{
    // === 新增：目标消失检测 ===
    if (this.TargetFollowing == null || !this.TargetFollowing.gameObject.activeInHierarchy)
    {
        if (this.mCurActionRuntime.Result == null)
            this.mCurActionRuntime.Result = new ActionResult();

        this.mCurActionRuntime.Result.Message =
            $"[跟随中断]\n" +
            $"原因: 跟随目标已从场景中消失\n" +
            $"对象: {this.mCurActionRuntime.TargetName ?? "未知目标"}\n" +
            $"说明: 跟随任务已结束";

        return true;
    }

    // 原有碰撞判断不变
    foreach (var obj in this.mTouchingObjs)
    {
        if (!this.mCurActionRuntime.StartTouchingObjs.Contains(obj))
        {
            // ...
        }
    }
    return false;
};
```

`ErrorConditionFunc` 在 `Update()` 中每帧调用，目标 `OnDisable` 后 `activeInHierarchy == false` 即可检测到，走 `Failed` + `OnActionFinished` 标准路径。

#### 3.2.2 改动点 2：`OnFollowFixedUpdate` override

当前 `CharaBase.OnFollowFixedUpdate` 中，`TargetFollowing == null` 时仅 `ChangeState("Idle")`，不处理 `mCurActionRuntime`。在 `AIPlayer` 中 override，改为走 `Failed` + `OnActionFinished` 路径：

```csharp
public override void OnFollowFixedUpdate()
{
    if (TargetFollowing == null)
    {
        // 目标消失：走 Failed 路径
        if (mCurActionRuntime != null && mCurActionRuntime.State == ActionState.Doing)
        {
            mCurActionRuntime.State = ActionState.Failed;
            mCurActionRuntime.Result ??= new ActionResult();
            mCurActionRuntime.Result.Message =
                $"[跟随中断]\n" +
                $"原因: 跟随目标已从场景中消失\n" +
                $"对象: {mCurActionRuntime.TargetName ?? "未知目标"}\n" +
                $"说明: 跟随任务已结束";

            var finishedRuntime = mCurActionRuntime;
            mCurActionRuntime = null;
            TargetFollowing = null;
            ChangeState("Idle");
            OnActionFinished(finishedRuntime);
        }
        else
        {
            TargetFollowing = null;
            ChangeState("Idle");
        }
        return;
    }

    // 原有跟随逻辑（不调用 base，因为 base.OnFollowFixedUpdate 的 null 检查会静默切 Idle）
    float delta = TargetFollowing.transform.position.x - transform.position.x;
    float distance = Mathf.Abs(delta);
    if (distance > FollowMaxDistance)
    {
        float dir = Mathf.Sign(delta);
        TurnBack(dir);
        mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
    }
    else if (distance < FollowMinDistance)
    {
        float dir = -Mathf.Sign(delta);
        TurnBack(dir);
        mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
    }
    else
    {
        float dir = Mathf.Sign(delta);
        TurnBack(dir);
        mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
    }
}
```

**防重复**：`ErrorConditionFunc` 和 `OnFollowFixedUpdate` 可能同一帧内都触发。但 `ErrorConditionFunc` 在 `Update()` 中先执行（`AIPlayer.Update()` 第 70 行），执行后 `mCurActionRuntime` 置 null。后续 `FixedUpdate` 中 `OnFollowFixedUpdate` 检测 `mCurActionRuntime == null`，走兜底 `ChangeState("Idle")` 不再发送 Feedback。

#### 3.2.3 新增 `ActionRuntime.TargetName` 字段

为防止目标销毁后无法获取名称，在 `ActionRuntime` 中新增：

```csharp
public class ActionRuntime
{
    // ... 现有字段 ...

    /// <summary>
    /// 跟随目标名称（防止目标消失后无法获取名称）
    /// </summary>
    public string TargetName;
}
```

在 `FollowTarget` 中赋值：

```csharp
mCurActionRuntime = new ActionRuntime
{
    ActionName = "FollowTarget",
    State = ActionState.Doing,
    TargetFollowing = target,
    TargetName = target.Name,  // 新增
    Result = new ActionResult()
};
```

#### 3.2.4 ActionSequence 内的 Follow 目标消失

当 Follow 嵌入 ActionSequence 时，`mCurActionRuntime` 指向当前 Action 的 `ActionRuntime`。目标消失后：

1. `ErrorConditionFunc` 检测到目标消失 → `mCurActionRuntime.State = Failed` → `OnActionFinished`。
2. `OnActionFinished` 内 `mCurActionSequenceRuntime?.State == ActionSequenceState.Executing` 分支捕获到 `Failed`，中断整个序列并发送 `[动作序列执行中断]` Feedback。

**这与撞击中断的行为一致**，符合预期。`mObserveRuntimes` 与 ActionSequence 无关，不受影响。

---

### 3.3 RuntimeInfoRenderer —「目前不在视线内」移除

#### 3.3.1 `RenderObserveRuntimeSummary`（第 64-88 行）

**当前**：`sceneObjs.IndexOf(runtime.Target) < 0` 时显示 `对象: {TargetName}(目前不在视线内)`。

**修改**：v0.20.11 落地后，消失的目标不再保留在 `mObserveRuntimes` 中，此分支仅在极端竞态下触发。将文案改为明确的终态提示：

```csharp
else
{
    infos.Add($"观察目标[{num}]:\n" +
        $"对象: {runtime.TargetName}（已消失）");
}
```

#### 3.3.2 `RenderActionRuntime`（第 39-51 行）

**当前**：`sceneObjs.IndexOf(TargetFollowing) < 0` 时显示 `跟随目标:{Name}(目前不在视线内)`。

**修改**：

```csharp
else
{
    text += $"跟随目标:{actionRuntime.TargetName ?? actionRuntime.TargetFollowing?.Name ?? "未知目标"}（已消失）";
}
```

使用 `TargetName`（新增字段）以应对 `TargetFollowing` 为 null 的情况。

#### 3.3.3 `FormatSceneObjLabel`（第 168-178 行）

此方法用于 WorldEventLog 渲染，与本期需求无直接关系，**不改动**。

---

### 3.4 `GetMonitorRecords` 详情头编号统一与编号漂移提示

#### 3.4.1 编号统一

`RuntimeInfoRenderer.RenderObserveTargetRuntime`（第 95-117 行）中当前 `对象:{runtime.TargetName}` 无编号。

为 `RenderObserveTargetRuntime` 增加 `sceneObjs` 参数：

```csharp
public string RenderObserveTargetRuntime(ObserveRuntime runtime, List<SceneObjBase> sceneObjs)
{
    var curTime = Time.time;
    var elapsed = curTime - runtime.LastChangeTime;
    string elapsedKey = runtime.StateChangeNum == 0 ? $"距离开始观察" : $"距离上次状态改变";
    var observeTime = curTime - runtime.ObserveStartTime;

    // 获取编号
    string targetLabel;
    if (runtime.Target != null)
    {
        int index = sceneObjs.IndexOf(runtime.Target);
        targetLabel = index >= 0 ? $"{index}. {runtime.TargetName}" : runtime.TargetName;
    }
    else
    {
        targetLabel = runtime.TargetName;
    }

    string text =
        $"[观察记录]\n" +
        $"对象:{targetLabel}\n" +
        $"观察时长:{observeTime:F1}秒\n" +
        $"最后状态:{runtime.LastStateName}\n" +
        $"{elapsedKey}:{elapsed:F1}秒前\n" +
        $"存储记录: {runtime.Records.Count}条\n" +
        $"注意: 同一目标在不同记录中的编号可能因其他物体出现/消失而发生改变，编号变化不代表观察目标改变\n\n";
    int idx = 1;
    foreach (string record in runtime.Records)
    {
        text += $"==========记录{idx}==========\n";
        text += record;
        text += "\n\n";
        idx++;
    }
    return text;
}
```

`AIPlayer.GetMonitorRecords` 中调用处补充传入 `sceneObjs`：

```csharp
public void GetMonitorRecords(string requestId, int monitorIndex)
{
    // ...
    var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);
    string text = actionInfoRenderer.RenderObserveTargetRuntime(runtime, sceneObjs);
    // ...
}
```

---

### 3.5 Python 侧变更（可选）

本期不修改 Python 代码。以下为**可选**的 docstring 更新：

- `monitor_target_cmd`：在 docstring 中补充「若观察目标消失，观察任务将自动结束并通过 Feedback 通知（含历史观察记录）」。
- `follow_target_cmd`：补充「若跟随目标消失，跟随将自动中断并通过 Feedback 通知」。

如用户认为有必要，可在开发阶段一并完成。

---

## 4. 实现步骤

1. **`ActionRuntime.cs`**：新增 `TargetName` 字段。
2. **`AIPlayer.cs` — MonitorTarget 消失处理**：
   - 修改 `StateChangedHandler`：在常规记录流程之后，若 `newState == "Disappearance"` 则调用 `HandleObserveTargetDisappeared`。
   - 新增 `HandleObserveTargetDisappeared` 方法（含历史记录输出）。
3. **`AIPlayer.cs` — FollowTarget 消失处理**：
   - 修改 `FollowTarget`：`ActionRuntime` 中赋值 `TargetName`。
   - 修改 `ErrorConditionFunc`：增加目标消失检测（主路径，使用 `TargetName`）。
   - Override `OnFollowFixedUpdate`：`TargetFollowing == null` 时走 `Failed` + `OnActionFinished` 路径（兜底，覆盖对象被 Destroy 的情况）。
4. **`RuntimeInfoRenderer.cs`**：
   - 修改 `RenderObserveRuntimeSummary`：`else` 分支文案改为「已消失」。
   - 修改 `RenderActionRuntime`：`else` 分支文案改为「已消失」，使用 `TargetName`。
   - 修改 `RenderObserveTargetRuntime`：增加 `sceneObjs` 参数、统一编号格式、追加编号漂移提示。
5. **`AIPlayer.cs` — `GetMonitorRecords`**：传入 `sceneObjs` 给渲染器。
6. **可选：Python docstring 更新**。

---

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| Follow 消失时 Feedback 中对象无编号 | 可接受退化，Agent 已知跟随目标名称；若后续需要编号可再追加主动检测机制 |
| `OnObjectDisabled` 回调中 Monitor 的 `sceneObjs.IndexOf` 返回 -1 | v0.20.10 已确保 `OnObjectDisabled` 先于 `UnRegister`；退化为 `TargetName` 无编号 |
| `ErrorConditionFunc` 与 `OnFollowFixedUpdate` 同帧触发 | `Update()` 先于 `FixedUpdate()` 执行，`ErrorConditionFunc` 置 null 后 `OnFollowFixedUpdate` 检测 `mCurActionRuntime == null` 跳过 |
| Monitor 回调中 `mObserveRuntimes.Remove(runtime)` 在迭代中执行 | `Remove` 在闭包内执行，不在 `foreach` 迭代中；`List.Remove` 线程安全（单线程 Unity） |
| ActionSequence 中 Follow 消失导致序列中断 | 与撞击中断行为一致，属预期 |
| `TargetName` 字段新增后 `RenderActionRuntime` 需兼容旧 `ActionRuntime`（无 `TargetName`） | `TargetName` 默认 null，渲染时 `?? actionRuntime.TargetFollowing?.Name ?? "未知目标"` |
| Feedback 含完整观察记录后体积较大 | 与 `GetMonitorRecords` 输出一致；观察任务结束后 Records 不再增长，属一次性输出 |
| 编号漂移提示可能被 Agent 忽略 | 仅作提示，不强制 Agent 理解；后续可考虑在 Record 中标注变化，本期不做 |

**回退**：移除 `HandleObserveTargetDisappeared` 调用、恢复 `ErrorConditionFunc` 为仅碰撞检测、恢复 `OnFollowFixedUpdate` 为原逻辑即可，不影响其他功能。

---

## 6. 测试建议

1. **单路 Monitor 消失**：Monitor 1 个目标 → 目标 Disable → 确认收到 `[持续观察中断]` Feedback（含历史记录）、`mObserveRuntimes.Count == 0`、`<你的状态>` 不再显示该观察。
2. **多路 Monitor 部分消失**：Monitor 3 个目标 → 中间 1 个 Disable → 确认仅消失的路被移除，其余 2 路观察正常。
3. **Monitor 消失 Feedback 含完整历史**：目标发生多次状态变化后消失 → Feedback 中 `==========观察记录汇总==========` 包含全部 Records（含最终 Disappearance Record）。
4. **Follow 目标消失（ErrorConditionFunc 路径）**：Follow 1 个目标 → 目标 Disable → 确认收到 `[跟随中断]` Feedback、Agent 回到 Idle、`mCurActionRuntime == null`。
5. **Follow 目标消失（OnFollowFixedUpdate 兜底路径）**：Follow 1 个目标 → 目标被 Destroy → 确认 `OnFollowFixedUpdate` 中 `TargetFollowing == null` 走 `Failed` + `OnActionFinished`。
6. **与 WorldEventLog 并发**：目标消失 → 同时写入 WorldEventLog（含 `[索引变化]`）和发 Feedback → 无冲突。
7. **主动 stop_action_cmd**：正常停止观察 → 文案「已停止 N 个观察任务」，与异常中断「[持续观察中断]」可区分。
8. **GetMonitorRecords 编号与漂移提示**：查看观察记录 → 详情头 `对象: {index}. {Name}`，且含编号漂移提示文案。
9. **ActionSequence 中 Follow 消失**：序列含 Follow 步骤 → 目标 Disable → 序列中断 + Feedback `[动作序列执行中断]`。
10. **竞态兜底**：极端情况下 `RuntimeInfoRenderer` 展示「已消失」而非「目前不在视线内」。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-10 | 完成全部改动：ActionRuntime.TargetName、MonitorTarget 消失处理、FollowTarget 消失处理、RuntimeInfoRenderer 移除「不在视线内」+ 编号统一 + 漂移提示、GetMonitorRecords 传入 sceneObjs |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
