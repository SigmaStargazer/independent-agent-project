# 技术方案 — v0.22.21 HumanPlayer 高频换向时朝向错误

> **状态**：已验收
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-16

---

## 1. 方案概述

根因是 **「FSM 同状态去重」使 `OnMoveEnter` 不会在「Move → Move」换向时触发**，导致换向只更新了速度方向、未更新 `Scale.X`。

修复思路（**v8 方案**，取代 v4/v5/v6/v7）：

- **转向判断完全收敛到 `CharaBase` 的 FSM hook 内，由「当前速度方向（`velocity.x`）」推导**——`PlayerBase` / `AIPlayer` **只允许对 `velocity`（速度）赋值**，对速度、位移以外的任何属性（含 `transform.localScale.x`）**零赋值、零调用转向方法**。（Enemy 专属状态转向保留在 Enemy hook，见下）
- **禁止 `MoveDirection` 变量**：v5/v6 的 `MoveDirection` 方案全部作废。
- **零新增**：不新增任何方法、属性、接口、字段、常量；只允许修改现有 FSM hook（`OnXxxEnter` / `OnXxxUpdate` / `OnXxxFixedUpdate` / `OnXxxExit`）的实现。
- 转向落点：**`CharaBase.OnMoveFixedUpdate` 内判断当前速度方向并决定是否转向**（用户指定）。Move 状态下 `velocity.x` 每帧由意图移动代码覆盖，读到的就是意图速度方向，天然规避「受击击退反例」（v3 在全状态 `FixedUpdate` 无条件读 velocity 被否决，v8 只在 Move 状态 hook 内读）。
- **Enemy 专属的 Chase / Searching / Investigate / Alerted / Inspect / 站岗 6 状态**：状态 Hook 在 `EnemyBase` 内自注册、自维护，**转向保留在 Enemy 的这些状态 Hook 内**（用户已确认豁免，见 §3.3.4.4）。

详见 §3.3.4。

---

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | — | 无 |
| Unity | `CharaBase.cs`（覆写已有 `OnMoveFixedUpdate`：读 `velocity.x` 转向；`OnFollowFixedUpdate` 改为面向目标） | 修复 + 收敛（仅改现有 hook） |
| Unity | `PlayerBase.cs`（`OnMoveEnter` 删转向；`OnMoveFixedUpdate` 只写速度 + 调 base） | 修复（等价） |
| Unity | `AIPlayer.cs`（`OnFollowFixedUpdate` 删转向，调 base） | 重构（等价） |
| Unity | `EnemyBase.cs`（`OnMoveEnter` 删转向；`OnMoveFixedUpdate` 只写速度 + 调 base；Chase 等 6 状态转向保留在 Enemy hook，不改动） | 重构（等价） |
| Unity | `Merchant.cs`（无移动/翻转逻辑） | 无 |
| 协议 | `Tools/message.proto` | 无 |

---

## 3. 详细设计

### 3.1 关键代码与根因证据

**HumanPlayer.GetInput**（每次 Update 读取输入）：

```csharp
float horizontal = Input.GetAxisRaw("Horizontal");
if (horizontal != 0)
{
    moveRight = horizontal > 0;
    ChangeState("Move");
}
else
{
    ChangeState("Idle");
}
```

**SceneObjBase.ChangeState**（FSM 同状态去重）：

```csharp
public virtual void ChangeState(string stateName)
{
    if (StateName == stateName)
        return;   // ← 已是 Move 再 ChangeState("Move") 直接 return
    ...
}
```

**PlayerBase.OnMoveEnter**（唯一的翻转入口）：

```csharp
public override void OnMoveEnter()
{
    float dir = moveRight ? 1f : -1f;
    TurnBack(dir);
}
```

### 3.2 根因分析（完整场景枚举）

翻转只发生在 `OnMoveEnter`。而 `OnMoveEnter` 只有在状态**从非 Move 变为 Move** 时才会触发。

| 时序 | 输入 | 状态序列 | 速度方向 | 是否翻转 | 朝向结果 |
|------|------|----------|----------|----------|----------|
| 低频切换 | 右 → 左，中间有 Idle | `Move(右) → Idle → Move(左)` | 右→0→左 | 第二次 `Move` 触发 `OnMoveEnter` | ✅ 正确 |
| 高频切换（同帧内无 Idle） | 右 → 左 连续 | `Move(右) → Move(左)` | 右→左 | `ChangeState("Move")` 同状态 return，**不触发** `OnMoveEnter` | ❌ **向左却仍面朝右** |
| 高频切换（同帧内无 Idle） | 左 → 右 连续 | `Move(左) → Move(右)` | 左→右 | 同上，**不翻转** | ❌ **向右却仍面朝左** |
| 初始/还原后 | `scale_x=1` 但 `moveRight=false`，且持续在 Move | `Move(左)` | 左 | `OnMoveEnter` 已过去（或从未触发） | ❌ **持续错误，无法自愈** |

**关键点**：

1. `GetInput` 在每帧 `Update` 把 `moveRight` 改成最新输入，但翻转却延迟到 `OnMoveEnter`。二者之间存在**一帧以上的窗口**：该帧 `FixedUpdate`（`OnMoveFixedUpdate`）已经按新 `moveRight` 施加了速度，而朝向还停留在旧的 `Scale.X`。
2. `ChangeState("Move")` 在「已经是 Move」时被去重跳过，所以高频换向时翻转入口**永远不会再次被调用**。
3. `TurnBack` 本身逻辑正确（只有方向不一致才翻转），但因为没人调用它，翻转无从发生。
4. 一旦进入「`Scale.X` 与 `moveRight` 不一致且一直处于 Move」的状态，就会**无限持续**，因为没有自愈机制。

**模拟验证**（用等价逻辑脚本复现，20000~50000 帧）：

| 场景 | 错误朝向帧占比 |
|------|----------------|
| 每帧 Move 且随机换向（无 Idle 穿插，模拟高频） | ~25%（50000 帧中 12493 帧） |
| 换向前必有一帧 Idle（模拟低频） | 0%（20000 帧中 0 帧） |
| `scale_x=1` + `moveRight=false` 且持续 Move | 100%（永不纠正） |

模拟结果与用户观察完全一致：**高频切换出错、低频不出错、出错时 `Scale.X` 正负号错误**。

### 3.3 修复方案（v2，已被 §3.3.2 取代，保留供对照）

> ⚠️ **本节为 v2 方案（收敛到 `PlayerBase.OnMoveFixedUpdate`），已被 §3.3.2 的 v4 方案（Move/Follow 统一到 Chara 入口，Chase 保留在状态）取代。** 保留仅为记录演进与回退参考。历史背景：修复范围曾确认统一到 `CharaBase`（不再做仅限玩家的局部方案）。

**核心思路：把「依据移动意图翻转」收敛到 Chara 的**权威移动执行点**（`PlayerBase.OnMoveFixedUpdate`），让翻转不再依赖 `OnMoveEnter` 是否触发。**

`OnMoveFixedUpdate` 是所有基于 `moveRight` 移动的角色（`HumanPlayer` 键盘输入 / `AIPlayer` Move 工具 / `AIPlayer` ActionSequence MoveAction）共同汇聚的**每帧执行点**，不受 `ChangeState` 同状态去重影响。在这个单点按 `moveRight` 翻转，业务入口（`GetInput` / `Move` / `ExecuteMoveAction`）**零侵入**——它们只需设置 `moveRight` 并 `ChangeState("Move")`，翻转由 Chara 层自动完成，未来新增移动入口也自动被覆盖。

```csharp
// PlayerBase.OnMoveFixedUpdate（唯一集中执行点）
public override void OnMoveFixedUpdate()
{
    float dir = moveRight ? 1f : -1f;
    FaceByMoveDirection(moveRight);   // 朝向与速度同一帧一致
    mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
}
```

`OnMoveEnter` 里原有的 `TurnBack` 保留作为**幂等兜底**（`TurnBack` 本身已做方向一致性判断，重复调用无害）。

**路径覆盖关系**（实现时已确认）：

