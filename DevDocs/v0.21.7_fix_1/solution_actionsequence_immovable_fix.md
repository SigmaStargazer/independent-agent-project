# v0.21.7_fix_1 ActionSequence × IImmovableState Fix

> 状态: 已实现
> 最后更新: 2026-06-28
> 关联主方案: DevDocs/v0.21.7/solution.md, DevDocs/v0.21.7/solution_cabinet_freeze_fix.md
> 复现日志: Src/PythonServer/logs/prompts/小明/2026-06-28_09-29-39.log
> 影响范围: Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/AIPlayer.cs (删 2 行 + 改 1 行)
> 风险等级: 低 (局部替换,回滚=单次 git revert)

## 1. 背景与现象

按 v0.21.7 主方案 + `solution_cabinet_freeze_fix` 设计，玩家在 `Hidden` 状态下应同时满足下列规则（均已在单步工具路径上通过）：

- `move_cmd` / `follow_target_cmd`：被 `AIPlayer.RejectIfImmovable` 拦截，工具直接返回失败。
- `interact_cmd`：不被拦截，且对柜子最近距离命中时调用 `Cabinet.Interact` 走「离开柜子」分支。
- 玩家 Rigidbody 位置完全冻结、渲染组件全部禁用、对所有伤害源无敌。

但联调 (`2026-06-28_09-29-39.log`) 的 5 项用例显示，**ActionSequence 启动链路上的 2 类用例没满足上述规则**：

| # | 状态 | 操作 | 预期 | 实际 |
|---|---|---|---|---|
| 1 | Hidden | 单步 `move_cmd right 2` | 失败，文案 `你正躲在柜子里, 无法移动。` | ✅ 与预期一致 |
| 2 | Hidden | 单步 `interact_cmd`（柜子最近） | 成功，`你从柜子里出来了。` 状态 → Idle | ✅ 与预期一致 |
| 3 | Hidden | `plan_action_sequence_cmd [Move(right, ≥2m)] → start` | AS Aborted，Move=Failed，玩家仍在 Hidden、位置不变 | ❌ **意外成功**：状态 Hidden → Idle，实际位移 2m，AS Completed |
| 4 | Hidden | `plan_action_sequence_cmd [Interact] → start`（柜子最近） | AS Completed，Interact=Done，玩家 Hidden → Idle、瞬移到 exit anchor | ❌ AS 标记 Interact=Done，但玩家仍在 Hidden、未瞬移 |
| 5 | Idle | `plan_action_sequence_cmd [Interact, Move(≥2m)] → start` | Interact=Done(进 Hidden)，Move=Failed(被 Hidden 拦截)，AS=Aborted | ✅ 与预期一致 |

测试 5 的通过说明：单条 AS 内**已开始执行后**遇到 IImmovableState，`ExecuteMoveAction` 的守卫是有效的。问题集中在 **AS 启动瞬间**与 **AS 主动停止时**。


## 2. 根因定位

逐行核对 `AIPlayer.cs` 与日志的对应执行路径。

### 2.1 `StartActionSequence`（行 1361-1411）

```csharp
try
{
    // 1.停止当前Action
    this.StopMovement(false);
    ChangeState("Idle");            // ← 关键问题行
    // 2.替换 ActionSequence ...
    mCurActionSequenceRuntime.State = ActionSequenceState.Executing;
    this.ExecuteCurAction();
    // 4.发送 [动作序列确认开始执行结果]成功
}
```

`StopMovement(false)` 内部（行 568-571）：

```csharp
// 不强制切回 Idle，以避免破坏躲藏 / 死亡 / 击晕等语义。
if (!IsImmovable)
    ChangeState("Idle");
```

— 守卫正确：Hidden 时不切。但**紧接着 1378 行裸 `ChangeState("Idle")`** 把守卫覆盖：

