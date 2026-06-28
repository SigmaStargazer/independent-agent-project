# v0.21.7 ActionSequence × IImmovableState Fix

> **状态**：待确认
> **最后更新**：2026-06-28
> **关联**：`DevDocs/v0.21.7/solution.md` — 本文件是 v0.21.7 主方案内 `IImmovableState` + ActionSequence 联动的补充修复方案。
> **复现日志**：`Src/PythonServer/logs/prompts/小明/2026-06-28_09-29-39.log`

## 1. 背景

按 v0.21.7 / `solution_cabinet_freeze_fix` 设计，玩家在 Hidden 状态下应满足：

- 单步 `move_cmd` / `follow_target_cmd` 被 `RejectIfImmovable` 拦截（已通过）。
- 单步 `interact_cmd` 仍可触发柜子退出（已通过）。
- ActionSequence 第一步是 `Move` 时整段被拦截。
- ActionSequence 第一步是 `Interact` 时仍可触发柜子退出。

但联调日志显示后两条**全部失败**：

| 测试 | 预期 | 实际 |
|---|---|---|
| 3 — Hidden 下以 Move 开头的 AS | 失败 | **意外成功**，玩家从 Hidden 变 Idle 并实际移动了 2m |
| 4 — Hidden 下以 Interact 开头的 AS | 成功（离开柜子） | 序列执行完成但玩家仍在 Hidden，柜子没出来 |

测试 1、2、5 均符合预期，说明守卫在 `AIPlayer.Move` / `AIPlayer.FollowTarget` / `RejectIfImmovable` / `Trap → ReturnToCheckPointByHurt` 链路上是生效的，问题只发生在 ActionSequence 启动链路。

## 2. 根因

`AIPlayer.StartActionSequence`（`AIPlayer.cs` 1361-1411）：

```csharp
try
{
    // 1.停止当前Action
    this.StopMovement(false);
    ChangeState("Idle");        // ← 这一行没判 IsImmovable
    // 2.替换 ActionSequence ...
    // 3.启动 ActionSequence
    mCurActionSequenceRuntime.State = ActionSequenceState.Executing;
    this.ExecuteCurAction();
}
```

`StopMovement` 内部本来就有 `if (!IsImmovable) ChangeState("Idle");` 守卫（行 569-570），但紧接着这一行**裸 ChangeState** 把它覆盖了：触发 `HiddenState.OnExit` → `PlayerBase.OnHiddenExit` → 还原 `RigidbodyConstraints2D` + 重新启用所有 `Renderer`。此刻 `mCurState` 已经是 Idle，`IsImmovable == false`。

随后 `ExecuteCurAction` 分发：

- **测试3** 走 `ExecuteMoveAction`（1520+）：1523 行 `if (IsImmovable)` 守卫此时为 false → 不命中拦截 → 正常 Move → 玩家移动 2m。整个流程"意外成功"。
- **测试4** 走 `ExecuteInteractAction`（1662+）：1692 行 `if (!IsImmovable) ChangeState("Idle");` 当 IsImmovable=false 时进入分支再切一次 Idle（空操作）→ `SceneObjManager.Interact` → `Cabinet.Interact` 检查 `player.StateName != "Hidden"`（因为已被强制 Idle）→ 走"**进入柜子**"分支 → 瞬移 enter anchor + `ChangeState("Hidden")` → 表面上看是"交互没生效，玩家还在 Hidden"，实际是"先非法退出又重新进入"。

`AIPlayer.StopActionSequence` 行 1463-1464 有完全相同的两连行（`StopMovement(false); ChangeState("Idle");`），同一类问题；`ExecuteWaitAction` 行 1659 也有一行裸 `ChangeState("Idle")`（本次测试未覆盖到，是隐患）。

`StopMovement` 自身（行 553-572）已经按 v0.21.7 设计正确做了 `IsImmovable` 守卫，所以**最少改动**是删掉这两行覆盖。

## 3. 改动点

| # | 文件 | 行 | 改动 | 理由 |
|---|---|---|---|---|
| F1 | `AIPlayer.cs::StartActionSequence` | 1378 | **删除** `ChangeState("Idle");` | `StopMovement(false)` 内部已守卫；本行覆盖了守卫 |
| F2 | `AIPlayer.cs::StopActionSequence` | 1464 | **删除** `ChangeState("Idle");` | 同 F1，避免主动停 AS 时把 Hidden 误切回 Idle |
| F3 | `AIPlayer.cs::ExecuteWaitAction` | 1659 | `ChangeState("Idle");` → `if (!IsImmovable) ChangeState("Idle");` | 防止未来 Wait 在 Hidden 下被触发时同样退出 Hidden（保持与 `ExecuteInteractAction` / `ExecuteSelectAction` / `StopMovement` 一致的守卫语法） |

