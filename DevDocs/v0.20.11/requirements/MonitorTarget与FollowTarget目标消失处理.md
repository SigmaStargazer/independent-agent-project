# MonitorTarget / FollowTarget 目标消失异常停止 — 需求文档

> **版本**：v0.20.11  
> **状态**：原始需求（待 Agent 生成 PRD / 方案）  
> **关联版本**：v0.20.10（WorldEventLog、`SceneObjBase.OnDisable` 顺序调整、`[索引变化]` 说明）  
> **最后更新**：2026-06-10

---

## 一、背景

Agent 有两类依赖「锁定目标」的长时任务：

| 工具 | Unity 实现 | 当前结束方式 |
|------|------------|--------------|
| **MonitorTarget** | `mObserveRuntimes` + 目标事件监听 | 仅 `stop_action_cmd(actionType="observe")` 主动停止 |
| **FollowTarget** | `mCurActionRuntime` + `ChangeState("Follow")` | 撞击触发 `ErrorConditionFunc`；或 `TargetFollowing == null` 时静默 `Idle` |

当目标 `SceneObj` **消失**（`OnDisable` → `UnRegister` → 状态 `Disappearance`）时，现有行为存在缺口：

### 1.1 MonitorTarget 现状

- 目标 `OnObjectDisabled` 仍会走 `StateChangedHandler`，把 `* -> Disappearance` **记入** `ObserveRuntime.Records`，并增加 `UnreadCount`。
- **不会**自动结束该观察任务；`mObserveRuntimes` 中条目**保留**。
- **不会**主动 `SendFeedbackToAgent` 通知 Agent「观察已中断」（状态变化本身也不发 Feedback，需 Agent 调 `GetMonitorRecords` 才发现）。
- `RenderObserveRuntimeSummary` 中，若目标已不在 `GetSceneObjsExcluding` 列表内，显示为  
  `对象: {Name}(目前不在视线内)` —— 含糊，且观察任务实际仍处于「进行中」状态。

### 1.2 FollowTarget 现状

- `CharaBase.OnFollowFixedUpdate`：仅当 `TargetFollowing == null` 时切 `Idle`，**无 Feedback**。
- 目标 `Disable` 后引用可能仍存在，但已从 `SceneObjManager` 注销；跟随逻辑可能继续访问 `transform`，行为不确定。
- `RenderActionRuntime` 中，若跟随目标不在 `sceneObjs` 列表，显示  
  `跟随目标:{Name}(目前不在视线内)` —— 同样含糊，且未明确「跟随已失败」。

### 1.3 与 v0.20.10 的关系

v0.20.10 将在世界事件日志中记录目标消失及 `[索引变化]`。本需求解决的是 **长时任务生命周期**：目标消失后，Monitor / Follow **应视为异常结束**，并**主动打断 Agent 推理**（Feedback），而不是让 Agent 从含糊的 UI 文案或滞后日志中自行猜测。

---

## 二、设计目标

1. **MonitorTarget**：任一被观察目标消失时，**自动结束该路观察**，清理监听与 `mObserveRuntimes` 条目，并向 Agent 发送 **Feedback**（打断推理）。
2. **FollowTarget**：跟随目标消失时，**异常停止跟随**（等同动作失败），向 Agent 发送 **Feedback**。
3. **RuntimeInfoRenderer**「目前不在视线内」：审视其是否仍有必要；若与上述自动停止重复或误导，**去掉或调整**文案与出现条件。
4. **不引入 UUID** 等机械化标识；目标指称继续用 `GetSceneObjsExcluding` 下的 `{index}. {Name}`（与 v0.20.10 / `RuntimeInfoRenderer` 惯例一致）。若消失瞬间仍能解析编号，Feedback 中应带上**消失前的编号**。

---

## 三、功能需求

### 3.1 「目标消失」的判定

满足以下任一即视为消失（以 Unity 侧可检测为准）：

| 条件 | 说明 |
|------|------|
| 目标触发 `OnObjectDisabled`，且 `newState == "Disappearance"` | 与 `SceneObjBase` 状态机一致 |
| 目标已从 `SceneObjManager` 注销（`UnRegister` 后不在 `GetSceneObjsExcluding` 内） | 与 Disable 链路一致 |
| 目标 Unity 对象/组件已销毁，`Target` 引用失效 | 兜底 |