1. `HiddenState.OnExit` 触发，`PlayerBase.OnHiddenExit` 还原 `RigidbodyConstraints2D`、重新启用所有缓存的 Renderer。
2. `IsImmovable`、`IsUndetectable`、`IsInvulnerable` 同步变 false。
3. 然后 `ExecuteCurAction` → 分发到具体 Execute*Action，此时 IsImmovable 已为 false，**任何后续守卫都已失效**。

### 2.2 测试 3 失败路径（Move）

`ExecuteMoveAction`（行 1520-1604）入口（行 1523）：

```csharp
if (IsImmovable) { ... 反馈失败 ... return; }
```

由于 1378 行已经把状态切成 Idle，`IsImmovable == false`，守卫不命中 → 正常进入 `ConditionEvaluator` → 玩家位移 2m → AS 完成。日志中可见状态 `Hidden → Idle → Move → Idle`、位置 `(−4.74, 0.97) → (−2.74, 0.97)`，与「意外成功」匹配。

### 2.3 测试 4 失败路径（Interact）

`ExecuteInteractAction`（行 1662-1720）的开头（行 1691-1693）：

```csharp
// Hidden 等 IImmovableState 下不切回 Idle, 以保留躲藏状态
if (!IsImmovable)
    ChangeState("Idle");
(bool success, string result) = SceneObjManager.Instance.Interact(this.gameObject);
```

注意：这里的守卫**只防本节点自己再切一次 Idle**，并不能阻止上游 1378 行已经造成的状态切换。

实际执行序：

1. 1377 `StopMovement(false)`：Hidden → (守卫生效)不切。
2. 1378 `ChangeState("Idle")`：Hidden → Idle，`OnHiddenExit` 触发（解冻 + 启用 Renderer）。
3. 1692 `if (!IsImmovable)`：此时 IsImmovable=false → 进入分支，再切一次 Idle（空操作）。
4. `SceneObjManager.Interact` → 命中 `Cabinet.Interact`。
5. `Cabinet.Interact` 内部判断 `player.GetStateName() == "Hidden"`：**为 false**（已被强制 Idle）→ 走「**进入**柜子」分支：瞬移到 enter anchor + `ChangeState("Hidden")`。
6. AS 标记 Interact=Done，AS Completed。

最终现象：日志显示 Interact=Done、AS Completed，玩家**仍在 Hidden**（因为又被重新装入了柜子）；位置也没变（enter anchor 与 exit anchor 在柜子里几乎重合，且 Hidden 期间被 FreezeAll 锁定）。

### 2.4 `StopActionSequence`（行 1449-1474）

```csharp
this.StopMovement(false);
ChangeState("Idle");                // ← 与 StartActionSequence 同样的两连行
mCurActionSequenceRuntime.State = ActionSequenceState.Aborted;
```

本次测试未直接触发 `stop_action_sequence_cmd`，但代码隐患等价：如果玩家在 Hidden 时通过工具主动停 AS，会被强制退柜。

### 2.5 `ExecuteWaitAction`（行 1605-1660）

行 1659：`ChangeState("Idle");` 无守卫。与 `ExecuteInteract/Select/Input` 形成不一致（后者均有 `if (!IsImmovable)` 守卫）。本次未触发，但属于同类隐患。

### 2.6 完整的「裸 ChangeState("Idle")」扫描结果

`rg 'ChangeState\("Idle"\)' Src/IndependentAgentProject -n` 共 22 处。逐一分类如下：