不动 `ExecuteMoveAction`、`ExecuteInteractAction`、`ExecuteSelectAction`、`ExecuteInputAction`，它们已有正确的 `IsImmovable` / `IsDead` 守卫。

## 4. 修复后预期行为（与日志对照）

| 测试 | 修复后预期 | 触达分支 |
|---|---|---|
| 3 — Hidden + AS[Move,...] | `start_action_sequence_cmd` 成功，AS 立刻进入 Aborted；`Move` action.State = Failed，Result.Message = `[动作中断]当前处于 Hidden 状态，无法移动。` | `ExecuteMoveAction` 1523 守卫命中 → `OnActionFinished` → `mCurActionSequenceRuntime.State = Aborted` |
| 4 — Hidden + AS[Interact] | AS 完成；玩家 Hidden → Idle；柜子瞬移到 exit anchor | `ExecuteInteractAction` 1692 `if (!IsImmovable)` 不进入 → 不切 Idle → `Cabinet.Interact` 看 `StateName == "Hidden"` 走「离开柜子」分支 |
| 1, 2, 5 | 无变化（已通过） | — |

新一轮验证日志中应能直接看到：

- 测试3 Tool Message `[动作序列确认开始执行结果]成功: 共计1个动作` 后，紧接 `[动作序列执行中断][动作中断]当前处于 Hidden 状态，无法移动。`（与测试5 的 Move 受阻报文一致）。
- 测试4 完成后 `<你的状态># 状态:Idle`，且 `# 可选择交互: 柜子` 仍在但玩家位置 = exit anchor。

## 5. 测试用例

完全复用 v0.21.7 主方案的 5 项测试用例（玩家在第一关 + 柜子），重测后预期：

| # | 状态 | 操作 | 预期结果 |
|---|---|---|---|
| F-AS-1 | Hidden | `move_cmd right 2` | 失败：`你正躲在柜子里，无法移动。` |
| F-AS-2 | Hidden | `interact_cmd`（柜子最近） | 成功：`你从柜子里出来了。` 状态 → Idle |
| F-AS-3 | Hidden | `plan_action_sequence_cmd [Move(direction=right, displacement>=2)] → start` | AS Aborted，Move=Failed，提示 `当前处于 Hidden 状态，无法移动。` 玩家仍在 Hidden、位置不变 |
| F-AS-4 | Hidden | `plan_action_sequence_cmd [Interact] → start` | AS Completed，Interact=Done。玩家 Hidden → Idle，被瞬移到 exit anchor |
| F-AS-5 | Idle | `plan_action_sequence_cmd [Interact, Move(displacement>=2)] → start` | Interact=Done（进 Hidden），Move=Failed（被 Hidden 拦截），AS=Aborted |

补加自测项（无需 Unity 联调）：

- F-AS-6（代码静态检查）：`AIPlayer.cs` 内 `ChangeState("Idle")` 的所有调用点，要么有 `IsImmovable` 守卫，要么属于"退出某 IImmovableState 后由 owner 显式切换"路径（如 `Cabinet.Interact` 退出柜子）。检查方法：`rg "ChangeState\\(\"Idle\"\\)" Src/IndependentAgentProject -n` 把每条调用都过一遍。

## 6. 风险与回滚

- 风险点 1：F2 删除 `StopActionSequence` 内的 `ChangeState("Idle")` 后，处于 Hidden 时执行 `StopActionSequence`，玩家会停在 Hidden。这是**正确行为**（用户主动中止 AS 不应解除躲藏）。如未来要求"AS 中止时强制退柜"，应由调用方/Cabinet 自己处理。
- 风险点 2：F3 改成守卫版后，Hidden 状态下执行 AS 中的 Wait 动作不会切回 Idle。这同样是**正确行为**——但当前 Hidden 链路里 Wait 不应出现（仍按 v0.21.7 设计被 Move 拦截覆盖到），属于防御性收紧。

回滚：本 fix 仅删 2 行 + 改 1 行守卫，回滚到 v0.21.7-fix 提交前即可。

## 7. 实现记录

| 日期 | 内容 |
|------|------|
| 2026-06-28 | 起草方案；待用户确认后再改 `AIPlayer.cs` |
