# PRD — v0.22.1 EnemyBase 异常事件吸引与巡逻点朝向配置

> **状态**：已实现
> **对应需求**：`requirements/需求文档.md`
> **最后更新**：2026-07-10

---

## 1. 背景与目标

### 1.1 现状

v0.21.7 已完成 EnemyBase 基础能力：**巡逻 (Idle↔Move) / 追击 (Chase) / 攻击 / 背刺 Stunned**。当前敌人行为的问题：

1. **感知面窄**：只有 `mVisionZone`（直视视野）能触发追击；玩家扔碎玻璃、开门等场景无法把敌人吸引过去。
2. **追丢瞬间"传送式回归"**：`OnVisionExit` 直接切 Idle 并把目标设为最远巡逻点，敌人 180° 反向就走，视觉上很跳。
3. **巡逻点朝向不可配置**：`SetNextPatrolTarget` 只按"当前位置 → 目标位置"确定去程方向；到达后 Idle 时的朝向就是"到达朝向"，无法在同一个巡逻点上"面朝墙"等语义化姿态。

### 1.2 目标

- 引入"**异常事件**"通用感知语义（可复用扩展），本期落一个具体装置 `BrokenGlass`（可发声的碎玻璃）作为触发源。
- 让 EnemyBase 具备"**发现 → 巡视 → 检查**"三段式的对异常事件的响应流程，同时把 Chase 追丢逻辑改造成"先走向消失点再检查"的连续动作，替换现有"追丢瞬间切 Idle 走最远巡逻点"的跳变。
- 让每个巡逻点可配置**逗留时的朝向**（保持当前 / 强制左 / 强制右 / 按下一段路径自动推断）。

---

## 2. 范围

### 2.1 本期包含

- **需求一**：
  - 新增 `BrokenGlass` 装置（DeviceBase，非交互、非可点击、非可 Trigger）。挂在地上，任何 `SceneObj` 进入其触发范围时"发声"一次。
  - `BrokenGlass` 支持在 Unity Editor 中通过 Gizmos 可视化其"吸引半径"（`mAttractRadius`，可配置）。
  - EnemyBase 收到异常事件后依次进入 `Alerted → Investigate → Inspect` 三个新状态，随后离开异常点走向**最远巡逻点**（复用现有 `SetTargetToFarthestPatrolPoint`）。
  - Chase 追丢改造：`OnVisionExit` 不再直接切 Idle，而是记录**玩家消失位置**并进入新的 `Searching` 状态，走到该位置后转入 `Inspect`（复用异常检查的 Idle+左右张望逻辑）。
- **需求二**：
  - 新增 `PatrolPointConfig` 组件（挂在 `mPatrolPoints` 引用的 Transform 上），暴露 `Facing` 字段（枚举：`Left / Right / KeepCurrent / AutoByNextMove`），用于配置 EnemyBase 逗留该巡逻点时的朝向。
  - EnemyBase 在**到达巡逻点进入 Idle 的瞬间**读取 `PatrolPointConfig.Facing` 并应用一次朝向；仅一次，不锁定后续帧。

### 2.2 本期不包含

- 不改动记忆系统、Python 通信、协议、Agent 工具。
- `BrokenGlass` 本期只有"进入即触发"这一种触发方式；不做爆炸、玩家踩踏音效、粒子等表现。
- 不新增全局事件总线；异常事件感知采用装置侧主动查询（详见 §4.1）。
- 不改造 `SceneObjInfoMapper / SceneObjInfoRenderer`（本期不把 `BrokenGlass` 的吸引范围向 AI Player 渲染）。
- 不改动 Stunned / Hidden / Dead 语义。Stunned 敌人不响应异常事件（复用 `IImmovableState` 判定）。
- 不新增 Python 侧的 `_cmd` 工具，也不新增 Unity → Python 协议消息。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家 | 把碎玻璃放在路径上，敌人从远处巡逻 | 玩家/其他 SceneObj 进入碎玻璃触发范围时，范围内的敌人被吸引，转头朝向异常源，短暂停留后走向异常点 |
| 玩家 | 敌人到达异常点 | 敌人在异常点 Idle，隔一段时间左右张望；一段时间后走向最远巡逻点，恢复巡逻 |
| 玩家 | 被敌人 Chase 追击中脱离视野 | 敌人不立即掉头，而是先走向玩家最后消失位置；到达后原地检查（Inspect），最后走向最远巡逻点 |
| 关卡设计 | 需要敌人在某巡逻点面朝墙壁 | 在该巡逻点 Transform 上加 `PatrolPointConfig` 组件，选 `Left` / `Right`，敌人到达该点 Idle 时朝向立即被应用一次 |
| 关卡设计 | 需要在编辑器里直观看到碎玻璃吸引范围 | Scene 视图中选中 `BrokenGlass` 后能看到一个圆形 Gizmos，编辑半径即时可见 |
| 关卡设计 | 不希望某个碎玻璃被多次拾取 | `BrokenGlass` 触发后进入冷却，冷却期内不再触发；冷却时间在 Inspector 可配置 |

---