| # | 位置 | 类型 | 处理 |
|---|---|---|---|
| 1 | `AIPlayer.cs:76,88,1046,1052` | 动作执行 hook 内（`Update` 与 `FollowTarget` 完成回调） | **保留**：这是「执行完成 / 跟随目标消失」后的正常回收，调用前 IImmovableState 不会出现 |
| 2 | `AIPlayer.cs:570,1692,1743,1792` | 已有 `if (!IsImmovable)` 守卫 | 保留 |
| 3 | `AIPlayer.cs:1378` | **StartActionSequence 内裸切** | **F1: 删除** |
| 4 | `AIPlayer.cs:1464` | **StopActionSequence 内裸切** | **F2: 删除** |
| 5 | `AIPlayer.cs:1659` | **ExecuteWaitAction 末尾裸切** | **F3: 加 `if (!IsImmovable)` 守卫** |
| 6 | `CharaBase.cs:44`, `PlayerBase.cs:30/141`, `HumanPlayer.cs:65`, `SceneObjBase.cs:75`, `EnemyBase.cs:107/118/135/180`, `MovingPlatformAuto.cs:63`, `MovingPlatformTrigger.cs:63`, `Cabinet.cs:47` | 非 AS 链路 | 保留（均属于 Cabinet 主动退柜、Enemy 巡逻完成、平台抵达终点等正常切换） |

结论：本次只需改 #3/#4/#5 三处。其它代码点要么已守卫、要么是「业主显式退出 IImmovableState」的正确入口（如 Cabinet 主动 `player.ChangeState("Idle")`）。

## 3. 改动点

| # | 文件 | 行 | 改动 | 理由 |
|---|---|---|---|---|
| F1 | `AIPlayer.cs::StartActionSequence` | 1378 | **删除** `ChangeState("Idle");` | `StopMovement(false)` 内部已有 IImmovableState 守卫；本行覆盖了守卫 |
| F2 | `AIPlayer.cs::StopActionSequence` | 1464 | **删除** `ChangeState("Idle");` | 同 F1；用户主动停 AS 不应解除 Hidden / Dead / Stunned 等保护语义 |
| F3 | `AIPlayer.cs::ExecuteWaitAction` | 1659 | `ChangeState("Idle");` → `if (!IsImmovable) ChangeState("Idle");` | 与 ExecuteInteract / ExecuteSelect / ExecuteInput / StopMovement 守卫语法对齐；防御性 |
| F4 | `CharaBase.cs` 修改 `OnFollowExit` 默认实现 | 行 72 | 从空体改为 `TargetFollowing = null;` | 旧有 Bug：`StopMovement` / `StartActionSequence` 强切状态后 `TargetFollowing` 字段残留。`TargetFollowing` / `FollowState` 均归 `CharaBase` 持有，状态退出清场由基类统一负责，派生类不需重复实现；`AIPlayer.OnFollowFixedUpdate` 行 1045 / 1051 内的清空保留为防御性双保险，可不动。 |

### 3.1 F2 语义说明（用户重点关注）

「StopActionSequence 在 Hidden 时不切 Idle」是否会留下副作用？逐项分析：

- **当前 AS 已经在执行某个 Action**：
  - 若是 Move/Follow：进入 AS 之前需要先切到 `Move`/`Follow` 状态，这本身就要求 IsImmovable=false。即不可能出现 「Hidden + AS 正在执行 Move」 的组合。如果真出现，那是更上游漏了 IImmovableState 守卫，应在那里修。
  - 若是 Interact/Select/Input：这几个 Execute 函数自身已有 `if (!IsImmovable) ChangeState("Idle");` 守卫，本来就不会真正切。AS 状态由 `mCurActionSequenceRuntime.State = Aborted` 直接置位，逻辑闭环。
  - 若是 Wait：F3 修完后同样不会切。
- **AS 已经处于 Idle 等待 / Aborted / Completed**：当前并无 Action 在跑，`StopMovement(false)` 内的守卫会跳过切换；删 1464 行后状态保持。
- **风险点 1**：未来若新增「无视玩家状态强制中止 AS」的需求，应由调用方/Cabinet 自己显式 `ChangeState("Idle")`，而不是由 AS 通用入口越权决定。

结论：F2 是「**正确收紧**」，符合 v0.21.7 主方案语义（IImmovableState 状态拥有唯一退出权）。

