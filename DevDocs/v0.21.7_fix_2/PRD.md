# PRD — v0.21.7_fix_2 EnemyBase 问题汇总修复

> **状态**：已实现（验收通过）
> **对应需求**：`requirements/EnemyBase问题汇总.md`
> **关联方案**：`solution_enemybase_issues_fix.md`
> **关联主版本**：`DevDocs/v0.21.7/PRD.md`、`DevDocs/v0.21.7/solution.md`
> **最后更新**：2026-06-28

---

## 1. 背景与目标

v0.21.7 主版本完成 EnemyBase + Cabinet + Hidden 后，针对 EnemyBase 的 code review 与场内测试发现 4 处问题（详见 `requirements/EnemyBase问题汇总.md`）：

1. 巡逻点高于地面时，EnemyBase 接近巡逻点会"原地跳一下"。位移方式 (`transform.position` 直接写) 与 `PlayerBase.OnMoveFixedUpdate` / `CharaBase.OnFollowFixedUpdate` 的"水平 velocity 推进"约定不一致，需要统一。
2. Chase 退出后立刻往巡逻点走，缺少"困惑"过渡。
3. `OnVisionEnter` 用 `StateName == "Stunned" || "Dead" || "Chase"` 字符串穷举，可读性与扩展性差。
4. `mInteractionZones` 在 Inspector 留空时仍能从 Back 区背刺成功，运行行为符合设计意图；但 `SceneObjBase` 当前 `[Header("交互区域（留空则使用自身Collider）")]` 的文案与实际不一致（实际是"留空 = 自动收集子物体 InteractionZone；只有收集后仍为空才退化到自身 Collider"），需要同步修正。

目标：在不引入新协议、不动 Python、不影响其它已落地特性的前提下，把 1/2/3 修干净；把 4 的 Header 文案修正到与实际行为一致，避免使用者继续误读。

## 2. 范围

### 2.1 本期包含

- 修复 EnemyBase 巡逻 / 追击位移会改写 y 导致原地跳的视觉 bug；与 `PlayerBase.OnMoveFixedUpdate` / `CharaBase.OnFollowFixedUpdate` 的水平 velocity 推进约定统一。
- 在 Chase 退出（玩家离开视野 / 追丢）后插入一段 Idle 等待，再切向**最远**巡逻点（Chase 通常在玩家附近终止，朝远端走相当于重新扫一段最长的巡逻路径，比立刻回最近点更接近"恢复巡逻"的视觉直觉）。**复用 `mWaitTime`** 作为等待时长，不新增计时器字段。
- 重构 `OnVisionEnter` 的状态判断：使用 `SceneObjBase.IsImmovable` 替换"Stunned/Dead"两段字符串比较，Chase 保留为字符串去重。
- 修正 `SceneObjBase.mInteractionZones` 的 `[Header(...)]` 文案与 XML 注释，使其与"留空 = 自动收集子物体 InteractionZone；收集后仍为空才退化到自身 Collider"的实际行为一致。
- 同步在 `DevDocs/v0.21.7/solution.md` 顶部「后续修复」追加 fix_2 链接。

### 2.2 本期不包含

- `SceneObjBase.mInteractionZones` 的语义切换（黑名单 → 白名单）。若有必要后续在 `需求池/backlog.md` 立独立条目。
- 新增 / 修改协议（`Tools/message.proto`、Python 工具）。
- EnemyBase 的攻击 / 背刺判定逻辑、视野 / 攻击 / 背刺子物体的几何结构调整。
- Cabinet / Hidden / ActionSequence 等 v0.21.7 与 fix_1 已落地的逻辑。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 关卡设计师 | 在 Unity 关卡里把巡逻点 Transform 放在比地面稍高的位置（贴脚部 / 头顶等不严格落地） | EnemyBase 不会在接近巡逻点时出现"原地跳"视觉异常 |
| 关卡设计师 / 玩家 | 玩家进入 EnemyBase 视野触发追击，再走出视野 | 敌人保持原朝向"困惑"约 `mWaitTime` 秒后才返回**最远**巡逻点，体验上不像即时知情 |
| 关卡设计师 / 玩家 | 在 EnemyBase Inspector 上不显式拖入 `mInteractionZones`，仅在子物体上挂 `mBackstabZone(ZoneTag=Back)` | 玩家从 Back 区交互能成功背刺（确认为预期行为，文档化） |
| 后续开发者 | 给 EnemyBase 新增一个"硬直 / 击晕分裂"类的 `IImmovableState` | 不需要再去改 `OnVisionEnter` 里的状态白名单 / 黑名单字符串，新状态自动被拦截 |