| 移动路径 | 是否进入 Move 状态 | 是否经过 `PlayerBase.OnMoveFixedUpdate` | 翻转是否被自动覆盖 |
|----------|-------------------|----------------------------------------|--------------------|
| `HumanPlayer.GetInput` → `ChangeState("Move")` | ✅ | ✅（未重写，继承 PlayerBase） | ✅ |
| `AIPlayer.Move`（Agent 工具）→ `ChangeState("Move")` | ✅ | ✅（未重写，继承 PlayerBase） | ✅ |
| `AIPlayer.ExecuteMoveAction`（ActionSequence）→ `ChangeState("Move")` | ✅ | ✅（未重写，继承 PlayerBase） | ✅ |
| `AIPlayer.OnFollowFixedUpdate`（Follow 状态） | ❌（Follow 状态） | ❌（走 `OnFollowFixedUpdate`） | 不适用——Follow 每帧 `TurnBack(dir)`，无此 bug |
| `EnemyBase` 巡逻/追击/调查 | ✅（Move 状态） | ❌（**重写** `OnMoveFixedUpdate`，用 `Mathf.Sign(dx)` 每帧 `TurnBack`） | 不适用——EnemyBase 每帧按实时目标翻转，无此 bug |

> **前置事实核查（已确认）**：
> 1. `transform.localScale.x` 在项目内**只被 `CharaBase.TurnBack` 一处写入**（`CharaBase.cs:21-30`），动画 / 相机 / 反馈等其它代码全部只**读** `IsRight`（`SceneObjAnimator.cs:147`、`CameraController.cs:48`、`AIPlayer.cs:593`、`SceneObjInfoMapper.cs:104`）。因此只要最终 `Scale.X` 正确，下游消费不受影响。
> 2. `HumanPlayer` / `AIPlayer` 均**未重写** `OnMoveFixedUpdate`，继承 `PlayerBase` 版本；`EnemyBase` **重写** `OnMoveFixedUpdate`，不经 `PlayerBase` 版本，保持自身每帧 `TurnBack` 逻辑（无 bug，不改动）。

### 3.3.1 两种自愈强度的优劣分析

翻转写法的两种强度（都基于 `CharaBase` 统一方法），核心差异在「**是否信任历史朝向**」：

**写法 1：条件翻转（保留 `TurnBack` 语义，`scale.x = -scale.x`）**

```csharp
protected void FaceByMoveDirection(bool moveRight)
{
    float dir = moveRight ? 1f : -1f;
    // 只有方向不一致才翻转（等价 TurnBack）
    if (dir < 0 && transform.localScale.x > 0
        || dir > 0 && transform.localScale.x < 0)
    {
        var ls = transform.localScale;
        ls.x = -ls.x;
        transform.localScale = ls;
    }
}
```

| 维度 | 评价 |
|------|------|
| 对现有行为的影响 | **最小**。翻转次数与现状完全一致（每次换向恰好翻转一次），`IsRight` 变化次数不变，下游（动画过渡、相机、AI 反馈）看到的朝向变化时序与现在相同，**回归风险最低** |
| 高频换向修复 | ✅ 修复。因翻转收敛到每帧执行的 `OnMoveFixedUpdate`，不再被 `OnMoveEnter` 去重跳过 |
| 历史残留自愈 | ❌ 不保证。若 `Scale.X` 已与方向不一致（非换向造成，如外部还原、初始化残留），且方向保持不变，则不翻转 → **持续错误**。但此类残留在本角色当前不存在（已确认 `Scale.X` 仅 `TurnBack` 一处写入） |
| 可维护性 | 逻辑与 `TurnBack` 重叠，需保留两者并保证语义一致，略有重复 |

**写法 2：强制对齐（无条件 `scale.x = ±|scale.x|`）**

```csharp
protected void FaceByMoveDirection(bool moveRight)
{
    float dir = moveRight ? 1f : -1f;
    var ls = transform.localScale;
    ls.x = dir * Mathf.Abs(ls.x);   // 无条件与方向一致，保留原绝对幅度
    transform.localScale = ls;
}
```

| 维度 | 评价 |
|------|------|
| 对现有行为的影响 | **相对较大**。每帧移动意图下都会**写一次** `Scale.X`（即使方向没变）。`IsRight` 值不变（因为符号同向），但 `transform` 的 dirty 标记 / 动画参数 SetFloat 每帧重复触发，理论上开销略增；更重要的是**任何"特殊朝向"都会被覆盖** |
| 高频换向修复 | ✅ 修复（同写法 1） |
| 历史残留自愈 | ✅ **彻底自愈**。不依赖「方向不一致」判断，任何来源造成的符号错误在下一帧移动时立即纠正 |
| 可维护性 | 逻辑更简单（无判断），且**未来若出现"外部还原 Scale"的 bug，移动时自动恢复**，防御性更强 |
| 风险点 | ① 若未来某角色需要"移动中保持非标准朝向"（本角色当前无此需求），会被覆盖；② 每帧强制写可能与动画 / 其它系统对 `localScale` 的写产生顺序耦合（当前代码无其它写入，暂安全） |

**对比小结**

| 对比项 | 写法 1（条件翻转） | 写法 2（强制对齐） |
|--------|--------------------|--------------------|
| 高频换向修复 | ✅ | ✅ |
| 历史残留自愈 | ❌ | ✅ |
| 对现有行为影响 | 最小 | 较大（每帧写、覆盖特殊朝向） |
| 回归风险 | 低 | 中 |
| 未来防御性 | 无 | 强 |
| 代码复杂度 | 略繁（与 TurnBack 重叠） | 更简 |

**推荐：写法 1（条件翻转）为主，写法 2 为可选加固。**

理由：
1. 本项目 `Scale.X` 已被确认**只有 `TurnBack` 一处写入**，不存在其它会污染符号的来源——「历史残留」在当前架构下不会自然发生，写法 2 的"彻底自愈"收益在当前是**理论性**的。
2. 写法 1 与现状行为完全兼容、回归风险最低，符合「先修已知 bug、不过度设计」的原则。
3. 若后续发现确有外部改 `Scale` 的路径，再升级到写法 2（二者仅差一个判断，迁移成本极低）。

> **待确认**：写法 1 或写法 2，请在确认时指明；默认按写法 1 实施。

### 3.3.2 架构演进（已废弃，被 v8 取代）：反转逻辑按「状态归属层」收敛

> ⚠️ **本节（v4/v5）已废弃**，被 §3.3.4 的 v8 方案取代。保留仅为记录演进。核心废弃原因：v5 的 `EnemyBase.OnMoveFixedUpdate` 仍写 `MoveDirection = Mathf.Sign(dx)`，**Chara 子类直接参与面朝方向判断**，违反用户红线（子类只允许对速度赋值、禁止 `MoveDirection`、零新增）。

> **状态**：方案中（用户提出设计方向，未改代码）
> **演进过程**：
> - 初版：把翻转分散接入 `HumanPlayer.GetInput` / `AIPlayer.Move` / `AIPlayer.ExecuteMoveAction` 三处业务入口（用户否决：破坏架构、分散）。
> - v2：收敛到 `PlayerBase.OnMoveFixedUpdate`（用户否决：仍绑在 Move 状态、Player/Enemy 各写一套）。
> - v3：从 `velocity.x` 自足推导、彻底统一到 `CharaBase.FixedUpdate`（用户否决：**受击击退反例**——角色受击时产生速度但不应转向，面朝方向≠速度方向）。
> - v4：入口统一到 Chara（`FaceByMoveDirection`），但调用点仍分散在各状态 hook（用户否决：调用点没按状态归属层收敛，Move 的反转仍写在 PlayerBase）。
> - **v5（当前）**：**反转逻辑的调用点按「状态归属层」收敛**——状态注册在哪个层，反转逻辑就放哪个层的 hook：
>   - **Move**（状态注册在 `SceneObjBase`，所有 Chara 共享；面朝概念从 `CharaBase` 开始）→ 反转逻辑放 **Chara 的 Move hook**（`CharaBase.OnMoveFixedUpdate`）
>   - **Follow**（状态注册在 `CharaBase`）→ 反转逻辑放 **Chara 的 Follow hook**（`CharaBase.OnFollowFixedUpdate`）
>   - **Chase / Searching / Investigate / Alerted / Inspect**（状态注册在 `EnemyBase`）→ 反转逻辑放 **Enemy 的 hook**