### 3.2 F3 是否必须

本轮日志没触发 Wait → 严格说 F3 是「**防御性收紧**」，并非阻塞本次回归。但保留 F3 的理由：

- 现状不一致：5 个 Execute*Action 中 4 个有守卫，唯独 Wait 没有。
- 修法成本=单行；不修则下一次有 AS 含 Wait 的回归就会复现同类问题。
- Wait 的语义本就「等待若干秒/位移条件成立」，与 Hidden 不冲突，没有理由强制切回 Idle。

如果用户偏好极小改动，可以把 F3 推到独立 fix，本 fix 仅含 F1/F2。**默认建议含 F3**。

### 3.3 F4 选址说明（旧 Move/Follow → AS 残留字段清理）

**问题**：`CharaBase.TargetFollowing` 字段在「正在 Follow → 被 `StopMovement` / `StartActionSequence` 强切到 Idle / Move 等」路径下不会被清理。后续 `<你的状态>` 渲染或 `follow_target_cmd` 校验可能出现「我还在跟随某人但状态不是 Follow」的错乱。

**候选清理点**（按耦合度排序，由低到高）：

| 选址 | 优点 | 缺点 |
|------|------|------|
| A. **`CharaBase.OnFollowExit` 默认实现里清**（选定） | `TargetFollowing` / `FollowState` 均归 `CharaBase` 持有，状态退出清场由所有权方负责，对所有派生类（AIPlayer / HumanPlayer / EnemyBase 等）一次到位 | 改基类需要确认无派生类把「保留 TargetFollowing 跨状态」当语义 — 经全工程 grep 仅 AIPlayer / RuntimeInfoRenderer / ActionRuntime 读取，且后两者仅在 Follow 上下文中使用，安全 |
| B. `AIPlayer.OnFollowExit` override 清 | 局部改动，最小 | 不覆盖未来 EnemyBase 等其它 CharaBase 派生类；与字段定义位置错层 |
| C. `StopMovement` 内统一清 | 集中处理 | 漏盖 Hurt / Die；`StopMovement` 已承担「停 Action + 停 AS + 切 Idle」三职，再加耦合 |
| D. AS 启动 / 停止入口分别清 | 仅 AS 路径 | 漏盖 `Move()`、`Hurt`、`Die` 等非 AS 路径强切场景；与「跟随目标」语义无关 |

**选定方案 A**：把 `CharaBase.OnFollowExit` 的默认实现从空体改为 `TargetFollowing = null;`。理由：

1. **字段归属对齐**：`TargetFollowing` 在 `CharaBase` 定义、`FollowState` 在 `CharaBase` 注册，清理逻辑放在同层。
2. **覆盖所有派生类**：`AIPlayer` / `HumanPlayer` / 未来 `EnemyBase` 等任何切出 Follow 的入口都会触发 `OnFollowExit`，无需在派生类逐个 override。
3. **与 v0.21.7 主方案语义一致**：参考 `PlayerBase.OnHiddenExit` 在状态退出时还原 physics constraints / renderer 的做法，本次只是把同一模式套用到 `OnFollowExit` 上。
4. **`OnFollowFixedUpdate` 内的 `TargetFollowing = null;` 保留为防御**：避免「目标已被销毁，但状态机还停留在 Follow」的极端帧序问题；不冲突。

伪代码：

```csharp
public virtual void OnFollowExit()
{
    TargetFollowing = null;
}
```

派生类若有特殊清场需求，仍可 override 后 `base.OnFollowExit();`。

**风险提示**：
- 若未来希望「短暂 Move 后自动恢复 Follow」之类的语义，`TargetFollowing` 不应在退出 Follow 时清。但当前没有这类需求，AIPlayer 也不存在「Move 之后回到 Follow」的自动机制，结论：不阻塞。
- `FollowMinDistance` / `FollowMaxDistance` 是否一并清？倾向**不清**，因为它们是「下一次 follow_target_cmd 的默认参数候选」语义弱，不影响渲染或校验；保留与现状一致。


