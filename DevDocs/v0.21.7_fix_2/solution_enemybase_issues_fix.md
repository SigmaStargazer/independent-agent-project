# v0.21.7_fix_2 EnemyBase 问题汇总修复

> 状态: 已实现（验收通过）
> 最后更新: 2026-06-28
> 关联主方案: DevDocs/v0.21.7/solution.md
> 需求来源: DevDocs/v0.21.7_fix_2/requirements/EnemyBase问题汇总.md
> 影响范围:
>   - Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/EnemyBase.cs
>   - （问题四仅文档/约定，不动代码）
> 风险等级: 低（行为局部调整，不改协议、不改 FSM 框架）

## 0. 概览

本版修四个 EnemyBase 相关问题：

| # | 问题 | 类型 | 影响 | 是否改代码 |
|---|---|---|---|---|
| 一 | 巡逻点离地时 EnemyBase 接近巡逻点会原地跳一下 | Bug / 统一位移约定 | 中（巡逻视觉异常） | 是（EnemyBase + 与 PlayerBase 对齐） |
| 二 | Chase 退出后立即往最远巡逻点走，建议先 Idle 一段时间 | 体验 | 中 | 是（最小改动：复用现有 Idle 等待节奏） |
| 三 | `OnVisionEnter` 用 `StateName == "Stunned"/"Dead"/"Chase"` 遍历字符串太丑 | 重构 / 可读性 | 小 | 是 |
| 四 | `mInteractionZones` 未在 Inspector 设置时仍能通过 ZoneTag=Back 背刺成功 | 文档 / Header 注解过期 | 小（运行行为正确，但 Header 误导使用者） | 是（仅改 SceneObjBase 头注，不改运行逻辑） |

—

四个问题以下分别给出根因与方案。

---

## 1. 问题一：接近高于地面的巡逻点时原地跳

### 1.1 现象

- 巡逻点 Transform 设在距地面有一定高度（例如 y 高出地面 0.3~1.5m）。
- EnemyBase 向巡逻点移动到接近时，画面上"原地跳一下"，随即落回地面继续。
- 通常发生在抵达巡逻点的最后阶段。

### 1.2 根因

看 `OnMoveFixedUpdate`（EnemyBase.cs 行 117-134）：

```csharp
public override void OnMoveFixedUpdate()
{
    if (mTargetPoint == null) { ChangeState("Idle"); return; }
    transform.position = Vector3.MoveTowards(
        transform.position, mTargetPoint.position,
        mPatrolSpeed * Time.fixedDeltaTime);
    if (Vector3.Distance(transform.position, mTargetPoint.position) < 0.02f)
    {
        transform.position = mTargetPoint.position;
        ...
    }
}
```

两层问题叠加：

1. `Vector3.MoveTowards` 直接写 `transform.position`，包含 y 分量。当巡逻点 y 高于敌人脚下，敌人 y 会被 MoveTowards 平滑抬高，**绕过 Rigidbody2D 物理与地面碰撞**——这就是"跳一下"的视觉来源。
2. 吸附阶段 `transform.position = mTargetPoint.position` 一脚把 y 钉到巡逻点高度，下一帧 Rigidbody 重力把它拉下来，形成"上抬再落回"的来回。

巡逻点 y 与敌人 y 不一致时此现象必现。`OnChaseFixedUpdate` 同样直接 `MoveTowards`，存在相同隐患（在玩家 y ≠ 敌人 y 时复现）。

对照 `CharaBase.OnFollowFixedUpdate`（行 40-71）与 `PlayerBase.OnMoveFixedUpdate`（行 97-101），它们的位移写法都是 `mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y)`——只动水平速度、保留垂直速度、X 方向由 `TurnBack(dir)` 同步翻转面朝。EnemyBase 当前用 `transform.position` 是少数派，**与现有"基于速度的水平位移"约定不一致**。

### 1.3 方案

统一对齐 `PlayerBase.OnMoveFixedUpdate` / `CharaBase.OnFollowFixedUpdate` 的"水平 velocity 推进 + y 交给 Rigidbody"约定。