## 4. 功能需求

### 4.1 `BrokenGlass` 装置 与 异常事件感知机制

#### FR-1.1 装置基础

- 新增 `BrokenGlass : DeviceBase`（namespace `IndependentAgentProject`）。
- 默认不可交互（`IsInteractable => false`）、不可点击（`IsClickable => false`）。
- 需要挂载 `CircleCollider2D`（IsTrigger=true），半径即"吸引/触发半径"。

#### FR-1.2 触发行为（可重复 + 冷却）

- 任意 `SceneObj`（含 `PlayerBase` 与 `EnemyBase` 本身）进入 Trigger 时，本次进入**视为一次异常事件**。
- 触发后进入 `mCooldownSeconds`（Inspector 默认 3s）的冷却，冷却期间**忽略新的 OnTriggerEnter2D**。
- **不销毁 GameObject**，冷却结束后可再次触发（对应用户选择的 `reusable_cooldown` 方案）。
- 冷却由 `mCooldownEndTime` 时间戳判定，纯数据，不占用 FSM 状态。

#### FR-1.3 感知机制方案（本期采纳方案 C）

本期采纳 **方案 C：全局事件总线**。项目已有 QFramework 风格的 `this.SendEvent<T>` / `this.RegisterEvent<T>` 基础设施（参考 `GameOverEvent`），复用即可，不新增框架代码。

##### 方案 C：全局事件总线（**采用**）

- 新增 `EnemyAnomalyEvent`：
  ```csharp
  public class EnemyAnomalyEvent
  {
      public Vector2 SourcePos;       // 异常源物理位置（碎玻璃的 transform.position）
      public float Radius;            // 异常传播半径（BrokenGlass.mAttractRadius）
      public SceneObjBase Triggerer;  // 谁踩/触发（触发 Collider 的场景对象）
      public SceneObjBase SourceDevice; // 声源装置本身（BrokenGlass 自身的引用）
  }
  ```
  命名前缀 `Enemy` 明确"这是给敌人 AI 感知用的异常源事件"，避免与"系统异常 / 错误"混淆。`Triggerer` 用于接收方分流（自触发 / 异敌仅警觉 / 完整调查）；`SourceDevice` 用于按声源实例做"每个 EnemyBase 独立冷却"。
  - `Triggerer == 自己` → 忽略（不能被自己踩碎玻璃的声响吸引）。
  - `Triggerer is EnemyBase`（其他敌人）→ 只做"警觉一下"（Alerted 后回之前的状态或继续原调查），不投入完整调查。
  - 其他（PlayerBase / 装置自身 / null）→ 走完整 Alerted → Investigate → Inspect。
- `BrokenGlass` 触发时 `this.SendEvent(new EnemyAnomalyEvent { SourcePos = transform.position, Radius = mAttractRadius, Triggerer = sceneObj, SourceDevice = this });`，**不查询**场内哪些敌人在范围内。
- 每个 `EnemyBase` 在 `Awake` 里 `this.RegisterEvent<EnemyAnomalyEvent>(OnEnemyAnomalyEventFired).UnRegisterWhenGameObjectDestroyed(this);`（QFramework 语义）。
- 接收方（EnemyBase）在 `OnEnemyAnomalyEventFired(EnemyAnomalyEvent evt)` 内先做距离过滤 + 源冷却过滤，再走 §4.2 的 `OnHearAnomaly` 分流。
- **优点**：装置与敌人完全解耦；发送方零物理查询；未来新增"脚步声 / 爆炸声 / 打斗声"等触发源复用同一事件与订阅链；`EnemyBase` 不需要新增子 Collider。
- **性能说明**：广播的"全场景生效"仅是委托调用；接收方通常是个位数敌人，一次 `Vector2.Distance` 远比 `Physics2D.OverlapCircleAll` 便宜。

##### 关于"敌人不在范围内也要广播吗"的澄清

采纳事件总线后，**发送方就是无差别广播**：`BrokenGlass` 只需要说"我在 X 位置发出了半径 R 的声音"，不需要判断谁在范围内。距离过滤是**接收方**的责任（EnemyBase 收到 `EnemyAnomalyEvent` 后先算 `Vector2.Distance` 再决定是否响应）。

这是"事件订阅"和"主动查询"的核心区别：
- 主动查询（方案 A）：发送方枚举监听者 → 发送方需要知道监听者类型 / 层 / 位置。
- 事件订阅（方案 C）：发送方**不认识**监听者 → 距离/资格过滤下沉到接收方各自判断。

若担心广播成本：`EnemyAnomalyEvent` 每次触发只在事件总线上做一次 `foreach handler → invoke`，等价于一次委托遍历，比 `Physics2D.OverlapCircleAll` 便宜得多；且不会因为敌人数量增多而带来物理开销的非线性增长。

##### 方案 A / B（对比备选，本期不实现）

- **方案 A（装置侧主动查询）**：`Physics2D.OverlapCircleAll` 找敌人；发送方耦合 `mEnemyLayerMask` 与"知道监听者类型"这个职责；未来新增其他响应者需要改装置代码。
- **方案 B（EnemyBase 挂 AlertZone）**：吸引半径变成敌人属性（"多远听得见"应由声源决定，语义反了）；且 AlertZone 长期挂 Collider 做物理检测。