## 4. 功能需求

### 4.1 巡逻 / 追击位移修复（问题一）

- `EnemyBase.OnMoveFixedUpdate`：必须只在 X 方向施加位移，通过 `mRigidbody2D.velocity = new Vector2(dir * mPatrolSpeed, mRigidbody2D.velocity.y)`，与 `PlayerBase.OnMoveFixedUpdate`、`CharaBase.OnFollowFixedUpdate` 的水平 velocity 推进约定保持一致；y 由 Rigidbody 物理处理；不允许直接写 `transform.position`。
- `EnemyBase.OnChaseFixedUpdate`：同上，水平方向用 `dirChase * mChaseSpeed`，y 不动。
- 抵达巡逻点判定：以 X 距离 `Mathf.Abs(dx) < kArriveEpsilonX` 为准；`kArriveEpsilonX` 在源码中以常量声明，默认 `0.05f`。
- 抵达后清零水平速度，垂直速度保留；并清 `mIsReturningToPatrol` / `mTargetPoint`。

### 4.2 Chase 退出后的 Idle 缓冲（问题二）

- 不引入新的计时器字段；**复用 `mWaitTime`** 作为"困惑停顿"时长。
- `OnChaseExit`（Chase 退出的唯一收口，覆盖玩家离开视野 / 追丢两条路径）必须：
  - `mChaseTarget = null;`
  - 水平 velocity 归零（垂直保留）；
  - 调 `SetTargetToFarthestPatrolPoint()`——只设 `mTargetPoint` + `mIsReturningToPatrol = true`，**不切状态、不翻 `TurnBack`**，面朝翻转延迟到 Idle 真正切 Move 那一刻，避免追丢瞬间立刻扭头。
- `OnVisionExit` 与 `OnChaseFixedUpdate` 追丢分支（`mChaseTarget` 为 null / Dead / Undetectable）仅 `ChangeState("Idle")`；其余资源释放与目标设置交给 `OnChaseExit`。
- `OnIdleEnter`：重置 `mWaitTimer = 0f`，保证 Chase → Idle 切换后困惑停顿计时从零开始。
- `OnIdleUpdate`：现有 `mWaitTimer` 累计逻辑保留；触发 `mWaitTimer >= mWaitTime` 时分支：
  - 若 `mIsReturningToPatrol == true`：`TurnBack` 翻向 `mTargetPoint` 后 `ChangeState("Move")`（`mTargetPoint` 已由 `OnChaseExit` 设置）；
  - 否则调 `SetNextPatrolTarget()` 走正常巡逻路径。
- `OnMoveFixedUpdate` 抵达目标后必须清 `mIsReturningToPatrol = false; mTargetPoint = null;`。
- `OnStunnedEnter` 必须清 `mIsReturningToPatrol = false; mTargetPoint = null;`（FSM 顺序「旧 OnChaseExit → 新 OnStunnedEnter」，Stunned 路径终态正确；Chase → Stunned 路径上"设了又清"的一次冗余写可接受）。

### 4.3 视野进入判定重构（问题三）

- `EnemyBase.OnVisionEnter` 第一行必须用 `if (IsImmovable) return;` 覆盖 Stunned / Dead 等当前及未来的 `IImmovableState`。
- 第二行保留 `if (StateName == "Chase") return;` 用于 Chase 自身去重。
- `OnVisionExit` 与 `OnAttackEnter` 保留 `if (StateName != "Chase") return;` 不变。

### 4.4 `SceneObjBase.mInteractionZones` Header 文案修正（问题四）

- 把 `SceneObjBase.mInteractionZones` 的 `[Header(...)]` 从 `"交互区域（留空则使用自身Collider）"` 改为更准确的 `"交互区域（留空则自动收集子物体上的所有 InteractionZone；若收集后仍为空，则降级使用自身 Collider）"`。
- 同步给 `mInteractionZones` 字段补一段 XML 注释，说明两层语义：
  1. Awake 时若列表为空，从子物体 `GetComponentsInChildren<InteractionZone>()` 自动收集；
  2. 自动收集后列表仍为空时，`IsCharacterInAnyZone` / `GetNearestZoneDistance` 才退化到自身 Collider。