#### 3.3.2.1 核心思想与原则

**原则 1：面朝方向 ≠ 速度方向。** 面朝方向由「意图性移动方向」驱动（玩家输入、走向目标、跟随目标）；速度可能是外力（受击击退、碰撞弹开）造成，**不承载转向意图**。因此不能从 `velocity.x` 自足推导。

> 反例（v3 被否决的直接原因）：角色正面或背面受击，会朝受击方向相反的方向位移（产生速度），但角色**不应转向**。若 Chara 统一读 velocity 翻转，受击时就会错误掉头。

**原则 2（本轮核心）：「统一」= 反转逻辑的调用点按状态归属层收敛。** 面朝方向概念从 `CharaBase` 开始才有（`IsRight` / `TurnBack` / `FaceByMoveDirection` 都在 Chara），因此：
- 状态注册在**越底层**，其反转逻辑就越该放**底层 hook**。
- Move 状态在 `SceneObjBase` 注册（所有 Chara 共享）→ 反转放 `CharaBase.OnMoveFixedUpdate`。
- Follow 状态在 `CharaBase` 注册 → 反转放 `CharaBase.OnFollowFixedUpdate`。
- Chase / Searching / Investigate 等在 `EnemyBase` 注册 → 反转放 `EnemyBase` 的 hook。

**原则 3：** 反转逻辑（面朝方向维护）与**移动执行**（速度/到达判定）分离。反转由状态归属层的 hook 统一处理；移动执行仍由各状态自身负责（方向来源、速度值、到达判定是各状态自己的语义）。

#### 3.3.2.2 现状：状态归属层 + 转向散落清单

| 状态 | 注册层 | 当前反转执行点 | 方向来源 | 归属层 |
|------|--------|---------------|----------|--------|
| **Move** | `SceneObjBase`（Awake 强制注册） | `PlayerBase.OnMoveFixedUpdate`（moveRight）/ `EnemyBase.OnMoveFixedUpdate`（mTargetPoint） | `moveRight` / `Sign(dx)` | **Chara**（Move hook） |
| **Follow** | `CharaBase` | `CharaBase.OnFollowFixedUpdate`（三分支各自 TurnBack） | `Sign(delta)` | **Chara**（Follow hook，已在此层，收敛调用） |
| Chase | `EnemyBase` | `EnemyBase.OnChaseFixedUpdate` | `Sign(dx)` | **Enemy** |
| Searching | `EnemyBase` | `EnemyBase.OnSearchingFixedUpdate` | `Sign(dx)` | **Enemy** |
| Investigate | `EnemyBase` | `EnemyBase.OnInvestigateFixedUpdate` | `Sign(dx)` | **Enemy** |
| Alerted / Inspect / 站岗 | `EnemyBase` | 各 Enter / Update（站定、张望、朝向偏好） | 目标点相对位置 | **Enemy** |

**为什么 Move 的反转逻辑该放 Chara 而非 PlayerBase**：
- `MoveState` 在 `SceneObjBase.Awake()` 强制注册（`SceneObjBase.cs:76`），**不是 PlayerBase 专属**——Player 和 Enemy 都用 Move 状态。
- 「面朝方向」概念（`IsRight` / `TurnBack` / `FaceByMoveDirection`）全部定义在 `CharaBase`（`CharaBase.cs:15,21,37`）——**Chara 是"有面朝概念"的最底层**。
- 因此 Move 的反转逻辑（按移动意图维护面朝方向）应放 `CharaBase.OnMoveFixedUpdate`，Player/Enemy 共享，不再各自实现。

#### 3.3.2.3 统一设计（v5：反转逻辑按状态归属层收敛）

**核心思路：反转逻辑（面朝方向维护）与移动执行（速度/到达）分离。** 状态归属层的 hook 统一处理反转；移动执行仍由各状态自己负责（方向来源、速度值、到达判定是自己的语义）。

**Chara 提供「移动意图方向」统一来源**（面朝概念在 Chara，方向由 Chara 维护）：

```csharp
// CharaBase —— 新增字段：当前移动意图方向（-1 左 / +1 右 / 0 不面向）
protected float MoveDirection { get; set; }
```

**Chara 的 Move hook 统一反转**（`CharaBase.OnMoveFixedUpdate` 覆写 `SceneObjBase` 的空实现）：

```csharp
// CharaBase.OnMoveFixedUpdate —— Move 状态的反转逻辑统一在这里
public override void OnMoveFixedUpdate()
{
    // 面朝方向概念在 Chara：按移动意图统一翻转
    if (MoveDirection != 0f)
        FaceByMoveDirection(MoveDirection > 0f);
}
```

**Player 系**（`PlayerBase.OnMoveFixedUpdate`）：只维护方向 + 施加速度，**不再自己写反转**，调 `base.OnMoveFixedUpdate()` 由 Chara 统一翻转：

```csharp
public override void OnMoveFixedUpdate()
{
    MoveDirection = moveRight ? 1f : -1f;   // 维护移动意图方向
    base.OnMoveFixedUpdate();               // Chara 统一按 MoveDirection 翻转
    mRigidbody2D.velocity = new Vector2(MoveDirection * moveSpeed, mRigidbody2D.velocity.y);
}
```

**Enemy 系 Move（巡逻）**（`EnemyBase.OnMoveFixedUpdate`）：同样只维护方向 + 速度/到达，反转交给 Chara：

```csharp
public override void OnMoveFixedUpdate()
{
    if (mTargetPoint == null) { ChangeState("Idle"); return; }   // 到达/防御判定仍是 Enemy 自己的
    float dx = mTargetPoint.position.x - transform.position.x;
    if (Mathf.Abs(dx) < kArriveEpsilonX) { /* 到达逻辑不变 */ ... return; }
    MoveDirection = Mathf.Sign(dx);        // 维护移动意图方向
    base.OnMoveFixedUpdate();              // Chara 统一按 MoveDirection 翻转
    mRigidbody2D.velocity = new Vector2(MoveDirection * mPatrolSpeed, mRigidbody2D.velocity.y);
}
```

**Follow 的反转**（`CharaBase.OnFollowFixedUpdate` 已在此层）：把三分支里分散的 `TurnBack(dir)` 收敛为按 `MoveDirection` 统一翻转：

```csharp
// CharaBase.OnFollowFixedUpdate（已在此层，仅收敛反转调用）
if (distance > FollowMaxDistance)
{
    MoveDirection = Mathf.Sign(delta);
    FaceByMoveDirection(MoveDirection > 0f);   // 统一翻转（替换原来散落的 TurnBack）
    mRigidbody2D.velocity = new Vector2(MoveDirection * moveSpeed, mRigidbody2D.velocity.y);
}
else if (distance < FollowMinDistance) { ... }
else { /* 保持距离内：只面向目标，不移动 */ }
```

**Enemy 专属状态（Chase / Searching / Investigate / Alerted / Inspect / 站岗）**：反转逻辑保留在 `EnemyBase` 的 hook（它们注册在 Enemy 层，不纳入 Chara 统一）：

```csharp
// EnemyBase.OnChaseFixedUpdate —— 保留在 Enemy 层（不调用 Chara 统一反转）
float dir = Mathf.Sign(dx);
TurnBack(dir);                            // Enemy 自己的反转逻辑
mRigidbody2D.velocity = new Vector2(dir * mChaseSpeed, mRigidbody2D.velocity.y);
```

**为什么这样划分**（对应 §3.3.2.1 原则 2）：
- Move / Follow 状态注册在底层（SceneObjBase / CharaBase），面朝概念从 Chara 开始 → 反转逻辑放 Chara 的 hook。
- Chase / Searching / Investigate 等注册在 Enemy → 反转逻辑放 Enemy 的 hook。
- 这就是"统一"：**反转逻辑的调用点按状态归属层收敛，不再按"谁实现了某个状态"分散。**

#### 3.3.2.4 关键设计点与取舍