#### FR-1.4 Gizmos 可视化

- 在 `BrokenGlass.OnDrawGizmos`（编辑器和运行时都可见）与 `OnDrawGizmosSelected`（选中时高亮）里绘制：
  - 触发半径圆：来自 `CircleCollider2D.radius`（黄色实心圆边缘）。
  - 吸引半径圆：`mAttractRadius`（红色空心圆边缘）。
- 不引入 `#if UNITY_EDITOR` 特殊分支（Gizmos API 在 Editor 与 Player 都可编译，只是 Player 内不显示）。

#### FR-1.5 触发形状（BoxCollider2D）

- `BrokenGlass` 触发器使用 `BoxCollider2D`（IsTrigger=true），而非圆形。Box 的 size 决定"SceneObj 进入即触发"的判定范围。
- 触发形状（Box）与"声音吸引半径"（`mAttractRadius`，圆形）是两个独立概念：
  - Trigger Box 决定"什么位置/什么形状会触发这次异常事件"（发声条件）。
  - `mAttractRadius` 决定"这次事件的声音传得多远"（会波及多远的听者），是圆形的物理直觉。
- Gizmos 画法：Trigger 区域用 `Gizmos.DrawWireCube`（结合 BoxCollider2D 的 size / offset 与 `transform.lossyScale`）；`mAttractRadius` 用 `Gizmos.DrawWireSphere`。

### 4.2 EnemyBase 异常事件响应流程

#### FR-2.0 新增 FSM 标记接口 `IBattleState`

- 路径：`Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/FSM/IBattleState.cs`
- 语义：实现此接口的 `FSMStateBase` 表示"角色处于战斗状态，不应被异常事件干扰"。
- 命名与既有 `IUndetectableState / IImmovableState / IInvulnerableState` 对齐。
- SceneObjBase 补充：`public virtual bool IsInBattle => mCurState is IBattleState;`（保持与 `IsUndetectable / IsImmovable / IsInvulnerable` 同样的暴露方式）。
- 本期实现者：`EnemyBase.ChaseState`、`EnemyBase.SearchingState`。
- 未来若有其他"战斗中不受打扰"的状态（如"处决"），只需实现 `IBattleState` 即可复用同一过滤逻辑。

#### FR-2.1 新增三个状态

引入三个新 `FSMStateBase`，均**不实现** `IUndetectableState / IImmovableState / IInvulnerableState / IBattleState`：

| 状态名 | 语义 | 允许被 Vision 检测转 Chase | 允许被背刺 | 允许被新的异常源打断 |
|--------|------|--------------------------|-----------|--------------------|
| `Alerted` | 发现异常：原地转头朝向异常源，停留 `mAlertedSeconds`（默认 3s）。**"仅警觉"模式**下退出时不进 Investigate，而是回到 `mPreAlertState`（详见 FR-2.4）。 | 是（视野优先级更高） | 是 | **异源** → 打断当前 Alerted，用新 sourcePos 重进；**同源** → 完全忽略 |
| `Investigate` | 巡视异常：以 `mPatrolSpeed` 走向异常源，直到 X 轴距离 < `kArriveEpsilonX` | 是 | 是 | **异源** → 打断回 Alerted 走新流程（异敌事件保留原目标，见 FR-2.4）；**同源** → 完全忽略 |
| `Inspect` | 检查：在异常点原地 Idle `mInspectSeconds`（默认 5s），每 `mInspectTurnInterval`（默认 1.2s）翻一次面朝 | 是 | 是 | **异源** → 打断回 Alerted 走新流程；**同源** → 完全忽略 |

三个状态的通用规则：

- 进入 Alerted / Investigate / Inspect 时 `mChaseTarget = null`（避免与 Chase 冲突）。
- 期间视野 `OnVisionEnter` 到玩家仍能立刻升级为 Chase（Chase 优先级最高）；升级前给 `mCurrentSourceDevice` 写入冷却（视为调查链条被战斗中断）。
- 期间收到新的 `EnemyAnomalyEvent`：
  - **同源**（`sourceDevice == mCurrentSourceDevice`） → 完全忽略，不重置计时、不改变 `mAnomalySource`。
  - **异源** → 给旧 `mCurrentSourceDevice` 写冷却；用新的 `sourcePos` 覆盖 `mAnomalySource`（除"完整调查中被异敌事件打断"外，见 FR-2.4）并重新进入 Alerted。
- Stunned / Dead 时 `EnemyAnomalyEvent` 直接忽略（`IsImmovable || IsDead`）。

#### FR-2.2 `OnHearAnomaly(sourcePos, triggerer, sourceDevice)` 入口

统一采用 **FSM 接口过滤 + 当前源识别 + 源冷却过滤 + Triggerer 分流**（不用状态名字符串），过滤顺序：