本轮日志没触发 Wait → 严格说 F3 是「**防御性收紧**」，并非阻塞本次回归。但保留 F3 的理由：

- 现状不一致：5 个 Execute*Action 中 4 个有守卫，唯独 Wait 没有。
- 修法成本=单行；不修则下一次有 AS 含 Wait 的回归就会复现同类问题。
- Wait 的语义本就「等待若干秒/位移条件成立」，与 Hidden 不冲突，没有理由强制切回 Idle。

如果用户偏好极小改动，可以把 F3 推到独立 fix，本 fix 仅含 F1/F2。**默认建议含 F3**。

## 4. 修复后预期行为

### 4.1 与日志（2026-06-28_09-29-39.log）的对照

| 测试 | 修复后预期 Tool 反馈 | 触达分支 |
|---|---|---|
| 3 | `[动作序列确认开始执行结果]成功: 共计1个动作` 后立即 `[动作序列执行中断][动作中断]当前处于 Hidden 状态, 无法移动。`，AS=Aborted，Move=Failed | `StartActionSequence` 不再裸切 → `ExecuteMoveAction` 行 1523 `IsImmovable` 守卫命中 → `OnActionFinished` → AS Aborted |
| 4 | `[动作序列确认开始执行结果]成功: 共计1个动作` 后 `[交互结果]你从柜子里出来了。`，Interact=Done，AS=Completed；玩家位置 = exit anchor，状态 Idle | `StartActionSequence` 不再裸切 → `ExecuteInteractAction` 守卫保持 Hidden → `Cabinet.Interact` 看 `StateName == "Hidden"` 走「离开柜子」分支 → 由 `Cabinet` 自己显式 `player.ChangeState("Idle")` |
| 1, 2, 5 | 无变化（保持已通过状态） | — |

### 4.2 状态机不变量

修复后下列不变量在 AS 启停链路上严格成立：

1. **IImmovableState 唯一退出权**：处于 `Dead` / `Hidden` / `Stunned` 时，状态只能由该状态对应的 owner（`CharaBase.Die` 自己、`Cabinet.Interact`、未来的 `Stunned` 复活逻辑）显式退出。AS 框架不再具备「越权解除 IImmovableState」的副作用。
2. **AS 启停幂等性**：在 IImmovableState 下 `start_action_sequence_cmd` 和 `stop_action_sequence_cmd` 不会修改玩家 FSM 状态，仅修改 `mCurActionSequenceRuntime.State`。
3. **`StopMovement(false)` 的守卫不再被旁路**。

## 5. 测试用例

复用 v0.21.7 主方案的 5 项联调（小明 + 第一关 + 柜子），追加 1 项静态检查与 1 项隐患复现：