1. **为什么不从 `velocity.x` 自足推导**：受击击退反例——角色受击产生速度但不应转向。速度不区分「意图性移动」与「外力」，而转向只应响应前者。因此反转由**移动意图方向**（`MoveDirection`）驱动，而非读速度。

2. **统一到什么程度**：反转逻辑的调用点按**状态归属层**收敛——Move / Follow 的反转放 Chara 的 hook，Chase / Searching / Investigate 等 Enemy 专属状态的反转放 Enemy 的 hook。不追求"所有状态的反转都收敛到 Chara"，而是"每个状态的反转逻辑放在它注册的那一层"。

3. **`MoveDirection` 字段 vs `FaceByMoveDirection(bool)` 入口**：`MoveDirection`（float）是方向来源的统一抽象，让 Player（`moveRight`）与 Enemy（`Sign(dx)`）都写同一个字段；`FaceByMoveDirection(bool)`（v2 已有）是翻转执行入口。两者配合：状态维护 `MoveDirection`，Chara hook 调 `FaceByMoveDirection` 翻转。

4. **高频换向 bug 的修复保证**：`CharaBase.OnMoveFixedUpdate` 每帧执行（不受同状态去重影响），按 `MoveDirection` 翻转 → 根治「Move→Move 不翻转」。Player 的 `OnMoveFixedUpdate` 调 `base.OnMoveFixedUpdate()` 后即获得统一反转。

5. **速度施加 / 到达判定不统一**：这些是各状态自己的移动语义（Player 用 `moveSpeed`，Enemy 用 `mPatrolSpeed` + 到达判定），保留在各状态 hook，不强行上浮到 Chara。反转与移动执行分离。

6. **`MoveDirection` 的生命周期**：只在 Move / Follow 等移动状态内被赋值；静止状态（Idle / 站定）不赋值（保持 `0`），Chara hook 检测 `MoveDirection != 0f` 才翻转，不会误翻转。

7. **不引入 `FixedUpdate` 重写**：v5 不需要 `CharaBase` 重写 `FixedUpdate`（v3 的 `FaceByVelocity` 已否决）。反转放在状态 hook 内（`OnMoveFixedUpdate` / `OnFollowFixedUpdate`），由状态显式触发，符合"状态知道自己是意图移动"的语义。

#### 3.3.2.5 影响面与风险

| 项 | 说明 |
|----|------|
| 改动文件 | `CharaBase.cs`（新增 `MoveDirection` 属性 + 覆写 `OnMoveFixedUpdate` 统一反转 + `OnFollowFixedUpdate` 收敛反转调用）· `PlayerBase.cs`（`OnMoveFixedUpdate` 改维护 `MoveDirection` + 调 `base`）· `EnemyBase.cs`（`OnMoveFixedUpdate` 改维护 `MoveDirection` + 调 `base`；Chase/Searching/Investigate 等保留在 Enemy 层）· `AIPlayer.cs`（Follow 已由 Chara 处理） |
| 高频换向 bug 修复 | 由 `CharaBase.OnMoveFixedUpdate` 每帧按 `MoveDirection` 统一反转保证（不受同状态去重影响），Player/Enemy 的 Move 都走这里 |
| 行为一致性 | Move/Follow 反转时机与现状一致（每帧幂等）；`IsRight` 变化时序不变，下游动画/相机/反馈不受影响 |
| 回归风险 | 低（Move 反转收敛到 Chara 一层，Player/Enemy 逻辑等价；Enemy 专属状态保留原样） |
| 与已有实现的关系 | v5 在 v2 已实现的 `FaceByMoveDirection` 基础上，把反转调用点按状态归属层收敛（Move/Follow→Chara，Enemy 专属→Enemy），不引入 CharaBase.FixedUpdate |

> **待确认问题汇总（v5 方案）**：
> 1. `MoveDirection`（float 属性）作为 Chara 统一方向来源——Player 写 `moveRight?1:-1`、Enemy 写 `Sign(dx)`，是否采纳？
> 2. `CharaBase.OnMoveFixedUpdate` 统一反转，Player/Enemy 的 `OnMoveFixedUpdate` 改为「维护方向 + 调 base + 施加速度」——是否采纳？
> 3. Follow 的反转收敛为按 `MoveDirection` 统一翻转（替换三分支里散落的 `TurnBack(dir)`）——是否采纳？
> 4. Chase / Searching / Investigate / Alerted / Inspect / 站岗 的反转全部保留在 `EnemyBase` hook——是否采纳？

> ⚠️ **v5 方案已废弃**。用户红线：**Chara 的任何子类只参与「速度及速度方向的修改」，不得出现任何「直接参与面朝方向判断」的逻辑**（点名否决 v5 中 `EnemyBase.OnMoveFixedUpdate` 的 `MoveDirection = Mathf.Sign(dx)`）。v5 的「子类写 `MoveDirection`」本质仍是子类参与面朝方向判断，故整体废弃，见 §3.3.3。

#### 3.3.3 架构演进（已废弃，v6 被 v8 取代）：面朝方向完全收敛到 Chara，子类只参与速度

> ⚠️ **本节（v6）已废弃**，被 §3.3.4 的 v8 方案取代。保留仅为记录演进。核心废弃原因：v6 让子类调用 `FaceToTarget` / `FaceToward` / `IntentMove` 等转向入口——**用户判定这仍是「子类参与转向」**（子类决定转向时机/方向），且 v6 仍保留 `MoveDirection` 概念，并新增了方法。用户红线：**子类只允许对 `velocity`（速度）赋值；禁止 `MoveDirection`；禁止新增任何方法/属性；转向只在 Chara 的 FSM hook 内从 `velocity.x` 推导。**

> **状态**：方案中（用户提出设计方向，未改代码）
> **演进过程**：
> - v4：入口统一到 Chara（`FaceByMoveDirection`），但调用点仍分散在各状态 hook（用户否决：调用点没按状态归属层收敛）。
> - v5：反转调用点按「状态归属层」收敛（Move/Follow→Chara，Enemy 专属→Enemy），但子类（`EnemyBase`）仍写 `MoveDirection = Mathf.Sign(dx)` 参与面朝判断（**用户否决，红线**）。
> - **v6（当前）**：**面朝方向的判断与维护 100% 收敛到 `CharaBase`，Chara 的任何子类只参与「速度及速度方向的修改」**——子类不再写任何面朝方向字段、不再调用 `TurnBack`/`FaceByMoveDirection`、不再出现 `Mathf.Sign(dx)` 赋给面朝字段的代码。

##### 3.3.3.1 核心原则（红线）

**原则 1：面朝方向 ≠ 速度方向。** 面朝方向由「意图性移动方向」驱动（玩家输入、走向目标、跟随目标）；速度可能是外力（受击击退、碰撞弹开）造成，**不承载转向意图**。因此不能从 `velocity.x` 自足推导。

**原则 2（本轮红线）：子类只参与「速度及速度方向」的修改。** 「速度」与「面朝」是两个正交概念，拆分为两套独立入口：
- **速度**（速度大小 + 速度方向）：子类的职责。子类算出速度矢量（如 `moveRight ? 1 : -1`、`Mathf.Sign(dx)`、`mPatrolSpeed`）并施加。
- **面朝方向**（`Scale.X`）：Chara 的专属职责。CharaBase 提供 `IntentMove` / `SetVelocity` / `FaceToTarget` / `FaceToward` / `FlipFacing` 等**统一能力入口**，所有读 `Scale.X`、判断方向是否翻转、写 `Scale.X` 的逻辑都封装在这些方法内部。子类调这些入口时只传「速度方向 + 速度」或「要面向的目标点 / 方向」——**子类内部不出现面朝判断**。

> **判定标准（用户红线，实施时逐行核对）**：任何 Chara 子类（`PlayerBase` / `AIPlayer` / `EnemyBase`，及其未来子类）的代码中：
> - ✗ 不得出现 `MoveDirection = ...`、`TurnBack(...)`、`FaceByMoveDirection(...)`、`transform.localScale.x`（读取或写入）、`Mathf.Sign(dx)` 后赋值给面朝用途字段；
> - ✓ 只允许出现「速度方向 / 速度」的赋值与调用（如 `moveRight = ...`、`dir = Mathf.Sign(dx)` 用于速度、`IntentMove(dir, speed)`、`SetVelocity(dir, speed)`）以及「面向目标」的调用（`FaceToTarget(pos)`、`FaceToward(dir)`、`FlipFacing()`）。