- 完成条件改为只比较 X 距离 `Mathf.Abs(dx) < kArriveEpsilonX`；
- 不再写 `transform.position`，水平速度归零（保留垂直分量）后切 Idle 即可；
- `kArriveEpsilonX = 0.05f`（比当前 0.02 放宽，留给浮点抖动余量）；
- `moveSpeed` 字段已经在 CharaBase 上，EnemyBase 当前的 `mPatrolSpeed` / `mChaseSpeed` 与之并列。本期不动 `mPatrolSpeed` / `mChaseSpeed` 的归属（仍保留为 EnemyBase 私有 Serialize 字段），只是改用「方向 × 速度 → `velocity.x`」的写法，让两者口径一致。

### 1.4 改动点

`EnemyBase.cs`：

- 加常量 `private const float kArriveEpsilonX = 0.05f;`
- `OnMoveFixedUpdate` 改写为 PlayerBase 风格：
  ```csharp
  if (mTargetPoint == null) { ChangeState("Idle"); return; }
  float dx = mTargetPoint.position.x - transform.position.x;
  if (Mathf.Abs(dx) < kArriveEpsilonX)
  {
      mRigidbody2D.velocity = new Vector2(0f, mRigidbody2D.velocity.y);
      mIsReturningToPatrol = false;
      mTargetPoint = null;
      ChangeState("Idle");
      return;
  }
  float dir = Mathf.Sign(dx);
  TurnBack(dir);
  mRigidbody2D.velocity = new Vector2(dir * mPatrolSpeed, mRigidbody2D.velocity.y);
  ```
- `OnChaseFixedUpdate` 同样改为水平速度推进：
  ```csharp
  if (mChaseTarget == null || mChaseTarget.IsDead || mChaseTarget.IsUndetectable)
  {
      // 见问题二：不再立刻调 SetTargetToFarthestPatrolPoint，由 OnChaseExit 统一处理 + Idle 倒计时驱动
      mChaseTarget = null;
      mIsReturningToPatrol = true;
      mPostChaseTimer = 0f;
      ChangeState("Idle");
      return;
  }
  float dxChase = mChaseTarget.transform.position.x - transform.position.x;
  float dirChase = Mathf.Sign(dxChase);
  TurnBack(dirChase);
  mRigidbody2D.velocity = new Vector2(dirChase * mChaseSpeed, mRigidbody2D.velocity.y);
  ```
- `OnMoveEnter` 内 `TurnBack(...)` 已用 `(mTargetPoint.position - transform.position).x` 计算方向，保留无需改。
- 进 Idle 时 `OnIdleEnter` 已经把水平速度归零（行 86-88），无需追加。

### 1.5 影响范围

- 仅 EnemyBase 自身的巡逻 / 追击位移；写法与 PlayerBase / Follow 对齐，**消除"基于 transform.position 的位移"这一遗留少数派**。
- 不影响视野 / 攻击 / 背刺等 Trigger（子物体 Collider2D 跟随父级 transform）。
- 玩家、Cabinet、ActionSequence 等无关。

---

## 2. 问题二：Chase 退出后，先 Idle 一段时间再回最远巡逻点

### 2.1 现象与期望

当前行为：玩家离开视野，`OnVisionExit` 立刻：

```csharp
mChaseTarget = null;
ChangeState("Idle");
MoveToFarthestPatrolPoint();   // ← 紧接着切到 Move
```

`MoveToFarthestPatrolPoint` 内部 `ChangeState("Move")`，**等于没有真正进入过 Idle**。玩家刚消失敌人就立刻掉头回巡逻路径，体验上像是"AI 即时知情"。

期望：玩家离开视野 → 敌人原地"困惑"一小段时间 → 再朝**最远**巡逻点走（Chase 通常在玩家附近终止，朝远端走相当于重新扫一段最长的巡逻路径，比立刻回最近点更接近"恢复巡逻"的视觉直觉）。

### 2.2 方案（简化版）