| # | 状态前置 | 操作 | 预期 |
|---|---|---|---|
| F-AS-1 | Hidden | `move_cmd right 2` | 失败：`你正躲在柜子里, 无法移动。`，玩家状态/位置不变 |
| F-AS-2 | Hidden | `interact_cmd`（柜子最近） | 成功：`你从柜子里出来了。`，状态 → Idle，位置 = exit anchor |
| F-AS-3 | Hidden | `plan_action_sequence_cmd [Move(right, ≥2m)] → start` | AS Aborted，Move=Failed，提示 `[动作中断]当前处于 Hidden 状态, 无法移动。`，玩家仍在 Hidden、位置不变、Renderer 全禁用 |
| F-AS-4 | Hidden | `plan_action_sequence_cmd [Interact] → start` | AS Completed，Interact=Done，玩家 Hidden → Idle，位置 = exit anchor，Renderer 全恢复 |
| F-AS-5 | Idle | `plan_action_sequence_cmd [Interact, Move(≥2m)] → start` | Interact=Done（进 Hidden），Move=Failed（被 Hidden 拦截），AS=Aborted |
| F-AS-6 | Hidden | `plan_action_sequence_cmd [Wait(2s)] → start` | （F3 收紧后）Wait=Done 或 Failed 均可，玩家**全程保持 Hidden**（不退柜） |
| F-AS-7 | Hidden + 任意 AS 正在 Aborted/Completed | `stop_action_sequence_cmd` | 工具返回成功，玩家保持 Hidden（F2 收紧）|
| F-AS-8 | Idle → `move_cmd right 5`（执行中，已位移约 1m，状态 Move） | `plan_action_sequence_cmd [Wait(2s)] → start` | 旧 Move runtime Aborted（既有行为，不发反馈）；新 AS 在 Idle 开始执行 Wait；2s 后 AS Completed，玩家保持 Idle |
| F-AS-9 | Idle → `move_cmd right 5`（执行中，状态 Move） | `plan_action_sequence_cmd [Move(left, ≥2m)] → start` | 旧 Move runtime Aborted；新 Move 从当前位置起算位移 2m，AS Completed，状态序列 Move→Idle→Move→Idle |
| F-AS-10 | Idle → `follow_target_cmd 小红` 执行中（状态 Follow，`TargetFollowing == 小红`） | `plan_action_sequence_cmd [Move(right, ≥2m)] → start` | F4 命中：`OnFollowExit` 触发，`TargetFollowing` 清空；旧 follow runtime Aborted；新 Move 正常执行；AS 完成后 `<你的状态>` 中**不再出现「正在跟随」语义** |
| F-AS-11 | Idle → `follow_target_cmd 小红` 执行中 | `move_cmd left 2` | F4 命中：`StopMovement` 内切回 Idle 时 `OnFollowExit` 触发，`TargetFollowing` 清空；新 `move_cmd` 正常执行；后续状态文本无残留 |

### 5.1 静态检查（自测，无需 Unity）

```
rg "ChangeState\(\"Idle\"\)" Src/IndependentAgentProject -n
```

预期：保留下来的所有 `ChangeState("Idle")` 调用，必须满足下列任一：

- 处于 IImmovableState 不可能成立的回收路径（如 Move/Follow 完成回调）；
- 调用前有 `if (!IsImmovable)` 守卫；
- 是 IImmovableState 的合法退出 owner（如 `Cabinet.Interact` 主动退柜）。

修复前共 22 处，修复后 19 处（删除 2，改造 1）。F4 仅新增一个 override 方法，不影响 `ChangeState("Idle")` 计数。

### 5.2 日志期望关键行（联调时核对）

测试 3：

```
[动作序列确认开始执行结果]成功: 共计1个动作
[动作序列执行中断][动作中断]当前处于 Hidden 状态, 无法移动。
```

测试 4：

```
[动作序列确认开始执行结果]成功: 共计1个动作
[交互结果]你从柜子里出来了。
```

测试 4 完成后下一轮 prompt 的 `<你的状态># 状态:Idle`，且坐标 = exit anchor。

## 6. 风险与回滚

| 风险 | 评估 | 缓解 |
|---|---|---|
| F1/F2 引入「AS 启动/停止时未切 Idle」副作用 | 已逐项分析 §3.1，正常状态下 `StopMovement(false)` 守卫即可切回 Idle；IImmovableState 下本就不该切 | 测试 F-AS-3..F-AS-7 全覆盖 |
| F3 在历史 AS（含 Wait）回归中行为变化 | Wait 不切 Idle 的唯一观察差异是「Hidden 时 Wait 不退柜」，与新设计一致 | F-AS-6 显式验证 |
| F4 在「短暂 Move 后想自动回到 Follow」场景下提前清空 | 当前 AIPlayer 没有此类自动恢复机制；FollowMinDistance/MaxDistance 保留 | F-AS-10 / F-AS-11 显式验证清空生效 |
| 误改其它 `ChangeState("Idle")` 点 | 已在 §2.6 完整扫描分类 | 静态检查 §5.1 兜底 |