##### 3.3.3.2 CharaBase 新增的统一能力入口

面朝概念（`IsRight` / `TurnBack` / `FaceByMoveDirection`）已全在 `CharaBase`。v6 在此基础上**新增一组「速度 / 面朝」正交入口**，作为子类与 Chara 之间唯一的契约：

```csharp
// ============ 速度入口（子类可调用，只参与速度及速度方向） ============

/// <summary>
/// 按「意图移动方向」移动：施加速度 + 同步面朝。
/// 子类只传「速度方向 + 速度大小」；面朝判断由 Chara 内部完成。
/// 用于 Move / Chase / Searching / Investigate 等「移动方向 == 面朝方向」的状态。
/// </summary>
protected void IntentMove(float direction, float speed)
{
    if (direction != 0f)
        FaceByMoveDirection(direction > 0f);
    if (mRigidbody2D != null)
        mRigidbody2D.velocity = new Vector2(direction * speed, mRigidbody2D.velocity.y);
}

/// <summary>
/// 只施加速度、不改变面朝。用于「速度方向 ≠ 面朝方向」的场景（如 Follow 后退时面向目标）。
/// </summary>
protected void SetVelocity(float direction, float speed)
{
    if (mRigidbody2D != null)
        mRigidbody2D.velocity = new Vector2(direction * speed, mRigidbody2D.velocity.y);
}

// ============ 面朝入口（子类可调用，但只传「目标」，判断由 Chara 完成） ============

/// <summary>面向某目标点（不移动）。子类传目标世界坐标，方向判断由 Chara 完成。</summary>
protected void FaceToTarget(Vector3 targetPos)
{
    float dx = targetPos.x - transform.position.x;
    FaceByMoveDirection(dx >= 0f);
}

/// <summary>面向某方向（不移动）。dir 为 -1 / +1。</summary>
protected void FaceToward(float direction)
{
    if (direction != 0f)
        FaceByMoveDirection(direction > 0f);
}

/// <summary>原地翻转一次朝向（Inspect 张望用）。</summary>
protected void FlipFacing()
{
    FaceByMoveDirection(transform.localScale.x <= 0f);
}
```

其中 `FaceByMoveDirection(bool)` 沿用 v2 已实现（内部是 `TurnBack` 的 bool 封装，写法 1 条件翻转，幂等）。

> **为什么 FaceToTarget / FlipFacing 也能被判定为「符合红线」**：子类调用它们时**不参与任何判断**——`FaceToTarget(pos)` 里 `dx >= 0` 的方向判断在 Chara 内部；`FlipFacing()` 的翻转向量判断也在 Chara 内部。子类只是表达了「我要面向这个目标 / 我要转身」的**意图**，而非面朝判断。

##### 3.3.3.3 子类改造后代码形态（对照）

**PlayerBase**（`HumanPlayer` / `AIPlayer` 共用）——Move 状态只剩速度，翻转交给 Chara：

```csharp
// PlayerBase.OnMoveEnter —— 移除翻转（每帧 IntentMove 已处理，OnMoveEnter 不再需要）
public override void OnMoveEnter() { }

// PlayerBase.OnMoveFixedUpdate —— 只参与速度：传速度方向 + 速度，Chara 统一翻转
public override void OnMoveFixedUpdate()
{
    IntentMove(moveRight ? 1f : -1f, moveSpeed);
}
```

**EnemyBase 巡逻 Move**——只剩速度 + 到达判定，不再有 `MoveDirection` / `TurnBack`：

```csharp
public override void OnMoveEnter()
{
    if (mTargetPoint != null)
        FaceToTarget(mTargetPoint.position);   // 只表达意图，方向判断在 Chara
}
public override void OnMoveFixedUpdate()
{
    if (mTargetPoint == null) { ChangeState("Idle"); return; }
    float dx = mTargetPoint.position.x - transform.position.x;
    if (Mathf.Abs(dx) < kArriveEpsilonX) { /* 到达逻辑不变 */ return; }
    IntentMove(Mathf.Sign(dx), mPatrolSpeed);  // Sign(dx) 是速度方向，Chara 统一翻转
}
```

**EnemyBase Chase / Searching / Investigate**——同理只剩速度：

```csharp
// OnChaseFixedUpdate
IntentMove(Mathf.Sign(dx), mChaseSpeed);
// OnSearchingFixedUpdate
IntentMove(Mathf.Sign(dx), mChaseSpeed);
// OnInvestigateFixedUpdate
IntentMove(Mathf.Sign(dx), mPatrolSpeed);
// OnSearchingEnter / OnAlertedEnter / OnInvestigateEnter —— 面向目标点
FaceToTarget(mLostSightPos);
FaceToTarget(mAnomalySource);
FaceToTarget(mAnomalySource);
// OnInspectUpdate 张望翻转
FlipFacing();
// 站岗朝向偏好（ApplyPatrolPointFacing）
FaceToward(-1f);  // PatrolFacing.Left
FaceToward(1f);   // PatrolFacing.Right
FaceToTarget(nextPt.position);  // PatrolFacing.AutoByNextMove
// SetNextPatrolTarget / OnIdleUpdate 面向目标点
FaceToTarget(mTargetPoint.position);
```

**Follow（CharaBase.OnFollowFixedUpdate 与 AIPlayer.OnFollowFixedUpdate）**——用正交入口保持「后退移动但面向目标」语义：

```csharp
// CharaBase.OnFollowFixedUpdate（已在此层，改用正交入口）
if (distance > FollowMaxDistance)      // 前进：速度方向 == 面朝方向
    IntentMove(Mathf.Sign(delta), moveSpeed);
else if (distance < FollowMinDistance) // 后退：速度方向 ≠ 面朝方向
{
    SetVelocity(-Mathf.Sign(delta), moveSpeed);
    FaceToTarget(TargetFollowing.transform.position);
}
else                                   // 保持距离：只面向目标，不移动
{
    SetVelocity(0f, 0f);
    FaceToTarget(TargetFollowing.transform.position);
}
```

##### 3.3.3.4 为什么这样划分（对应红线）

1. **速度与面朝拆分成正交入口**：`IntentMove`（速度+面朝同向）、`SetVelocity`（只速度）、`FaceToTarget`/`FaceToward`/`FlipFacing`（只面朝）。子类按需组合，但**面朝判断永远发生在 CharaBase 内部**。
2. **「统一」= 面朝判断收敛到 CharaBase 一处**：所有 flip 逻辑（读 `Scale.X`、判断、写 `Scale.X`）只在 `TurnBack` / `FaceByMoveDirection` 出现，这两者又被封装进 `IntentMove` / `SetVelocity` / `FaceToTarget` / `FaceToward` / `FlipFacing`。子类不再出现任何面朝判断。
3. **覆盖全部 Chara 子类**：`PlayerBase`（HumanPlayer / AIPlayer）、`EnemyBase` 的所有移动/面向调用点都改走 Chara 入口；`Merchant` 无移动/翻转逻辑，不受影响。
4. **`Mathf.Sign(dx)` 的合法用途**：作为**速度方向**传给 `IntentMove`（这是「速度方向修改」，子类职责）；它不再被赋值给任何面朝用途字段（那是 v5 的红线违规）。

##### 3.3.3.5 影响面与风险