不为"困惑停顿"单独引入计时器。复用现有的 **"Idle 等待 → Move"** 巡逻节奏：

1. 在 `OnChaseExit`（Chase 退出的唯一收口，覆盖 `OnVisionExit` 与 `OnChaseFixedUpdate` 追丢分支）里：
   - `mChaseTarget = null;`
   - **直接更新 `mTargetPoint` 为最远的巡逻点**（不切状态、不翻面朝）；
2. `OnVisionExit` / `OnChaseFixedUpdate` 追丢分支仅 `ChangeState("Idle")`，其余交给 `OnChaseExit`。
3. `OnIdleUpdate` 的现有逻辑会按 `mWaitTime` 等待后再 `SetNextPatrolTarget`——但我们已在第 1 步显式设了 `mTargetPoint` 为最远点，所以希望 Idle 等待结束后直接走向该点，而不是被 `SetNextPatrolTarget` 替换为巡逻序列里的下一个点。

为此引入一个轻状态位：

- 把现有 `mIsReturningToPatrol` 字段重用为"下一次离开 Idle 应直接走到 `mTargetPoint`，跳过 `SetNextPatrolTarget`"标记。
- `OnIdleUpdate` 累计 `mWaitTimer >= mWaitTime` 后：
  - 若 `mIsReturningToPatrol == true`：`TurnBack` 翻向 `mTargetPoint` 后直接 `ChangeState("Move")`（`mTargetPoint` 已经在第 1 步指定），不调 `SetNextPatrolTarget`；
  - 否则走原本的 `SetNextPatrolTarget` 流程。
- `OnMoveFixedUpdate` 抵达目标后已有 `mIsReturningToPatrol = false; mTargetPoint = null;`，下一轮回到正常巡逻节奏，无需额外处理。

效果：Chase 退出 → 保持原朝向 Idle 等 `mWaitTime` 秒（与巡逻停顿一致）→ 翻向最远巡逻点并走过去 → 回归原巡逻循环。**复用 `mWaitTime` 作为"困惑停顿"时长**，不再新增 `mPostChaseIdleTime`。

### 2.3 改动点

`EnemyBase.cs`：

- 新增 `SetTargetToFarthestPatrolPoint`：仅负责"找出离当前位置最远的巡逻点、设置 `mTargetPoint`、`mIsReturningToPatrol = true`"。**不切状态、不翻 `TurnBack`**——面朝翻转延迟到 `OnIdleUpdate` 真正切 Move 那一刻，避免追丢瞬间立刻扭头。
- `OnChaseExit`（Chase 退出的唯一收口）：`mChaseTarget = null;` + `SetTargetToFarthestPatrolPoint();` + 水平 velocity 归零。
- `OnVisionExit` 与 `OnChaseFixedUpdate` 追丢分支：仅 `ChangeState("Idle")`，资源释放与目标设置由 `OnChaseExit` 统一处理。
- `OnIdleUpdate`：在 `mWaitTimer >= mWaitTime` 触发处分支——`mIsReturningToPatrol` 为 true 则 `TurnBack` + `ChangeState("Move")`，否则走 `SetNextPatrolTarget()`。
- `OnIdleEnter`：重置 `mWaitTimer = 0f`，保证 Chase → Idle 切换后困惑停顿计时从零开始。
- `OnStunnedEnter` 内追加 `mIsReturningToPatrol = false;` 与 `mTargetPoint = null;` 防御性清零（FSM 顺序「旧 OnChaseExit → 新 OnStunnedEnter」，Stunned 路径终态正确，"设了又清"的一次冗余写可接受）。

### 2.4 边界情形

