# PRD — v0.20.11 MonitorTarget / FollowTarget 目标消失异常停止

> **状态**：待确认
> **对应需求**：`requirements/MonitorTarget与FollowTarget目标消失处理.md`
> **最后更新**：2026-06-10

---

## 1. 背景与目标

Agent 的两类长时任务——**MonitorTarget**（持续观察，最多 3 个目标）和 **FollowTarget**（跟随移动）——均依赖「锁定目标 `SceneObj`」存活。当目标从场景消失（`OnDisable` → `OnObjectDisabled(Disappearance)` → `UnRegister`）时，当前行为存在以下缺陷：

| 工具 | 现状问题 |
|------|----------|
| MonitorTarget | 目标消失后仅记录一条 `Disappearance` 状态变化到 `Records`，但 `ObserveRuntime` **不会自动移除**，监听不取消，不主动通知 Agent，`<你的状态>` 仍显示含糊的「目前不在视线内」 |
| FollowTarget | `OnFollowFixedUpdate` 中仅当 `TargetFollowing == null` 时静默切 `Idle`，不设 `Failed` 状态，不调用 `OnActionFinished`，不发送 Feedback；`mCurActionRuntime` 未清理，`TargetFollowing` 引用可能悬空 |
| RuntimeInfoRenderer | 「目前不在视线内」无法区分「任务已死」和「还能继续」，误导 Agent 推理 |

**本期目标**：目标消失时，MonitorTarget / FollowTarget **视为异常结束**，自动清理运行时状态，并通过 **Feedback 打断 Agent 推理**，使 Agent 立即感知并重新决策。

---

## 2. 范围

### 2.1 本期包含

- Unity `AIPlayer`：MonitorTarget 目标消失时自动结束观察、取消监听、携带历史观察记录发送 Feedback。
- Unity `AIPlayer`：FollowTarget 目标消失时异常停止跟随、发送 Feedback。
- Unity `RuntimeInfoRenderer`：移除「目前不在视线内」文案。
- （附带）`RenderObserveTargetRuntime` / `GetMonitorRecords` 详情头对象编号格式统一为 `{index}. {Name}`。
- （附带）`RenderObserveTargetRuntime` 增加编号漂移提示，说明同一观察对象在不同 Record 中编号可能因其他物体注销而改变。

### 2.2 本期不包含

- Python `base_tools.py` 工具签名变更（仅可能在 docstring 中补充说明新行为）。
- 协议 / `message.proto` 变更（纯 Unity 行为修正 + Feedback 文案，不新增 Request/Response）。
- Move / ActionSequence 其他动作的目标消失处理（可引用相同模式，但不强制本期实现）。
- WorldEventLog（属 v0.20.10）。
- `monitor_target_cmd` / `follow_target_cmd` 工具的 Python 侧参数或返回值变更。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| AI Agent | 正在 MonitorTarget 一个按钮，按钮被机关移除 | 收到 `[持续观察中断]` Feedback（含该目标迄今所有观察记录），观察任务自动结束，注意力释放 |
| AI Agent | 正在 FollowTarget 一个电梯，电梯消失 | 收到 `[跟随中断]` Feedback，退出 Follow 状态，重新决策 |
| AI Agent | 同时 Monitor 3 个目标，其中 1 个消失 | 仅消失的 1 路观察结束，其余 2 路不受影响 |
| AI Agent | 主动 `stop_action_cmd(observe)` 停止观察 | 行为不变，文案与异常中断可区分（「已停止 N 个观察任务」vs「[持续观察中断]」） |

---

## 4. 功能需求

### 4.1 「目标消失」的判定与触发

**判定条件**（满足任一）：

| 条件 | 说明 |
|------|------|
| 目标触发 `OnObjectDisabled`，且 `newState == "Disappearance"` | 主路径，与 `SceneObjBase` 状态机一致 |
| 目标已从 `SceneObjManager` 注销（`UnRegister` 后不在 `GetSceneObjsExcluding` 内） | 兜底 |
| 目标 Unity 对象已销毁，`Target` 引用被 Unity 判为 `null` | 兜底 |

**触发点**：