- **不修改任何运行逻辑**。「留空 = 强白名单」语义切换若有必要后续在 `需求池/backlog.md` 立独立条目。

## 5. 非功能需求

- **回归**：v0.21.7 / fix_1 已落地的 Cabinet / Hidden / ActionSequence × IImmovable 行为不得回归。
- **可读性**：替换字符串穷举后，`OnVisionEnter` 行内注释保留中文，说明"Stunned/Dead 走 IsImmovable，Chase 单独去重"。
- **可扩展性**：新增 `IImmovableState` 的实现类时，`OnVisionEnter` 不需要再改。
- **可观测性**：本期无需新增日志；已有的 ChangeState 日志已足够追踪状态切换时序。

## 6. 验收标准

- [ ] T-1.1：巡逻点 y 高于敌人脚下 0.5~1m，敌人接近巡逻点时无原地跳，平稳贴地走过。
- [ ] T-1.2：巡逻点 y 与敌人 y 一致时巡逻行为无回归。
- [ ] T-1.3：玩家 y 与敌人 y 不一致（玩家在低一级平台）时进入视野触发 Chase，敌人追击只在水平方向推进，垂直由 Rigidbody 物理处理，无"原地跳"。
- [ ] T-2.1：玩家进入视野触发 Chase；走出视野后，敌人**保持原朝向** Idle ≈ `mWaitTime` 秒（与巡逻停顿一致），再翻向**最远**巡逻点开始 Move。
- [ ] T-2.2：玩家在 Chase 期间进入 Hidden（`IUndetectable`），`OnChaseFixedUpdate` 追丢分支同样走 T-2.1 行为。
- [ ] T-2.3：Idle 等待进行中若玩家再次进入视野，立刻进入 Chase；后续 `OnChaseExit` 再次正常进入"等够 `mWaitTime` → 回最远点"流程。
- [ ] T-2.4：Idle 等待进行中若敌人被背刺进入 Stunned，`mIsReturningToPatrol` 与 `mTargetPoint` 清零，状态保持 Stunned。
- [ ] T-3.1：敌人处于 Stunned 时玩家进入视野，不切 Chase。
- [ ] T-3.2：敌人处于 Dead 时玩家进入视野，不切 Chase。
- [ ] T-3.3：敌人处于 Chase 时多次 `OnVisionEnter` 触发，不重复 ChangeState。
- [ ] T-3.4：敌人处于 Idle 时玩家进入视野，正常切 Chase。
- [ ] T-4.1：`mInteractionZones` Inspector 留空但 `mBackstabZone(ZoneTag=Back)` 配置正常时，玩家从 Back 区交互可背刺成功（确认为预期）。
- [ ] T-4.2：`mInteractionZones` 显式拖入 `mBackstabZone` 一个的情况下，背刺仍正常（无回归）。
- [ ] T-4.3：`SceneObjBase.mInteractionZones` 的 `[Header(...)]` 文案与 XML 注释已更新到"自动收集 + 降级到自身 Collider"的描述。
- [ ] 文档：`solution_enemybase_issues_fix.md` 与 `DevDocs/v0.21.7/solution.md` 顶部「后续修复」对 fix_2 的引用已更新。

## 7. 待确认问题

- [ ] 问题二复用 `mWaitTime`（默认 1s）作为 Chase 退出后的"困惑停顿"时长是否合适？体验上需要 1s vs 1.5s vs 2s 的话 Inspector 单独配 `mWaitTime` 即可（与正常巡逻停顿一致；若希望两者分开，再立 backlog 条目）。
- [ ] T-1.2 是否需要专门走一次"等高巡逻"的对照测试？建议保留为最小回归。
- [ ] 问题四后续是否需要在 `需求池/backlog.md` 立一个"`mInteractionZones` 改为强白名单"的候选条目？倾向：暂不立，等真正出现误用再立。

---

*本文档由 Cursor Agent 根据 `requirements/EnemyBase问题汇总.md` 与 `solution_enemybase_issues_fix.md` 生成，确认前请勿直接据此改代码。*