| 场景 | 期望行为 |
|---|---|
| Idle 等待期间玩家再次进入视野 | `OnVisionEnter`（问题三改后）正常拦截 IsImmovable / Chase，进 Chase；`mWaitTimer` 在 `OnIdleEnter` 重置，`mIsReturningToPatrol` 在抵达目标或下次 `OnChaseExit` 重置 |
| Idle 等待期间被背刺进入 Stunned | `OnStunnedEnter` 清 `mIsReturningToPatrol / mTargetPoint`；保持 Stunned |
| 巡逻点列表为空 | `SetTargetToFarthestPatrolPoint` 在 `mPatrolPoints.Count == 0` 时清掉目标位与回归标记，Idle 阶段 `mTargetPoint == null`，`OnMoveFixedUpdate` 一进就直接 `ChangeState("Idle")`，自然停在原地 |

### 2.5 影响范围

- 仅 EnemyBase 内部状态机时序。
- 巡逻 / 玩家追击 / 背刺逻辑无回归。

---

## 3. 问题三：用接口 / 类型判定替代 `StateName` 字符串遍历

### 3.1 现象

`OnVisionEnter`（EnemyBase.cs 行 178-187）：

```csharp
public void OnVisionEnter(Collider2D other)
{
    if (StateName == "Stunned" || StateName == "Dead" || StateName == "Chase") return;
    ...
}
```

用三段字符串硬比较列举"不应再进入 Chase 的当前状态"。问题：

- 之后再加新状态（例如新的"昏迷/受伤"），要回头改这里加 `||`。
- 字符串口径分散：`Stunned` / `Dead` 与 `IUndetectableState` / `IImmovableState` 语义已经一致，再额外用字符串维护是重复来源。

### 3.2 现有接口能解决什么

| 接口 | 语义 | 当前实现者 |
|---|---|---|
| `IUndetectableState` | 不可被检测/追击 | `DeadState`, `HiddenState`, `StunnedState` |
| `IImmovableState` | 禁止主动移动 | `DeadState`, `HiddenState`, `StunnedState` |
| `IInvulnerableState` | 免疫致死/受伤型重生 | `DeadState`, `HiddenState` |

`Stunned` / `Dead` 都既是 `IUndetectableState` 又是 `IImmovableState`；Chase 自身不是 immovable 也不是 undetectable，而是"已经在追击中"——这是另一类语义。

### 3.3 方案

**A（推荐）**：用 `IsImmovable` 把"被击晕/死亡"统一起来，再单独处理 Chase。代码改为：

```csharp
public void OnVisionEnter(Collider2D other)
{
    if (IsImmovable) return;          // 涵盖 Stunned / Dead，未来加新的 IImmovableState 自动覆盖
    if (StateName == "Chase") return; // Chase 是"已经在追"的去重判断，与 immovable 无关
    ...
}
```

- 第一行用 `SceneObjBase.IsImmovable` 直接代替三段字符串中的 `Stunned` + `Dead`。语义对：被击晕/死亡都不应再进 Chase。
- 第二行的 Chase 判断保留为字符串。不引入新接口（`IInChaseState` 这种太窄）。

**B**：把 Chase 也抽成接口（如 `IAlreadyChasingState`）。当前只有 EnemyBase 有 Chase，新增接口收益小，PASS。

**C**：直接 `if (mCurState is StunnedState or DeadState or ChaseState) return;`。比字符串好但仍是穷举，不利于扩展，PASS。

采用 A。

附带处理：`OnVisionExit` 当前是 `if (StateName != "Chase") return;`——这是"我必须正处于 Chase 才会理 Exit"，与 A 语义不冲突；保留。

`OnAttackEnter` 也是 `if (StateName != "Chase") return;`——同上，保留。

### 3.4 改动点

`EnemyBase.cs::OnVisionEnter` 单方法替换为方案 A 写法。其余三处（`OnVisionExit` / `OnAttackEnter` / `Interact`）不变。

### 3.5 风险

- `IsImmovable` 的实现位于 `SceneObjBase`，已稳定服役于 HumanPlayer / AIPlayer / ActionSequence / fix_1。复用无新风险。
- 行为对等性：现实施 Stunned / Dead / Chase 列表与"`IImmovable || StateName == Chase`"在当前注册集合下完全等价（Stunned / Dead 都是 IImmovable；Chase 不是 IImmovable）。
- 未来加新的 `IImmovableState`（例如硬直/眩晕分裂出更多类型）会自动被拦截，是期望的扩展性。