**建议触发点**（实现阶段在方案中二选一或组合，需求层要求结果一致）：

- MonitorTarget：在已有 `StateChangedHandler` 内识别 `Disappearance`，或监听 `OnObjectDisabled` 后专用处理。
- FollowTarget：在 `ErrorConditionFunc`、Follow 状态 Update、或目标 `OnObjectDisabled` 回调中检测。

**依赖**：v0.20.10 拟将 `SceneObjBase.OnDisable` 调整为 **先** `OnObjectDisabled` **再** `UnRegister`，以便消失瞬间仍能 `IndexOf` 得到 `{index}. {Name}`，用于 Feedback 与 `[索引变化]` 类文案。v0.20.11 应与此顺序兼容。

### 3.2 MonitorTarget — 目标消失时

**行为：**

1. **结束**该目标的观察任务（从 `mObserveRuntimes` 移除对应 `ObserveRuntime`）。
2. **取消**该目标上的 `OnStateChanged` / `OnObjectEnabled` / `OnObjectDisabled` 监听。
3. **可选**：将最后一次 `Disappearance` 状态变化写入 `Records`（实现阶段决定；若与「中断 Feedback」重复，可只保留 Feedback 中的摘要）。
4. **`SendFeedbackToAgent`**，建议 `forceInterrupt = true`（与定时器到期、移动中断等环境反馈一致，确保打断当前推理）。

**Feedback 内容要求**（示意，实现可微调措辞，须含关键信息）：

```
[持续观察中断]
原因: 观察目标已从场景中消失
对象: 2. 按钮
说明: 该目标的持续观察任务已自动结束，注意力已释放
```

- `对象` 行：优先 `消失前索引. 名称`；无法解析索引时退化为 `名称`。
- 若 v0.20.10 已落地 `[索引变化]`，可在 Feedback 中**简要提及**其余物体编号前移（或提示 Agent 查阅 `GetWorldEventLog`），避免与 WorldEventLog 全文重复。

**不应出现的状态：**

- 目标消失后，`mObserveRuntimes` 中仍保留该目标且 `RenderObserveRuntimeSummary` 显示「目前不在视线内」。
- 目标消失后，`<你的状态>` 里仍列出该路「进行中的持续观察」。

### 3.3 FollowTarget — 目标消失时

**行为：**

1. 将当前 `mCurActionRuntime` 置为 **失败/中断**（与撞击中断一致，如 `ActionState.Failed`）。
2. 清空 `TargetFollowing`，`ChangeState("Idle")`，停止位移。
3. 经 **`OnActionFinished` → `SendFeedbackToAgent`** 发送中断说明（与移动撞击中断路径一致）。

**Feedback 内容要求**（示意）：

```
[跟随中断]
原因: 跟随目标已从场景中消失
对象: 1. 电梯
说明: 跟随任务已结束
```

**不应出现的状态：**

- 目标消失后 Agent 仍处于 `Follow` 状态且无 Feedback。
- 仅 `TargetFollowing == null` 时静默切 `Idle` 而无说明。
- `<你的状态>` 中长期显示 `跟随目标:xxx(目前不在视线内)` 而跟随任务未标记结束。

### 3.4 与 stop_action_cmd 的关系

- 用户/Agent **主动** `stop_action_cmd(observe)` / `StopMovement` 行为**不变**。
- 目标消失导致的停止是 **系统侧异常结束**，文案须与主动停止区分（「中断」vs「已停止 N 个观察任务」）。

### 3.5 RuntimeInfoRenderer —「目前不在视线内」

当前出现位置：

| 方法 | 条件 | 现文案 |
|------|------|--------|
| `RenderObserveRuntimeSummary` | `sceneObjs.IndexOf(runtime.Target) < 0` | `对象: {TargetName}(目前不在视线内)` |
| `RenderActionRuntime` | `sceneObjs.IndexOf(TargetFollowing) < 0` | `跟随目标:{Name}(目前不在视线内)` |

**问题：**

- 「不在视线内」语义模糊：可能是 Disable/消失，也可能是索引漂移后的**瞬态**；Agent 无法区分「任务已死」与「还能继续」。
- v0.20.11 自动停止落地后，**正常路径下不应再存在**「任务进行中但目标不在列表」的稳态。