1. `triggerer == this` → **忽略**（不能被自己踩碎玻璃的声响吸引；EnemyBase 也可能踩到碎玻璃触发事件）。
2. **当前源识别**（详见 FR-2.5）：若本敌人已在**当前调查链**（Alerted/Investigate/Inspect）中，且 `sourceDevice == mCurrentSourceDevice` → **忽略**（不被自己正在调查的同一来源反复打断，不重置计时，不重新面朝）。
3. **同源冷却过滤**：若 `sourceDevice` 在本 `EnemyBase.mSourceCooldowns` 中记录且未过期 → **忽略**。冷却从上一次针对该源的调查链条**结束/中断**后开始计算（详见 FR-2.5）。
4. `IsImmovable`（覆盖 Stunned）或 `IsDead` → **忽略**。
5. `IsInBattle`（覆盖 Chase / Searching）→ **忽略**（战斗中不受异常事件干扰）。
6. 其余（Idle / Move / Alerted / Investigate / Inspect 但源不同） → 记录 `mCurrentSourceDevice = sourceDevice`；`mAnomalySource = sourcePos`（按 FR-2.4 表格决定是否覆盖）；根据 `triggerer` 与当前状态决定分流（详见 FR-2.4 / FR-2.6）。
7. 若当前已经在 Alerted，`ChangeState` 需要"重进 Alerted"以重置计时；具体实现见 solution §3.5.4。

设计动因：EnemyBase 也可能踩到 `BrokenGlass` 的 Trigger，因此：
- **自触发忽略**：避免自己踩到就绕回头找自己，形成死循环。
- **当前源不打断**：敌人 A 正在走向 BrokenGlass X 的路上或者已经在 X 附近张望；如果 X 又被踩了一次（或其他敌人踩到 X），A 不应该重新计时、也不应改变行为，反正它就是在检查这块玻璃，不需要"再被同一块玻璃惊动一次"。
- **链条结束后冷却**：BrokenGlass 自身冷却（`mCooldownSeconds = 3f`）只解决"玩家原地来回穿越 Trigger"的抖动。但一个 EnemyBase 完成一次 X 的完整调查后（几秒到十几秒），马上又有人再踩 X，敌人立刻再次被吸引也不合理，需要冷却。**冷却计时应该从调查链条结束开始**，否则玩家完成了 Investigate → Inspect（十几秒）时，`mSameSourceCooldown = 5f` 早就过完了，起不到"敌人刚检查完不应立刻被同一块玻璃再吸引"的效果。
- **异敌只警觉不调查**：多个敌人巡逻时，A 踩碎玻璃，B 听到应该"抬头看一下"而不是"扔掉自己的巡逻走过去"，否则整个警备体系会因为一次踩玻璃全体聚集，语义与玩家预期不符。玩家踩碎玻璃才是"引诱敌人偏离岗位"的关卡设计元素，应保持完整调查。
- **异敌不打断完整调查目标**：EnemyBase 已经在 Investigate/Inspect（完整调查）中被异敌事件打断时，Alerted 结束后应**回到原调查目标**继续 Investigate → Inspect，而不是回 Idle/Move 或跑向新的异敌位置（避免"一半路上被另一个巡逻兵踩玻璃、结果放弃原调查回岗位"的反直觉）。

#### FR-2.3 状态迁移

正常路径（完整调查，`mAlertOnly = false`，通常由 Player 触发）：

```
Idle / Move  --OnHearAnomaly--> Alerted (面朝 sourcePos.x, mAlertOnly=false)
Alerted      --mAlertedSeconds 到--> Investigate
Investigate  --X 轴对齐到 sourcePos--> Inspect
Inspect      --mInspectSeconds 到--> 设最远巡逻点 --> Idle
```

仅警觉路径（`mAlertOnly = true`，由其他 EnemyBase 触发）：

```
Idle / Move --OnHearAnomaly(其他EnemyBase)--> Alerted (mAlertOnly=true, mPreAlertState=当前)
Alerted     --mAlertedSeconds 到 && mAlertOnly--> ChangeState(mPreAlertState)
```

被新异常打断（三种路径同构，均切回 Alerted 用新的 sourcePos）：

```
Alerted     --OnHearAnomaly (异源 sourcePos)--> Alerted (重置计时)
Investigate --OnHearAnomaly (异源 sourcePos)--> Alerted (重置计时)
Inspect     --OnHearAnomaly (异源 sourcePos)--> Alerted (重置计时)

Alerted / Investigate / Inspect --OnHearAnomaly (同源 sourceDevice)--> 完全忽略（不重进 Alerted）
```