**回滚方案**：单次 git revert 本 fix 提交即可，无文件结构 / 接口变更。

## 7. 实现步骤（确认后执行）

1. 改 `AIPlayer.cs` 行 1378：删除 `ChangeState("Idle");`。
2. 改 `AIPlayer.cs` 行 1464：删除 `ChangeState("Idle");`。
3. 改 `AIPlayer.cs` 行 1659：`ChangeState("Idle");` → `if (!IsImmovable) ChangeState("Idle");`。
4. 在 `CharaBase.cs::OnFollowExit` 默认实现内写 `TargetFollowing = null;`（从空体改为带清场，所有派生类自动受益）。
5. 跑 `rg "ChangeState\(\"Idle\"\)" Src/IndependentAgentProject -n` 复核 §5.1 不变量。
6. Unity 编译，无 CS 错误；玩家走一遍 F-AS-1..F-AS-11。
7. 把回归日志按测试编号留档到 `Src/PythonServer/logs/prompts/小明/`，挑 F-AS-3 / F-AS-4 / F-AS-10 三段贴回本文件 §8。
8. 更新本方案状态为「已实现」，并在 `DevDocs/v0.21.7/solution.md` 状态行追加「fix_1 已实现」。

## 8. 实现记录

| 日期 | 内容 |
|------|------|
| 2026-06-28 | 起草方案；待用户确认后再改 `AIPlayer.cs` |
| 2026-06-28 | 用户审核通过，落地 F1/F2/F3/F4：<br>- F1：`AIPlayer.cs::StartActionSequence` 删除 `StopMovement(false);` 之后的裸 `ChangeState("Idle");`，留下 fix_1 说明注释。<br>- F2：`AIPlayer.cs::StopActionSequence` 同样删除裸 `ChangeState("Idle");`。<br>- F3：`AIPlayer.cs::ExecuteWaitAction` 末尾 `ChangeState("Idle");` 改为 `if (!IsImmovable) ChangeState("Idle");`，并补注释与同类 Execute*Action 守卫风格对齐。<br>- F4：用户建议把清场放到字段所有者 `CharaBase` 层；遂将 `CharaBase.OnFollowExit` 默认实现从空体改为 `TargetFollowing = null;`，AIPlayer 不再 override。<br>静态检查 `rg "ChangeState\("Idle"\)" AIPlayer.cs` 11 处全部归类为「完成回调 / 已有 IsImmovable 守卫」，无裸调用残留。`ReadLints` AIPlayer.cs + CharaBase.cs 均 No linter errors。 |
| 2026-06-28 | 联调日志 `logs/prompts/小明/2026-06-28_14-32-42.log` 5/5 通过：<br>- 测试 1（Hidden + `move_cmd` / `follow_target_cmd`）：均返回 `你正躲在柜子里, 无法移动。`，状态保持 Hidden。<br>- 测试 2（Hidden + `interact_cmd`）：`你从柜子里出来了。`，状态 Hidden → Idle。<br>- 测试 3（Hidden + `AS[Move, Interact]`）：AS Aborted、Move=Failed(`[动作中断]当前处于 Hidden 状态, 无法移动。`)、Interact=Todo，状态保持 Hidden。**F1 命中**。<br>- 测试 4（Hidden + `AS[Interact]`）：AS Completed、Interact=Done，状态 Hidden → Idle。**F1 命中**：移除裸切后 `Cabinet.Interact` 看到的仍是 Hidden，走「离开柜子」分支，未触发误判进柜。<br>- 测试 5（Idle + `AS[Interact, Move]`）：Interact=Done 进 Hidden，Move=Failed 被 `IsImmovable` 守卫拦截，AS=Aborted。<br>状态推到「已实现」。 |