- **MonitorTarget**：在现有 `StateChangedHandler` 中识别 `newState == "Disappearance"`，触发清理逻辑。
- **FollowTarget**：在 `ErrorConditionFunc` 中增加目标消失检测；同时在 `OnFollowFixedUpdate` 中 `TargetFollowing == null` 时走异常结束路径。

**前提**：v0.20.10 已将 `SceneObjBase.OnDisable` 调整为 **先 `OnObjectDisabled` 再 `UnRegister`**，确保消失瞬间仍可 `IndexOf` 获取编号。

### 4.2 MonitorTarget — 目标消失时的行为

1. **写入最后一条 Disappearance Record**：将 `* -> Disappearance` 状态变化按现有格式记入 `Records`，与所有常规状态变化记录保持一致。
2. **结束**该目标的观察任务：从 `mObserveRuntimes` 移除对应 `ObserveRuntime`。
3. **取消**该目标上的 `OnStateChanged` / `OnObjectEnabled` / `OnObjectDisabled` 监听。
4. **发送 Feedback**（Feedback 本身即打断，无需额外指定 `forceInterrupt`），格式如下：

```
[持续观察中断]
原因: 观察目标已从场景中消失
对象: 2. 按钮
说明: 该目标的持续观察任务已自动结束，注意力已释放

==========观察记录汇总==========
（此处附上该目标迄今所有 Records 的格式化输出，复用 RenderObserveTargetRuntime 的逻辑）
```

- `对象` 行：优先使用**消失前的索引** `{index}. {Name}`（在 `OnObjectDisabled` 触发时 `UnRegister` 尚未执行，`sceneObjs.IndexOf(target)` 有效）；无法解析索引时退化为 `名称`。
- 观察记录汇总：包含该 `ObserveRuntime` 全部 `Records`，让 Agent 在观察中断时一次性获得完整历史，避免此前观察成果丢失。
- 若 v0.20.10 已落地 `[索引变化]`，Feedback 中可**简要提及**其余物体编号前移，或提示 Agent 查阅 `GetWorldEventLog`，避免与 WorldEventLog 全文重复。

**不应出现的状态**：

- 目标消失后 `mObserveRuntimes` 中仍保留该目标。
- `<你的状态>` / `RenderObserveRuntimeSummary` 显示「目前不在视线内」。

### 4.3 FollowTarget — 目标消失时的行为

1. 将 `mCurActionRuntime` 置为 **`ActionState.Failed`**，设置 `Result.Message` 为中断说明文案。
2. 清空 `TargetFollowing`，`ChangeState("Idle")`，停止位移。
3. 经 **`OnActionFinished`** 路径发送 Feedback（与移动撞击中断路径一致，确保 `EndEnv` 快照正确）。

Feedback 文案：

```
[跟随中断]
原因: 跟随目标已从场景中消失
对象: 1. 电梯
说明: 跟随任务已结束
```

- `对象` 行：同 MonitorTarget 规则，优先消失前索引。

**不应出现的状态**：

- 目标消失后 Agent 仍处于 `Follow` 状态且无 Feedback。
- 仅 `TargetFollowing == null` 时静默切 `Idle` 而无说明。
- `<你的状态>` 中长期显示 `跟随目标:xxx(目前不在视线内)`。

### 4.4 与 stop_action_cmd 的关系

- 主动 `stop_action_cmd(observe)` / `StopMovement` 行为**不变**。
- 消失导致的停止是**系统侧异常结束**，文案须与主动停止区分（「中断」vs「已停止 N 个观察任务」）。

### 4.5 RuntimeInfoRenderer —「目前不在视线内」处理

**决定：移除**

v0.20.11 自动停止落地后，正常路径下不应再存在「任务进行中但目标不在列表」的稳态。移除以下两处「目前不在视线内」分支：

| 方法 | 现状 | 改为 |
|------|------|------|
| `RenderObserveRuntimeSummary` | `sceneObjs.IndexOf(runtime.Target) < 0` 时显示 `对象: {TargetName}(目前不在视线内)` | 已消失的目标不会出现在 `mObserveRuntimes` 中，此分支不再可达；保留防御性兜底但改为 `对象: {TargetName}（已消失）`（仅在极端竞态时触发） |
| `RenderActionRuntime` | `sceneObjs.IndexOf(TargetFollowing) < 0` 时显示 `跟随目标:{Name}(目前不在视线内)` | 同理，改为 `跟随目标:{Name}（已消失）`（防御性兜底） |