| 项 | 说明 |
|----|------|
| 改动文件 | `CharaBase.cs`（新增 `IntentMove`/`SetVelocity`/`FaceToTarget`/`FaceToward`/`FlipFacing` 五个能力入口；`OnFollowFixedUpdate` 改用正交入口）· `PlayerBase.cs`（`OnMoveEnter` 移除翻转、`OnMoveFixedUpdate` 改 `IntentMove`）· `AIPlayer.cs`（`OnFollowFixedUpdate` 改正交入口）· `EnemyBase.cs`（16 处 `TurnBack` 调用全部改为 Chara 能力入口） |
| 高频换向 bug 修复 | 由子类每帧 `OnMoveFixedUpdate` 调 `IntentMove` 保证（不受同状态去重影响） |
| 行为一致性 | 速度矢量与现状完全一致；`IsRight` 变化时序不变（每帧幂等），下游动画/相机/反馈不受影响 |
| 受击击退 | 外力不经过 `IntentMove`/`FaceToTarget` 等入口，不触发翻转 ✓ |
| 回归风险 | 中（EnemyBase 调用点改动面大，但均为等价替换；速度矢量不变，仅翻转入口收敛到 Chara） |
| 红线合规 | 实施后逐行核对 §3.3.3.1 判定标准，子类无任何面朝判断代码 |

> **待确认问题汇总（v6 方案）**：
> 1. CharaBase 新增五个能力入口（`IntentMove` / `SetVelocity` / `FaceToTarget` / `FaceToward` / `FlipFacing`）——是否采纳？
> 2. 子类（PlayerBase / AIPlayer / EnemyBase）只参与「速度及速度方向」，面朝判断 100% 收敛到 Chara——是否采纳？
> 3. Follow 用正交入口保持「后退移动但面向目标」语义——是否采纳？
> 4. `FaceToTarget` / `FlipFacing` 作为「面向目标 / 转身」的意图入口，方向判断在 Chara 内部——是否采纳？

> ⚠️ **v6 已废弃**（见 §3.3.3 标题说明），v8 见下。

#### 3.3.4 架构演进（当前方案 v8）：转向完全在 Chara 的 FSM hook 内从 velocity.x 推导，零新增

> **状态**：已实现（2026-08-16）
> **演进过程**：
> - v5：子类写 `MoveDirection = Mathf.Sign(dx)`（用户否决：子类参与面朝判断）。
> - v6：Chara 提供转向入口，子类调用 `FaceToTarget` 等（用户否决：子类调用转向入口 = 仍是子类参与转向；且仍提 `MoveDirection` 概念）。
> - v7：Chara 读 `velocity.x` 转向，但引入 `SyncFacingFromVelocity` / `IIntentionalMoveState` 接口 / 覆写 `FixedUpdate` 等**新方法/接口**（用户否决：**禁止新增任何方法和属性，只允许对 FSM hook 进行更改**）。
> - **v8（当前）**：**零新增——不新增任何方法、属性、接口、字段；只允许修改现有 FSM hook。** 禁止 `MoveDirection`；子类只允许对 `velocity`（速度）赋值；转向判断完全在 `CharaBase` 的 FSM hook 内从「当前速度方向 `velocity.x`」推导。

##### 3.3.4.1 核心原则（红线，绝对禁止项）

**红线 A：禁止 `MoveDirection` 变量。** 任何形式的 `MoveDirection` 字段/属性一律不得新增、不得使用。

**红线 B：子类只允许对「速度 / 位移」赋值。** `PlayerBase` / `AIPlayer` 中，对 `velocity`（速度）的赋值是允许的；对 `transform.localScale.x`（面朝）、任何转向方法的调用、任何方向字段的赋值，**一律禁止**。

> **豁免（用户确认，2026-08-16）**：**Enemy 注册的状态（Chase / Searching / Investigate / Alerted / Inspect / 站岗）不适用红线 B**——这些状态的 Hook 属于 `EnemyBase`（Enemy 自注册、自维护），转向可以在 Enemy 的这些状态 Hook 内进行（维持现状，仅随 v8 删除冗余/等价改写）。红线 B 只约束**由 `CharaBase` 通用注册的状态**（Move / Follow）——其 Hook 是 Chara 的，转向必须收敛到 Chara。因此 v8 的实际改动面 = **只收敛 Move / Follow 两个 Chara 通用状态**，Enemy 专属状态转向保持现状。

**红线 C：禁止新增任何方法和属性。** 本次改动**不新增**任何方法、属性、接口、字段、常量。所有逻辑只能写在**现有 FSM hook**（`OnXxxEnter` / `OnXxxUpdate` / `OnXxxFixedUpdate` / `OnXxxExit`）的方法体内，或修改现有 hook 的实现（覆写/重写已存在的虚 hook）。不新增 `MonoBehaviour.FixedUpdate` 覆写、不新增接口标记、不新增私有辅助方法。

> **判定标准（实施时逐行核对）**：
> - ✗ 不得出现任何新增的 `private/protected/public` 方法、属性、接口、字段、`const`；不得出现 `MoveDirection`、`SyncFacingFromVelocity`、`IIntentionalMoveState` 等新标识符。
> - ✗ **Chara 通用状态（Move / Follow）的 Hook**（属 `CharaBase`）不得由子类做转向；`PlayerBase` / `AIPlayer` 中不得出现 `TurnBack(...)`、`FaceByMoveDirection(...)`、`FaceToTarget(...)`、`FaceToward(...)`、`FlipFacing()`、`IntentMove(...)`、`transform.localScale.x`（读写）、`Mathf.Sign(...)` 赋给转向用途。
> - ✓ `PlayerBase` / `AIPlayer` 只允许对 `mRigidbody2D.velocity` 赋值（`dir` 仅用于速度方向），并通过调 `base.OnMoveFixedUpdate()` / `base.OnFollowFixedUpdate()` 让 Chara 完成转向。
> - ✓ **Enemy 专属状态的 Hook（Chase / Searching / Investigate / Alerted / Inspect / 站岗）不受红线 B 约束**：这些 Hook 在 `EnemyBase` 内自注册、自维护，其转向代码保持现状（用户确认豁免，见红线 B）。
> - ✓ CharaBase 允许在已有 hook 内使用已有成员（`TurnBack` / `velocity` / `mRigidbody2D`）完成转向。

##### 3.3.4.2 为什么「从 velocity.x 推导」这次不被受击击退反例否决

v3 被否决是因为它让 **`CharaBase.FixedUpdate` 无条件、对所有状态** 读 `velocity.x`——受击击退发生在非移动状态（Idle 等），此时有外力速度但不应转向。而 v8 **只在 `OnMoveFixedUpdate`（Move 状态专属 hook）内读 `velocity.x`**，且 Move 状态下 `velocity.x` 每帧由意图移动代码覆盖（已核实代码）：

| 状态 | `velocity.x` 来源 | 读 `velocity.x` 是否安全 |
|------|------------------|--------------------------|
| Move（Player/Enemy 巡逻） | 每帧 `moveRight`/`Sign(dx)` 覆盖 | ✅ 安全 |
| Idle / Alerted / Inspect / Stunned / Hidden / Dead | 不施加速度（velocity 归零或外力） | ❌ 不读（不在 OnMoveFixedUpdate 内） |

受击击退时角色处于**非 Move 状态**（站定、受击、Idle），`OnMoveFixedUpdate` 不执行，不读 `velocity.x`，因此不受影响。**v8 只收敛 Move 状态（bug 所在），其它状态的转向边界见 §3.3.4.3。**

##### 3.3.4.3 设计（零新增，只改 FSM hook）

**① `CharaBase.OnMoveFixedUpdate`（Move 状态 hook，Chara 侧覆写）——转向唯一落点：**

```csharp
// CharaBase —— 覆写已有 FSM hook：读当前速度方向决定是否转向
public override void OnMoveFixedUpdate()
{
    if (mRigidbody2D != null && Mathf.Abs(mRigidbody2D.velocity.x) > 0.01f)
        TurnBack(mRigidbody2D.velocity.x);
}
```

`TurnBack`（已有方法）保持「方向不一致才翻转」的幂等语义。**不新增任何东西。**

**② 子类 Move hook 只写速度，并让 Chara 的转向生效：**

- `PlayerBase.OnMoveFixedUpdate`（已有 hook，修改实现）：写速度后调 `base.OnMoveFixedUpdate()`，让 Chara 按**刚写入的** `velocity.x` 转向：

```csharp
public override void OnMoveFixedUpdate()
{
    float dir = moveRight ? 1f : -1f;
    mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
    base.OnMoveFixedUpdate();   // Chara：按 velocity.x 转向（读的是本帧刚写入的速度）
}
```