**需求结论（须在 PRD/方案中择一明确）：**

| 方案 | 说明 |
|------|------|
| **A. 移除** | 删除上述「目前不在视线内」分支；消失后任务已清理，摘要里只显示**有效进行中**的 Monitor / Follow。若列表为空则显示「无」。 |
| **B. 调整为明确终态文案** | 仅用于**极短窗口**（如消失帧到 Feedback 发出前）或防御性兜底，改为例如 `对象: 2. 按钮（已消失，观察任务结束中）` / `跟随目标:…（已消失，跟随结束中）`，且**不得**与「进行中」样式混淆。 |

**倾向**：优先 **方案 A**；若实现存在单帧延迟，可用 **方案 B** 作过渡，但不得长期展示。

**其他渲染一致性：**

- `GetMonitorRecords` / `RenderObserveTargetRuntime` 详情头目前为 `对象:{TargetName}` **无编号** —— 本需求文档建议 v0.20.11 **一并评估**是否改为 `{index}. {Name}`（与 v0.20.10 编号体系一致）；若改动面大，可单列验收项「后续优化」，但 PRD 须写明。

---

## 四、非功能需求

- **打断语义**：消失触发的 Feedback 应能进入 Agent `feedback_queue` 并触发打断（与现有环境反馈一致）。
- **多目标 Monitor**：仅结束**消失的那一个** `ObserveRuntime`，不影响同 Agent 另外 1～2 路观察。
- **重复触发**：同一消失事件不得重复发送多条中断 Feedback、不得重复移除同一 runtime。
- **编码**：UTF-8。

---

## 五、范围边界

### 本期包含

- Unity：`AIPlayer` 中 MonitorTarget / FollowTarget 的目标消失检测、清理、Feedback。
- Unity：`RuntimeInfoRenderer` 「目前不在视线内」的移除或调整。
- （可选，PRD 定夺）`GetMonitorRecords` 详情头对象编号格式统一。

### 本期不包含

- Python `base_tools.py` 工具签名变更（除非需更新 docstring 描述新行为）。
- 协议 / `message.proto` 变更（预期纯 Unity 行为修正 + Feedback 文案）。
- Move / ActionSequence 其他动作的目标消失处理（可引用相同模式，但不强制本期实现）。
- WorldEventLog 本身（属 v0.20.10）。

---

## 六、验收标准

- [ ] MonitorTarget 观察中的目标 `Disable` 后，该路观察自动结束，`mObserveRuntimes` 不再含该目标。
- [ ] 上述情况 Agent 收到 `[持续观察中断]` 类 Feedback，且带 `对象: {index}. {Name}`（或合理退化）。
- [ ] FollowTarget 跟随中目标 `Disable` 后，Agent 退出 Follow，收到 `[跟随中断]` 类 Feedback。
- [ ] 主动 `stop_action_cmd(observe)` 仍正常工作，文案与异常中断可区分。
- [ ] `<你的状态>` / `RenderObserveRuntimeSummary` / `RenderActionRuntime` 在目标消失**稳态下**不再出现误导性的「目前不在视线内」；或已按 3.5 调整为明确终态文案。
- [ ] 多路 Monitor 仅消失一路时，其余路观察不受影响。
- [ ] 与 v0.20.10 `OnDisable` 顺序、`[索引变化]` 无逻辑冲突（联调场景：目标消失同时写 WorldEventLog + 发本需求 Feedback）。

---

## 七、待 PRD 阶段确认的问题

1. MonitorTarget 消失时，是否仍将 `Disappearance` 写入 `Records`，还是仅发 Feedback？（需求倾向：**可省略最后一条 Record**，避免 Agent 未拉取记录却收到重复信息；若保留，须在 Feedback 中提示「详见观察记录」。）
2. Follow 中断是否复用 `ActionState.Failed` + 现有 `OnActionFinished` 路径，还是新增 `Aborted`？（需求倾向：**复用 Failed**，与撞击中断一致。）
3. `RenderObserveTargetRuntime` / `GetMonitorRecords` 是否在本期统一加上 `{index}. {Name}`？
4. Feedback 是否默认 `forceInterrupt = true`？（需求倾向：**是**。）

---

*本文档为 v0.20.11 原始需求，供生成 `PRD.md` / `solution.md` 使用；确认前请勿直接改业务代码。*