**关于 `mAlertOnly` / `mAnomalySource` / `mCurrentSourceDevice` 在打断时的更新规则**（细化版，配合 FR-2.4）：
- 同源事件 → 完全忽略，不改变任何字段。
- 已在"完整调查"中被"仅警觉"型异源打断：`mAlertOnly` 保持 `false`（不降级）；**`mAnomalySource` 与 `mCurrentSourceDevice` 都不更新**（保留原调查目标，新异敌源不成为当前源、也不写它的冷却）；Alerted 结束后继续 Investigate 走向原目标。
- 已在"仅警觉"中被"完整调查"型异源打断：`mAlertOnly = false` 升级为完整调查；**给旧 `mCurrentSourceDevice` 写冷却**；**更新 `mAnomalySource` 与 `mCurrentSourceDevice` 为新源**；最终去最远巡逻点而不是回 `mPreAlertState`。
- 已在"完整调查"中被"完整调查"型异源打断（新玩家/装置声源）：`mAlertOnly` 保持 `false`；**给旧 `mCurrentSourceDevice` 写冷却**；**更新 `mAnomalySource` 与 `mCurrentSourceDevice` 为新源**（新目标替换旧目标）。
- 已在"仅警觉"中被"仅警觉"型异源打断（多个异敌）：`mAlertOnly` 保持 `true`；**给旧 `mCurrentSourceDevice` 写冷却**；`mAnomalySource` 与 `mCurrentSourceDevice` 更新为新源（仅用于 Alerted 期间面朝方向，Alerted 结束仍回 `mPreAlertState`，届时再给新源写冷却）。
- 相同类型互相打断（异源）：仅重置 Alerted 计时；`mPreAlertState` 保持不变。

#### FR-2.4 Alerted 结束后的三种去向

Alerted 结束时按当前的 `mAlertOnly` + 「进入 Alerted 前是否已在完整调查中」两个维度决定去向：

| 进入 Alerted 前的状态 | 本次异常 triggerer | 本次事件 sourceDevice vs mCurrentSourceDevice | `mAlertOnly` | Alerted 结束后 |
|-----------------------|-------------------|----------|--------------|----------------|
| `Idle / Move` | 玩家/装置/null | 新链条（`mCurrentSourceDevice = new`） | `false` | `ChangeState("Investigate")`（走新 `mAnomalySource`） |
| `Idle / Move` | 其他 EnemyBase | 新链条 | `true` | `ChangeState(mPreAlertState)`（回 Idle/Move） |
| `Investigate / Inspect`（同源） | 任意 | **同源** → 完全忽略新事件，不进入 Alerted | 保持 | 无变化，继续原调查 |
| `Investigate / Inspect`（原本已在完整调查，异源） | 玩家/装置/null | 新源替换旧源；旧源写入冷却 | `false` | `ChangeState("Investigate")`（走**新的** `mAnomalySource`） |
| `Investigate / Inspect`（原本已在完整调查，异源） | 其他 EnemyBase | 保留原 `mCurrentSourceDevice` 与 `mAnomalySource`；新源不写入冷却（未成为当前源） | `false` **不降级** | `ChangeState("Investigate")`（**继续原调查目标**，Alerted 只是"抬头一下"）|
| `Alerted / Investigate / Inspect`（仅警觉中，异源） | 玩家/装置/null | 新源替换旧源；旧源写入冷却 | 升级为 `false` | `ChangeState("Investigate")`（新目标） |
| `Alerted / Investigate / Inspect`（仅警觉中，异源） | 其他 EnemyBase | 新源替换旧源；旧源写入冷却 | 保持 `true` | Alerted 结束仍回 `mPreAlertState` |

关键规则简化版：
- **同源** → 完全忽略新事件（不重进 Alerted，不重置计时）。
- **异源 + 玩家/装置** → 替换 `mCurrentSourceDevice`，旧源立即写冷却。
- **异源 + 异敌 + 我已在完整调查中** → 保留 `mCurrentSourceDevice` 与 `mAnomalySource`（异敌只让我短暂警觉，不换我在调查的玻璃）。
- **异源 + 异敌 + 我在仅警觉中** → 换成新的异敌事件，旧异敌源写冷却。

设计动因：EnemyBase 已经在追一个"高优先级"（玩家/装置/null 触发）线索，中途听到另一个同伴踩玻璃的声音只应短暂警觉；如果放弃原调查目标回岗位，玩家精心引诱的调查目标会被同伴意外中断，反直觉。同源不打断则避免"敌人一路走一路被同一块玻璃反复重置计时"。

#### FR-2.5 当前调查源识别与同源冷却（防同一 BrokenGlass 反复吸引同一敌人）

**字段**：
- `SceneObjBase mCurrentSourceDevice`：当前正在调查/警觉的声源装置。进入 Alerted 时写入；调查链条结束（回到 Idle/Move）或被异敌打断继续原目标（保留原值）或被完整调查升级（替换为新源）时更新。
- `Dictionary<SceneObjBase, float> mSourceCooldowns`：本敌人对每个声源装置的冷却截止时间。

**当前源识别（不打断）**：
- 已在 `Alerted / Investigate / Inspect` 中且 `sourceDevice == mCurrentSourceDevice` → **完全忽略**新事件：
  - 不重置 Alerted 计时。
  - 不改变 `mAnomalySource`（保留当前调查目标）。
  - 不改变 `mAlertOnly`。
  - 不重新写入 `mSourceCooldowns`（冷却由"链条结束时"统一写入）。
- 语义："这块玻璃我已经在处理了，别再惊动我一次"。