- `EnemyBase.OnMoveFixedUpdate`（已有 hook，修改实现）：写速度 + 到达判定后调 `base.OnMoveFixedUpdate()`：

```csharp
public override void OnMoveFixedUpdate()
{
    if (mTargetPoint == null) { ChangeState("Idle"); return; }
    float dx = mTargetPoint.position.x - transform.position.x;
    if (Mathf.Abs(dx) < kArriveEpsilonX) { /* 到达逻辑不变 */ return; }
    float dir = Mathf.Sign(dx);
    mRigidbody2D.velocity = new Vector2(dir * mPatrolSpeed, mRigidbody2D.velocity.y);
    base.OnMoveFixedUpdate();   // Chara：按 velocity.x 转向
}
```

> **顺序说明**：子类先写 `velocity`，再调 `base.OnMoveFixedUpdate()`（Chara 转向读刚写入的 `velocity.x`）。Chara 的转向在子类 hook 之后执行，读到的是本帧意图速度，不会晚一帧。

**③ `OnMoveEnter` 处理**（已有 hook）：`PlayerBase.OnMoveEnter` / `EnemyBase.OnMoveEnter` 中的 `TurnBack` 移除（改空或仅保留速度相关初始化），进入 Move 首帧的朝向由首帧 `OnMoveFixedUpdate` 按 velocity 修正（`OnMoveEnter` 与 `OnMoveFixedUpdate` 同一物理帧内先后执行，无朝向错误窗口）。

**④ `AIPlayer.OnMoveFixedUpdate`**：`HumanPlayer` / `AIPlayer` 均未重写 `OnMoveFixedUpdate`，继承 `PlayerBase` 版本（含 `base.OnMoveFixedUpdate()` 转向），因此 Move 工具 / ActionSequence MoveAction 自动被覆盖，业务入口零改动。

##### 3.3.4.4 状态归属边界（已确认）

**Chara 通用状态（Move / Follow）的 Hook 属于 `CharaBase`**，是 v8 唯一需要收敛转向的两个状态：

- **Move 状态**：转向由 `CharaBase.OnMoveFixedUpdate` 按 `velocity.x` 收敛 ✓（§3.3.4.3 ①）
- **Follow 状态**：转向由 `CharaBase.OnFollowFixedUpdate` 面向目标收敛 ✓（见下）

**Enemy 注册的状态（Chase / Searching / Investigate / Alerted / Inspect / 站岗）**：**用户确认（2026-08-16）**——这些状态的 Hook 在 `EnemyBase` 内自注册、自维护，**转向可以在 Enemy 的这些状态 Hook 内进行**（维持现状，不收敛到 Chara）。这构成对红线 B 的明确豁免（见红线 B 豁免说明）。因此这些状态的转向代码**本期不改动**，v8 的改动面只覆盖 Move / Follow 两个 Chara 通用状态。

**Follow 状态（CharaBase 有 hook，可收敛）**：Follow 后退时「速度方向 ≠ 面朝方向」（`velocity.x` 向左但应面向目标右侧），**不能从 `velocity.x` 推导转向**。改造 `CharaBase.OnFollowFixedUpdate` 为「面向目标」：

```csharp
// CharaBase.OnFollowFixedUpdate —— 转向在 Chara 内，面向 TargetFollowing 而非 velocity
if (TargetFollowing != null)
    TurnBack(TargetFollowing.transform.position.x - transform.position.x);
// 速度仍按原三分支施加（前进/后退/保持）
```

`AIPlayer.OnFollowFixedUpdate`（覆写版）删除其中 3 处 `TurnBack`，只保留业务（ActionRuntime 完成/失败判定）与速度赋值；转向交给 `base.OnFollowFixedUpdate()`（即 Chara 的面向目标逻辑）。

> **结论**：v8 的「子类只写速度」红线严格限定于 **Chara 通用状态（Move / Follow）**；**Enemy 专属状态（Chase / Searching / Investigate / Alerted / Inspect / 站岗）转向保持现状，不受红线 B 约束**（用户已确认豁免）。

##### 3.3.4.5 影响面与风险

| 项 | 说明 |
|----|------|
| 改动文件 | `CharaBase.cs`（覆写 `OnMoveFixedUpdate` 转向 + 改 `OnFollowFixedUpdate` 面向目标，均为**修改现有 hook**）· `PlayerBase.cs`（`OnMoveEnter` 删转向、`OnMoveFixedUpdate` 写速度 + 调 base）· `AIPlayer.cs`（`OnFollowFixedUpdate` 删 3 处 `TurnBack`，调 base）· `EnemyBase.cs`（`OnMoveEnter` 删转向、`OnMoveFixedUpdate` 写速度 + 调 base；Chase 等 6 状态转向**保留在 Enemy hook，不改动**） |
| 高频换向 bug 修复 | `CharaBase.OnMoveFixedUpdate` 每帧读 `velocity.x` 转向（不受同状态去重影响） |
| 零新增 | 不新增任何方法/属性/接口/字段；只改现有 FSM hook 实现 |
| 子类红线 | 子类无 `MoveDirection` / 转向方法调用 / `localScale.x` 读写；只对 `velocity` 赋值 |
| 受击击退 | 非 Move 状态不读 `velocity.x`，不受影响 ✓ |
| Follow 后退反例 | Follow 不读 `velocity.x`，改为 Chara 内面向 `TargetFollowing` ✓ |
| 回归风险 | 低-中（Move 收敛到 Chara；Enemy 专属 6 状态转向保留在 Enemy hook，不改动） |

> **待确认问题汇总（v8 方案）**：
> 1. `CharaBase.OnMoveFixedUpdate` 读 `velocity.x` 转向（Move 状态），子类 hook 写速度 + 调 `base.OnMoveFixedUpdate()`——是否采纳？
> 2. `PlayerBase.OnMoveEnter` / `EnemyBase.OnMoveEnter` 删除 `TurnBack`，首帧朝向由 `OnMoveFixedUpdate` 修正——是否采纳？
> 3. Follow 转向改为 Chara 内面向 `TargetFollowing`（不从 velocity 推导），`AIPlayer.OnFollowFixedUpdate` 删 3 处 `TurnBack` 调 base——是否采纳？
> 4. ~~Enemy 专属 6 状态（Chase / Searching / Investigate / Alerted / Inspect / 站岗）~~ **已确认（2026-08-16）**：Enemy 注册状态的 Hook 属于 `EnemyBase`，转向在 Enemy 的这些状态 Hook 内进行（维持现状），不收敛到 Chara。

### 3.4 关于「速度与朝向不同帧」的说明

`OnMoveFixedUpdate` 每物理帧用移动意图方向（`moveRight` / `Sign(dx)`）设速度；若反转只放在 `OnMoveEnter`，则 `OnMoveEnter` 被同状态去重跳过时，速度已按新方向施加而朝向未翻转，产生不一致窗口。**v8 让子类在 `OnMoveFixedUpdate` 先写 `velocity`、再调 `base.OnMoveFixedUpdate()`（Chara 按刚写入的 `velocity.x` 转向）** 消除该窗口——同一物理帧内「施加速度 → 读速度方向转向」先后完成，朝向与速度方向始终一致。Follow 的转向在 `CharaBase.OnFollowFixedUpdate` 每帧处理，无此窗口。

## 4. 实现步骤

> 按 **v8 方案（§3.3.4，零新增 + 只改 FSM hook + 子类只写速度）** 实施：