---

## 4. 问题四：`mInteractionZones` 留空时仍能被 Back 区背刺成功——是预期行为（但 Header 注解需要更新）

### 4.1 现象重述

用户复现：在 EnemyBase Inspector 上 `mInteractionZones` 留空，子物体上挂了 `InteractionZone(ZoneTag="Back")` + Trigger Collider2D 的 `mBackstabZone`，玩家走到该子物体范围内交互，仍能成功背刺。

直觉上"我没在 Inspector 拖任何 InteractionZone，应该什么都不该触发"，所以怀疑是 Bug。

### 4.2 根因 / 行为解释

不是 Bug。链路：

1. **自动收集兜底**（`SceneObjBase.Awake` 行 67-69）：

   ```csharp
   if (mInteractionZones.Count == 0)
       mInteractionZones.AddRange(GetComponentsInChildren<InteractionZone>());
   ```

   Inspector 留空 → Awake 时把所有子物体上的 `InteractionZone` 拉进来。`mBackstabZone` 子物体上的 `InteractionZone(ZoneTag="Back")` 被自动加入。

2. **EnemyBase 自己也兜底**（`EnemyBase.Awake` 行 52-53）：

   ```csharp
   if (mBackstabZone != null && !mInteractionZones.Contains(mBackstabZone))
       mInteractionZones.Add(mBackstabZone);
   ```

   即使第 1 步关掉，只要 `mBackstabZone` 字段在 Inspector 拖好了，它也会被自动加入。两层兜底之间用 `Contains` 防重复，不会重入。

3. **交互判定**：`SceneObjManager.GetNearestInteractableObj` 用 `IsCharacterInAnyZone` 走 `mInteractionZones`；命中则 `EnemyBase.Interact` 用 `GetActiveZoneTag` 拿到 `"Back"` → 走背刺分支。

所以"Inspector 留空但 ZoneTag=Back 的子物体存在"时背刺成功是**两层自动收集合力的结果**，符合 `SceneObjBase` 的设计意图。

### 4.3 这是 Bug 还是设计

是设计。

- `SceneObjBase` 的约定一直是"`mInteractionZones` 留空 = 自动收集子物体上的全部 InteractionZone"，Merchant 等其他 SceneObj 也按这个语义工作。
- EnemyBase 自身再为 `mBackstabZone` 兜底一次，是为了"开发者忘了在子物体 InteractionZone 上设 ZoneTag、或忘了启用"时也能保底背刺。

误解来源：用户把 `mInteractionZones` 当成"白名单"——以为留空 = 黑名单。实际语义是"留空 = 自动收集子物体上的 InteractionZone（**不是**退化到自身 Collider）"。

### 4.4 SceneObjBase 文档需要同步修正（本期改）

当前 `SceneObjBase.mInteractionZones` 头注：

```csharp
[Header("交互区域（留空则使用自身Collider）")]
[SerializeField] protected List<InteractionZone> mInteractionZones = new List<InteractionZone>();
```

**"留空则使用自身 Collider"与实际行为不一致**：留空时实际是 `GetComponentsInChildren<InteractionZone>()` 自动收集子物体上的全部 InteractionZone；只有"自动收集后仍然为 0"且降级路径触发（`IsCharacterInAnyZone` / `GetNearestZoneDistance` 在 `mInteractionZones.Count == 0` 才退化到自身 Collider）才会用到自身 Collider。

本期把 Header 文案改为更准确的说法。建议：

```csharp
[Header("交互区域（留空则自动收集子物体上的所有 InteractionZone；若收集后仍为空，则降级使用自身 Collider）")]
[SerializeField] protected List<InteractionZone> mInteractionZones = new List<InteractionZone>();
```

同时给 `mInteractionZones` 加一段 XML 注释把"自动收集 + 降级到自身 Collider"两层语义写清，避免下次还有人疑惑。

### 4.5 候选改进（本期**不做**，仅记录）