**冷却写入时机（关键：链条结束时才写）**：
不在 `OnHearAnomaly` 进入 Alerted 时立即写冷却，而是在**该源的调查链条结束**时才写入 `mSourceCooldowns[source] = Time.time + mSameSourceCooldown`。链条结束的判定：
- 完整调查链：`Inspect` 状态结束时（`mStateTimer >= mInspectSeconds`）。
- 仅警觉链：`Alerted` 状态结束、切回 `mPreAlertState` 时。
- 被"完整调查"型新源升级替换：**旧源**的链条视为"中断结束"，此时给旧源写入冷却；同时把 `mCurrentSourceDevice` 换为新源，新源不写冷却（正常进入新链条）。
- 被 Chase 抢占（`OnVisionEnter` 升级为 Chase）：当前源链条被战斗打断，视为"中断结束"，给当前 `mCurrentSourceDevice` 写入冷却。
- 被 Stunned 打断（Interact("Back")）：视为"中断结束"，同上写入冷却。
- 被 Dead / SceneStop：不写冷却（对象将销毁或场景重置）。

**同源冷却过滤时机**：
- 在 `OnEnemyAnomalyEventFired` 中先判 `mCurrentSourceDevice`（当前源不打断），再判 `mSourceCooldowns` 冷却期（链条结束后的静默期）。
- 冷却期内的同源事件被过滤（在 FR-2.2 第 3 步）。
- 不同 `SourceDevice` 之间独立计时，不互相影响。
- 冷却字典条目**永久保留**（游戏内 EnemyBase 生命周期通常有限；如担心内存，可在 `OnDestroy` 或 `mSourceCooldowns.Count > 32` 时按过期时间做一次清理，本期不做）。

**默认参数**：`mSameSourceCooldown = 5f`（可 Inspector 配置）。

**设计动因（结合两条来源）**：
- BrokenGlass 自身冷却（`mCooldownSeconds = 3f`）只解决"玩家原地来回穿越 Trigger"的抖动。
- 当前源不打断：EnemyBase 在调查同一块玻璃的过程中（十几秒），玩家或异敌又踩了它，敌人已经在处理这个线索，不应该重新惊动一次（否则会重置 Alerted 计时，浪费玩家时间）。
- 链条结束后冷却：调查完毕后立刻再被同一块玻璃吸引也不合理（"我刚检查完这块玻璃、周围没异常"）。冷却应从链条结束开始，而不是从进入 Alerted 开始，否则 Investigate/Inspect 期间的十几秒已经把 5s 冷却耗完，冷却起不到隔离作用。
- 一群敌人依次踩同一块玻璃：只要**第一个非自触发**敌人的调查链条还没结束，或者结束后 5s 内，其他敌人再踩同一玻璃就不会广播——不对，广播依然会发（BrokenGlass 只知道自己冷却过没过）；关键是**接收方**的 `mCurrentSourceDevice`（如果已在调查同源就忽略）或 `mSourceCooldowns`（如果链条刚结束且在冷却期内就忽略）过滤保证同一敌人不会被同一玻璃"再触发"。

#### FR-2.6 仅警觉模式（EnemyBase 触发路径）

- 新增字段：`bool mAlertOnly`、`string mPreAlertState`（合法值："Idle" / "Move"）。
- 进入 Alerted 时的分流（配合 FR-2.4 的三种去向）：
  - 完整调查（进入前 Idle/Move）：`mAlertOnly = false`；Alerted 结束后 `ChangeState("Investigate")`。
  - 仅警觉（进入前 Idle/Move）：`mAlertOnly = true`；Alerted 结束后 `ChangeState(mPreAlertState)`。
  - 完整调查中被异敌事件打断（进入前 Investigate/Inspect + 新事件是"仅警觉"型）：`mAlertOnly = false`（不降级），且**不更新** `mAnomalySource`（保留原调查目标）；Alerted 结束后 `ChangeState("Investigate")`。
- `mPreAlertState` 只在**首次**从 Idle/Move 进入 Alerted 时记录；后续 Alerted/Investigate/Inspect 被新异常打断时**不覆盖 mPreAlertState**（保持"最初出发点"，仅在仅警觉路径下有效）。
- 若 `mPreAlertState` 记录时刻角色处于 `Move`，退出 Alerted 时切回 `Move`，会走原有 `OnMoveEnter/FixedUpdate` 继续巡逻；若为 `Idle`，则回 Idle 等待 `mWaitTime` 再切下一段 Move。

Chase 追丢改造（详见 §4.3）；Chase / Searching 期间不响应新异常：

```
Chase --OnVisionExit 且丢失 target-->  记录 mLostSightPos --> Searching
Searching --X 轴对齐到 mLostSightPos-->  Inspect
Chase / Searching --OnHearAnomaly-->  (IBattleState → 忽略)
```

### 4.3 Chase 追丢改造 —— 新增 `Searching` 状态

- 新增 `SearchingState : FSMStateBase`（不实现三个接口）。
- 触发路径：
  - 现有 `OnVisionExit` 直接切 `Idle` → 改成先记录 `mLostSightPos = player.transform.position` 再切 `Searching`。
  - 现有 `OnChaseFixedUpdate` 中"target 变 null / IsDead / IsUndetectable"分支也走同一路径，但要区分"没有 mChaseTarget"时 fallback 到直接切 Idle（防御 NPE）。