### 4.6 GetMonitorRecords 详情头编号统一与编号漂移提示

#### 4.6.1 编号统一

当前 `RenderObserveTargetRuntime` 中 `对象:{runtime.TargetName}` **无编号**。本期统一为 `{index}. {TargetName}`（与 v0.20.10 编号体系一致，且与 Records 中环境物体编号格式对齐）。

- 若 `runtime.Target` 仍存活，用 `sceneObjs.IndexOf(runtime.Target)` 获取编号。
- 若 `runtime.Target` 为 null（理论上不应出现，但防御性处理），退化为 `runtime.TargetName`（无编号）。

#### 4.6.2 编号漂移提示

同一观察对象在不同时间的 Record 中，编号可能因**其他物体的注销/出现**而发生改变（例如：按钮原编号为 `2.`，另一物体消失后变为 `1.`）。Agent 可能误以为观察目标发生了变化。

**处理方式**：在 `RenderObserveTargetRuntime` 的详情头中追加一条简短提示：

```
注意: 同一目标在不同记录中的编号可能因其他物体出现/消失而发生改变，编号变化不代表观察目标改变
```

此提示为静态文案，无需跟踪具体编号变化历史，仅提醒 Agent 注意此现象即可。

### 4.7 已确认的设计决策

| # | 问题 | 结论 |
|---|------|------|
| 1 | MonitorTarget 消失时是否将 `Disappearance` 写入 `Records`？ | **写入**，且将目标迄今所有 Records 随 Feedback 一并发送给 Agent |
| 2 | Follow 中断是复用 `ActionState.Failed` + 现有 `OnActionFinished`，还是新增 `Aborted`？ | **复用 `Failed`**，与撞击中断一致，不新增状态 |
| 3 | `RenderObserveTargetRuntime` / `GetMonitorRecords` 是否本期统一加 `{index}. {Name}`？ | **是**，便于 Record 中环境物体编号对应；同时追加编号漂移提示 |
| 4 | Feedback 是否需要额外指定 `forceInterrupt = true`？ | **不需要**，Feedback（`is_feedback=True`）本身即打断 Agent 推理，`forceInterrupt` 参数冗余 |

---

## 5. 非功能需求

- **打断语义**：消失触发的 Feedback 进入 `feedback_queue`，`is_feedback=True` 始终打断当前推理，无需额外指定 `forceInterrupt`。
- **多目标 Monitor**：仅结束消失的那一个 `ObserveRuntime`，不影响其余观察。
- **重复触发**：同一消失事件不得重复发送 Feedback、不得重复移除同一 runtime（在 handler 触发前先检查目标是否已从 `mObserveRuntimes` 移除）。
- **与 v0.20.10 兼容**：目标消失同时写 WorldEventLog + 发本需求 Feedback，无逻辑冲突。
- **编码**：所有新增/修改源文件 UTF-8。

---

## 6. 验收标准

- [ ] MonitorTarget 观察中的目标 `Disable` 后，该路观察自动结束，`mObserveRuntimes` 不再含该目标。
- [ ] 上述情况 Agent 收到 `[持续观察中断]` 类 Feedback，带 `对象: {index}. {Name}`（或合理退化），且 Feedback 中包含该目标迄今所有观察记录。
- [ ] MonitorTarget 消失时最后一条 Disappearance Record 已写入。
- [ ] FollowTarget 跟随中目标 `Disable` 后，Agent 退出 Follow，收到 `[跟随中断]` 类 Feedback。
- [ ] 主动 `stop_action_cmd(observe)` 仍正常工作，文案与异常中断可区分。
- [ ] `<你的状态>` / `RenderObserveRuntimeSummary` / `RenderActionRuntime` 在目标消失稳态下不再出现误导性的「目前不在视线内」。
- [ ] 多路 Monitor 仅消失一路时，其余路观察不受影响。
- [ ] 与 v0.20.10 `OnDisable` 顺序、`[索引变化]` 无逻辑冲突。
- [ ] `GetMonitorRecords` 详情头对象格式统一为 `{index}. {Name}`（目标有效时）。
- [ ] `GetMonitorRecords` 输出含编号漂移提示文案。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