如果未来想把语义改成"留空 = 不响应任何 Zone（强白名单）"：

- 删 `SceneObjBase.Awake` 中的 `if (mInteractionZones.Count == 0) ...` 自动收集；
- 删 EnemyBase 内部 `mBackstabZone` 的兜底逻辑（强制用户在 Inspector 显式拖入）；
- 修 Merchant 等已有依赖隐式收集的 SceneObj 配置；
- 同步刷新 Doc。

影响面较大，且与现有"留空 = 退化到自身 Collider"的多处降级路径耦合，本版按"约定确认 + Header 文案修正"处理，不改运行逻辑。后续如有需要在 `需求池/backlog.md` 立独立条目。

---

## 5. 综合改动清单

| 文件 | 改动概要 | 行为类别 |
|---|---|---|
| `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/EnemyBase.cs` | 问题一：`OnMoveFixedUpdate` / `OnChaseFixedUpdate` 改为水平 velocity 推进（与 `PlayerBase.OnMoveFixedUpdate` / `CharaBase.OnFollowFixedUpdate` 对齐），抵达判定改 X 距离 + 常量 `kArriveEpsilonX`；问题二：把"Chase 退出 → 设最远巡逻点 + 标 mIsReturningToPatrol"统一收到 `OnChaseExit`，`OnVisionExit` / `OnChaseFixedUpdate` 追丢分支仅 `ChangeState("Idle")`，`SetTargetToFarthestPatrolPoint` 不切状态也不翻面朝，`OnIdleUpdate` 等待结束时若 `mIsReturningToPatrol` 为 true 则 `TurnBack` + 切 Move（复用 `mWaitTime`，不新增计时器）；问题三：`OnVisionEnter` 改用 `IsImmovable` + Chase 字符串 | Bug 修复 / 体验调整 / 重构 |
| `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Base/SceneObjBase.cs` | 问题四：把 `mInteractionZones` 的 `[Header(...)]` 由"留空则使用自身Collider"改为"留空则自动收集子物体上的所有 InteractionZone；若收集后仍为空，则降级使用自身 Collider"，并补 XML 注释 | 文档 / 注解修正 |
| 文档 | 新增 `DevDocs/v0.21.7_fix_2/PRD.md`、`DevDocs/v0.21.7_fix_2/solution_enemybase_issues_fix.md`；更新 `DevDocs/v0.21.7/solution.md` 附录指向 fix_2 | 文档 |

无 protobuf / Python / 协议改动。

## 6. 自测计划

| 用例 | 前置 | 操作 | 期望 |
|---|---|---|---|
| T-1.1 | 巡逻点 y 高于敌人脚下 0.5~1m，至少两个点 | 敌人从远端开始巡逻 | 接近巡逻点时 **没有原地跳**，敌人沿地面平稳通过，Idle ↔ Move ↔ Idle 正常切换 |
| T-1.2 | 巡逻点 y 与敌人 y 一致 | 同上 | 回归无差异，与 fix 前视觉一致 |
| T-1.3 | 玩家 y 与敌人 y 不一致（例如玩家站在低一级平台上） | 玩家进入视野触发 Chase | 敌人追击只在水平方向推进，垂直方向交给 Rigidbody，不再"原地跳" |
| T-2.1 | 默认 `mWaitTime = 5s` | 玩家走入视野进 Chase → 走出视野 | Vision Exit 后**保持原朝向先 Idle ~`mWaitTime` 秒**（与正常巡逻停顿一致），再翻向**最远**巡逻点并走过去（Move） |
| T-2.2 | 玩家走入视野进 Chase → 进 Hidden（IUndetectable） | `OnChaseFixedUpdate` 追丢分支生效 | 同 T-2.1，等够 `mWaitTime` 秒后回**最远**巡逻点 |
| T-2.3 | Chase 退出 → Idle 等待中 | 玩家再次出现 / 进入视野 | 立刻重新进入 Chase；`mIsReturningToPatrol` 在 Chase 终结时仍能复位，不残留状态错乱 |
| T-2.4 | Chase 退出 → Idle 等待中 | 玩家走到 Back 区交互背刺 | 进入 Stunned，`mIsReturningToPatrol` / `mTargetPoint` 清零，不再恢复巡逻 |
| T-3.1 | 敌人 Stunned | 玩家进入视野范围 | 不切 Chase（`IsImmovable` 命中） |
| T-3.2 | 敌人 Dead | 同上 | 不切 Chase |
| T-3.3 | 敌人 Chase | 玩家未离开视野，重复触发 OnVisionEnter | 不重复 ChangeState（Chase==Chase 守卫） |
| T-3.4 | 敌人 Idle | 玩家进入视野 | 正常切 Chase |
| T-4.1 | EnemyBase Inspector `mInteractionZones` 留空，子物体 `mBackstabZone(ZoneTag=Back)` 配置正常 | 玩家从 Back 区交互 | 背刺成功（**确认是预期行为**） |
| T-4.2 | Inspector `mInteractionZones` 已拖入 `mBackstabZone` 一个 | 同上 | 背刺成功（无回归） |
| T-4.3 | SceneObjBase 源码层面 | 查看 `mInteractionZones` Header 与注释 | 文案为"留空则自动收集子物体上的所有 InteractionZone；若收集后仍为空，则降级使用自身 Collider"，不再写"留空则使用自身 Collider" |