- `Searching` 行为：以 `mChaseSpeed` 移动到 `mLostSightPos`（X 轴对齐即可）。到达后清空 `mLostSightPos` 并切 `Inspect`。
- `OnChaseExit` 里现有"设最远巡逻点 + mIsReturningToPatrol=true"逻辑**保留**但仅在**从 Chase 直接切到 Idle**（防御路径）时生效；正常路径 Chase→Searching→Inspect 已覆盖。
- 为避免语义混淆：`SetTargetToFarthestPatrolPoint()` 由 `Inspect` 状态在结束时调用；`OnChaseExit` 里的调用**移除**（Chase → Searching → Inspect 三段收口更干净）。

### 4.4 `PatrolPointConfig` 组件与朝向应用

#### FR-4.1 组件设计

- 新增 `PatrolPointConfig : MonoBehaviour`，挂在巡逻点 Transform 上（可选组件；不挂等同 `KeepCurrent`）。
- 枚举 `PatrolFacing`：`KeepCurrent | Left | Right | AutoByNextMove`。
- 字段：`public PatrolFacing Facing = PatrolFacing.KeepCurrent;`。

| 枚举 | 语义 |
|------|------|
| `KeepCurrent` | 到达该点 Idle 时不主动改变朝向 |
| `Left` | 到达该点 Idle 时强制面向左（`transform.localScale.x < 0`） |
| `Right` | 到达该点 Idle 时强制面向右 |
| `AutoByNextMove` | 到达该点 Idle 时按"下一个巡逻点相对方向"翻朝向；若无下一个点（列表空/单点）退化为 `KeepCurrent` |

#### FR-4.2 应用时机与范围（对应用户选择 `idle_arrive_only`）

- **仅在**"到达巡逻点、由 Move 切入 Idle 时"应用一次（即 `OnIdleEnter` 检测到本次 Idle 是**从巡逻点抵达**而非 Chase/Inspect 回归时）。
- 不在 Idle 每帧强制刷新（避免影响 Alerted 转头、Inspect 张望等主动改朝向的逻辑）。
- Inspect / Searching 到达异常源后进入的 Idle **不受此配置影响**（因为不在巡逻点上）。

#### FR-4.3 Editor 可用性

- `PatrolPointConfig` 直接暴露在 Inspector；无需自定义 Editor。
- 可选（本期不做）：在 Scene 视图上通过 Gizmos 画一个小箭头指示配置方向。若不实现，仅靠 Inspector 也能满足需求。

---

## 5. 非功能需求

- **性能**：BrokenGlass 采用事件总线广播，接收方一次 `Vector2.Distance` + Dictionary Lookup 完成距离与冷却过滤；不做 `Physics2D.OverlapCircleAll`。
- **兼容性**：
  - 不改动 v0.21.7 EnemyBase 已有的 Chase/Stunned 行为语义；仅追加新状态与改造 Chase→Idle 的直连。
  - 不挂 `PatrolPointConfig` 的旧场景巡逻点行为保持不变（`KeepCurrent` 默认值）。
- **可观测**：EnemyBase 的四个新状态在 `OnStateChanged` 事件中正常发射；`SceneObjInfoRenderer` 若渲染状态名，会看到 `Alerted / Investigate / Inspect / Searching`。
- **不破坏 SceneObjInfoMapper 现有映射**：不新增 Python 端字段。

---

## 6. 验收标准

- [ ] 在场景中放一个 `BrokenGlass`，让 `PlayerBase` 走进 Trigger：
  - [ ] 范围内的 `EnemyBase`（不在 Chase/Searching/Stunned/Dead）依次 `Alerted → Investigate → Inspect → Idle`（回最远巡逻点）。
  - [ ] Chase 状态下的 EnemyBase 不被吸引（`IBattleState` 过滤）。
  - [ ] Searching 状态下的 EnemyBase 不被吸引（`IBattleState` 过滤）。
  - [ ] Stunned 状态下的 EnemyBase 不被吸引。