1. `CharaBase`：覆写已有 `OnMoveFixedUpdate`，读 `velocity.x`（`Mathf.Abs > 0.01f` 才转向）调 `TurnBack`；`OnFollowFixedUpdate` 改为面向 `TargetFollowing`（不从 velocity 推导）。**仅修改现有 hook，不新增任何方法/属性。**
2. `PlayerBase`：`OnMoveEnter` 删除 `TurnBack`（改空）；`OnMoveFixedUpdate` 改为「写 `velocity` → 调 `base.OnMoveFixedUpdate()`」。
3. `EnemyBase`：`OnMoveEnter` 删除 `TurnBack`；`OnMoveFixedUpdate` 改为「写 `velocity` + 到达判定 → 调 `base.OnMoveFixedUpdate()`」。**Chase / Searching / Investigate / Alerted / Inspect / 站岗 6 状态转向保留在 Enemy hook，不改动**（用户已确认豁免，§3.3.4.4）。
4. `AIPlayer`：`OnFollowFixedUpdate` 删除 3 处 `TurnBack`，只保留业务与速度赋值，转向交给 `base.OnFollowFixedUpdate()`。
5. **红线自检**：逐行核对 §3.3.4.1 判定标准——不新增任何方法/属性/接口/字段；子类无 `MoveDirection` / 转向方法调用 / `localScale.x` 读写；只对 `velocity` 赋值。
6. 运行回归验证（§6）。

> **架构说明**：v8 的"统一"= **转向判断收敛到 `CharaBase` 的 Move / Follow hook 内**，由当前速度方向（Move）或跟随目标（Follow）驱动。子类只负责写速度（`mRigidbody2D.velocity`）与业务逻辑，通过调 `base.OnMoveFixedUpdate()` / `base.OnFollowFixedUpdate()` 让 Chara 完成转向。受击击退等外力速度（非 Move 状态）不被读取，不影响朝向。**零新增**：不引入任何新方法、属性、接口、字段。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 高频换向 bug 未根治（若翻转仅依赖 `OnMoveEnter`） | Move 转向在 `CharaBase.OnMoveFixedUpdate`（每帧执行、不受同状态去重影响），子类调 `base.OnMoveFixedUpdate()` 后按 `velocity.x` 翻转 |
| 在 Idle 等静止状态误翻转 | `OnMoveFixedUpdate` 只属于 Move 状态；Idle 等不执行，保持当前朝向；且 `Abs(velocity.x) > 0.01f` 才翻转 |
| 受击击退等外力速度导致错误掉头 | 外力速度发生在非 Move 状态（Idle 等），`OnMoveFixedUpdate` 不读这些状态的速度；Follow 后退反例由「面向目标」规避 |
| 历史残留无法自愈（写法 1 下） | 已确认当前 `Scale.X` 仅 `TurnBack` 一处写入，无其它污染源；如后续出现，可升级写法 2（§3.3.1） |
| 红线违规（子类仍出现面朝判断） | 实施后逐行核对 §3.3.4.1 判定标准；子类只对 `velocity` 赋值 |
| 零新增违规（新增方法/属性/接口） | 实施后逐行核对——不新增任何方法/属性/接口/字段/常量，只改现有 FSM hook 实现 |
| Enemy 6 状态（Chase 等）转向保留在 Enemy hook | 用户已确认豁免：Enemy 注册状态的 Hook 属 `EnemyBase`，转向在 Enemy 状态 Hook 内进行（维持现状），不收敛到 Chara ✓ |

## 6. 测试建议

### 6.1 回归清单

- [ ] 高频左右切换（手速极限连按 A/D）500 帧，逐帧校验 `Scale.X` 与速度方向一致
- [ ] 低频切换（中间停顿 ≥1 帧）行为不回归
- [ ] 静止（松开按键）时朝向不变
- [ ] `AIPlayer` Move 工具：左移/右移后朝向正确
- [ ] `AIPlayer` Follow：跟随目标左右移动朝向正确
- [ ] `EnemyBase` 巡逻 / 追击 / 调查：朝向不回归
- [ ] 受击击退（外力产生速度）时**不应转向**（v4 边界验证：证明「面朝方向≠速度方向」）

### 6.2 自愈强度的选择

已统一到 §3.3.1 的两种写法分析（条件翻转 vs 强制对齐）。回归清单中若采纳写法 1，补一条「人为制造 `Scale.X` 反向残留 → 换向后是否纠正」的确认；若采纳写法 2，补一条「移动中每帧强制写无异常」。最终写法以用户确认为准。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-15 | **初版（已废弃，架构修正）**：按写法 1 把 `FaceByMoveDirection` 调用分散接入 `HumanPlayer.GetInput`、`AIPlayer.Move`、`AIPlayer.ExecuteMoveAction` 三处业务入口。用户评审指出「应统一到 Chara 层面，分散调用是破坏架构的设计」，故废弃。 |
| 2026-08-15 | **v2（已废弃，仍绑状态）**：`CharaBase` 新增 `FaceByMoveDirection(bool)`（`TurnBack` 的 bool 薄封装，写法 1 条件翻转）；在 `PlayerBase.OnMoveFixedUpdate` 集中调用。`HumanPlayer` / `AIPlayer` 未重写 `OnMoveFixedUpdate` → 自动被覆盖，业务入口零改动。用户指出「仍放在各状态 Hook 里 + Player/Enemy 各写一套」，未真正上浮到 Chara，故废弃。 |
| 2026-08-15 | **v3（已废弃，受击击退反例）**：从 `velocity.x` 自足推导、彻底统一到 `CharaBase.FixedUpdate`。用户以「受击击退反例」否决（有速度但不应转向，面朝方向≠速度方向）。 |
| 2026-08-15 | **v4（已废弃，调用点未收敛）**：入口统一到 Chara（`FaceByMoveDirection`），但调用点仍分散在各状态 hook。用户指出「调用点没按状态归属层收敛，Move 的反转仍写在 PlayerBase」。详见 §3.3.2。 |
| 2026-08-15 | **v5（已废弃，红线违规）**：反转逻辑按「状态归属层」收敛（Move/Follow→Chara，Enemy 专属→Enemy）。但 `EnemyBase.OnMoveFixedUpdate` 仍写 `MoveDirection = Mathf.Sign(dx)`——**子类直接参与面朝方向判断，被用户红线否决**。 |
| 2026-08-15 | **v6（已废弃，子类仍参与转向）**：CharaBase 新增 `IntentMove`/`SetVelocity`/`FaceToTarget`/`FaceToward`/`FlipFacing` 五个能力入口，子类调用这些入口表达转向意图。用户判定这仍是「子类参与转向」（子类决定转向时机/方向），且仍提 `MoveDirection` 概念，故废弃。详见 §3.3.3。 |
| 2026-08-15 | **v7（已废弃，新增方法/接口）**：CharaBase 读 `velocity.x` 转向，但引入 `SyncFacingFromVelocity` / `IIntentionalMoveState` 接口 / 覆写 `FixedUpdate` 等**新方法/接口**。用户否决：**禁止新增任何方法和属性，只允许对 FSM hook 进行更改**。 |
| 2026-08-15 | **v8（当前方案，未改代码）**：**零新增**（不新增任何方法/属性/接口/字段/常量，只改现有 FSM hook）；禁止 `MoveDirection`；子类只允许对 `velocity` 赋值；`CharaBase.OnMoveFixedUpdate` 读 `velocity.x` 转向（Move 状态）；Follow 改为面向目标。待用户确认后实施。详见 §3.3.4。 |
| 2026-08-16 | **v8 补充确认**：**Enemy 注册状态（Chase / Searching / Investigate / Alerted / Inspect / 站岗）转向可在 Enemy 的状态 Hook 内进行**（状态 Hook 属 `EnemyBase`，维持现状，构成对红线 B 的豁免）。v8 改动面限定为 Move / Follow 两个 Chara 通用状态。 |
| 2026-08-16 | **v8 已实现**：`CharaBase` 覆写 `OnMoveFixedUpdate`（读 `velocity.x` 转向）+ `OnFollowFixedUpdate` 改为面向目标 + 删除废弃 `FaceByMoveDirection`；`PlayerBase` 的 `OnMoveEnter` 删转向、`OnMoveFixedUpdate` 只写速度 + 调 base；`AIPlayer` 的 `OnFollowFixedUpdate` 删 3 处 `TurnBack` 只保留业务 + 调 base；`EnemyBase` 的 `OnMoveEnter` 删转向、`OnMoveFixedUpdate` 只写速度 + 调 base（Chase 等 6 状态未改）。零新增、子类只写速度，红线自检通过。 |
| 2026-08-16 | **v8 已验收**：用户联调验收通过。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