无需 Python 联调；上述用例全部可在 Unity 关卡内自测覆盖。

## 7. 回滚方案

- 单文件 `EnemyBase.cs` 一次提交，回滚 = 单次 git revert。
- 文档单独提交，互不耦合。

## 8. 实现记录

| 日期 | 变更 |
|---|---|
| 2026-06-28 | 撰写本方案；待用户审核确认后实施。 |
| 2026-06-28 | 用户确认后实施 EnemyBase.cs（mWaitTime 默认 5s、kArriveEpsilonX 常量、OnMoveFixedUpdate / OnChaseFixedUpdate 改 velocity 推进、SetTargetToNearestPatrolPoint 仅设目标不切状态、OnVisionExit / OnChaseFixedUpdate 追丢分支统一走「设目标 + 切 Idle」、OnIdleUpdate 加 mIsReturningToPatrol 分支、OnIdleEnter 重置 mWaitTimer、OnVisionEnter 用 IsImmovable + Chase 字符串、OnStunnedEnter 清 mIsReturningToPatrol）与 SceneObjBase.cs（mInteractionZones Header 文案 + 详细 XML 注释）。Roslyn 静态检查无 lint。待 Unity 关卡内手工验证 T-1.* / T-2.* / T-3.* / T-4.*。 |
| 2026-06-28 (hotfix-1) | 联机测试发现两个问题，修复：1) Vision Exit 后敌人立刻扭头看巡逻点的违和感——把 `SetTargetToNearestPatrolPoint()` 调用从 `OnVisionExit` / `OnChaseFixedUpdate` 追丢分支统一搬到 `OnChaseExit`（Chase 退出的唯一收口），并删掉 `SetTargetToNearestPatrolPoint` 内部的 `TurnBack`，让面朝翻转延迟到 `OnIdleUpdate` 真正切 Move 那一刻；`OnStunnedEnter` 的清零仍生效，FSM 顺序保证 Stunned 路径终态正确。2) SceneObjBase 的 `mInteractionZones` Header 文案太长被 Inspector 截断——Header 改成「交互区域」短标题，长说明放进 `[Tooltip(...)]` 多行展示，XML 注释保留。Roslyn 静态检查无 lint。 |
| 2026-06-28 (hotfix-2) | 联机复测后产品决策调整：Chase 退出后改朝**最远**巡逻点移动（而不是最近）。`SetTargetToNearestPatrolPoint` 重命名为 `SetTargetToFarthestPatrolPoint`，内部 `minDist` 改为 `maxDist`，比较方向反转；`OnChaseExit` 调用点同步更新，并在注释里说明选择最远点的产品意图。Roslyn 静态检查无 lint。 |