- [ ] 敌人已在 `Alerted / Investigate / Inspect` 中，**另一块**（异源）`BrokenGlass` 在新位置触发：敌人打断当前流程、面朝新源、走向新源、张望新源（旧源被写入冷却）。
- [ ] 敌人已在 `Alerted / Investigate / Inspect` 中，**同一块**（同源）`BrokenGlass` 再次触发：敌人**完全无反应**，Alerted 计时不重置、`mAnomalySource` 不改变，继续原调查。
- [ ] EnemyBase 自己踩到 `BrokenGlass`：**不进入 Alerted**，维持当前状态（自触发忽略）。
- [ ] 一块 `BrokenGlass` 半径内有两个 EnemyBase A/B，A 踩到玻璃：A 不响应；B 进入 `Alerted`，`mAlertedSeconds` 结束后**回原 Idle/Move**（同时该玻璃被写入 B 的 `mSourceCooldowns`），不进 Investigate / Inspect。
- [ ] B 处于"仅警觉"Alerted 中，Player 触发另一块 `BrokenGlass`：B 升级为完整调查（Investigate → Inspect → 最远巡逻点），旧异敌源被写入冷却，不再回 A 触发时记录的 `mPreAlertState`。
- [ ] B 处于"完整调查"Alerted / Investigate / Inspect 中，另一个 EnemyBase A 触发另一块 `BrokenGlass`：B **保持完整调查**（不降级、不改变 `mAnomalySource`、不改变 `mCurrentSourceDevice`、异敌源不写入 B 的冷却），Alerted 结束后继续走向原调查目标 P，完成 Investigate → Inspect → 最远巡逻点。
- [ ] EnemyBase 完成一次针对 X 的完整调查后（Inspect 结束）5 秒内，X 再次被踩：敌人**不响应**（X 已在其 `mSourceCooldowns` 中且未过期）；5 秒后 X 被踩：敌人正常响应。
- [ ] EnemyBase 走向 BrokenGlass X 的途中（`Investigate` 状态），X 再次被踩（Player 或其他 SceneObj）：敌人**继续走向 X**，不重进 Alerted、不重置计时。
- [ ] 一群 EnemyBase 依次经过同一块 `BrokenGlass`：\
  - 只有第一个进入 Trigger 的敌人触发广播（其余在 BrokenGlass 自身冷却 3s 内被 BrokenGlass 屏蔽）；
  - BrokenGlass 冷却过后另一敌人再踩，本轮所有已响应过该源的敌人在 `mSameSourceCooldown = 5s` 内（且冷却计时从各自调查链条结束时开始）**不重复响应**。
- [ ] `BrokenGlass` 触发一次后进入冷却，冷却期内玩家再次进入 Trigger **不再触发**；冷却结束后再次进入可再次触发。
- [ ] Scene 视图选中 `BrokenGlass`，可看到吸引半径 Gizmos。
- [ ] EnemyBase 在 Chase 中脱离视野时：
  - [ ] 立即进入 `Searching`，走向玩家最后位置。
  - [ ] 到达后进入 `Inspect`（张望），随后走向最远巡逻点。
- [ ] `PatrolPointConfig`：
  - [ ] 在某巡逻点上设置 `Left`，敌人到达该点 Idle 时朝向立即变为左。
  - [ ] 设置 `AutoByNextMove` 时，敌人朝下一个巡逻点方向翻朝向。
  - [ ] 不挂该组件的巡逻点行为与旧版一致。

---

## 7. 待确认问题

- [x] 碎玻璃触发后生命周期 → **冷却可重复触发**。
- [x] Inspect 结束后走"最远巡逻点"（复用 Chase 追丢的产品直觉）。
- [x] 追丢瞬间不直接切 Idle，而是新增 `Searching` 状态，走到消失位置再进入 `Inspect`。
- [x] 巡逻点朝向枚举包含 `AutoByNextMove` 分支。
- [x] 朝向仅在到达巡逻点的 Idle 瞬间应用一次。
- [x] 感知机制方案：本期采纳方案 C（全局事件总线，QFramework 风格 `SendEvent<EnemyAnomalyEvent>`）。
- [x] 触发形状：BoxCollider2D。
- [x] `Alerted / Investigate / Inspect` 中收到新的异常源：**打断当前调查并从 Alerted 重新开始**（用新的 `sourcePos`）。
- [x] `Chase / Searching` 属于战斗状态，通过新增 `IBattleState` 接口标记，异常事件对其无效。
- [x] 事件类命名 `EnemyAnomalyEvent`（避免与"系统异常"混淆）。
- [x] EnemyBase 自触发过滤：`triggerer == this` → 忽略（避免踩到自己触发的碎玻璃形成死循环）。
- [x] 异敌触发 → "仅警觉"模式：`EnemyAnomalyEvent` 携带 `Triggerer: SceneObjBase`，`triggerer is EnemyBase && triggerer != this` 时只做 Alerted、不做 Investigate/Inspect；Alerted 结束回到 `mPreAlertState`（Idle/Move）。玩家或装置本身触发保持完整调查。
- [x] 完整调查中被异敌"仅警觉"型事件打断：Alerted 结束后**继续原调查目标**（不覆盖 `mAnomalySource`），走 Investigate → Inspect → 最远巡逻点。
- [x] 同源不打断：EnemyBase 已在响应某个 `sourceDevice` 的调查链（Alerted/Investigate/Inspect）中时，同一 `sourceDevice` 的新事件完全忽略（不重进 Alerted、不重置计时、不改变字段）。
- [x] 同源冷却：`EnemyAnomalyEvent` 携带 `SourceDevice`；EnemyBase 每个源独立维护 `mSameSourceCooldown = 5f` 冷却；**冷却从调查链条结束/中断时才开始计算**（Inspect 结束、Alerted 回 mPreAlertState、被 Chase/Stunned 抢占、被完整调查升级替换），避免 Investigate/Inspect 十几秒期间冷却已过、起不到隔离效果。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成；确认前请勿据此改代码。*

