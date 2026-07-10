# 技术方案 — v0.22.1 EnemyBase 异常事件吸引与巡逻点朝向配置

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-07-10

---

## 1. 方案概述

在 Unity 侧新增：
1. `BrokenGlass : DeviceBase` 装置（发声源，Gizmos 可视化，冷却可重复触发）。
2. EnemyBase 新增 4 个 FSM 状态：`Alerted / Investigate / Inspect / Searching`。
3. `PatrolPointConfig : MonoBehaviour` 组件，暴露 `Facing` 枚举，仅在敌人抵达巡逻点 Idle 瞬间应用一次朝向。

不改动协议、Python、记忆、Agent 工具。

## 2. 影响范围

| 层级 | 模块 / 路径 | 变更类型 |
|------|-------------|----------|
| Unity | `.../Event/EnemyAnomalyEvent.cs` | 新增 |
| Unity | `.../SceneObj/Device/BrokenGlass.cs` | 新增 |
| Unity | `.../FSM/IBattleState.cs` | 新增（FSM 标记接口，Chase / Searching 实现） |
| Unity | `.../SceneObj/Base/SceneObjBase.cs` | 修改（追加 `IsInBattle` 属性） |
| Unity | `.../SceneObj/Chara/EnemyBase.cs` | 修改（新增 4 状态 + 追丢逻辑改造 + `OnEnemyAnomalyEventFired` + `OnHearAnomaly` + Chase/Searching 实现 IBattleState） |
| Unity | `.../SceneObj/Chara/PatrolPointConfig.cs` | 新增 |
| Unity | Prefab / 场景资源 | 手工挂载新组件（本方案不改 prefab 二进制，由你在编辑器完成） |
| Python | — | 无 |
| 协议 | `Tools/message.proto` | 无 |
| 文档 | `AGENTS.md` | 不必然改（EnemyBase 段落若已提，则新增状态列表） |

## 3. 详细设计

### 3.1 `EnemyAnomalyEvent.cs` （新增）

路径：`Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/Event/EnemyAnomalyEvent.cs`

```csharp
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 敌人 AI 感知用的"异常源事件"。发送方是任意声源（碎玻璃、脚步、玩家说话等），
    /// 接收方是订阅了本事件的 AI 单位（当前仅 EnemyBase）。
    /// 命名以 Enemy 前缀，明确"AI 感知"语义，避免与"系统异常/错误"混淆。
    ///
    /// Triggerer 是"引发本次异常源"的场景对象（谁踩了碎玻璃）；接收方基于此分流：
    /// - Triggerer == 自己：忽略（避免自触发死循环）。
    /// - Triggerer is EnemyBase 且 != 自己：仅警觉（Alerted 后回上一状态或继续原调查，不进 Investigate 新目标）。
    /// - 其他（PlayerBase / 装置自身 / null）：完整调查流程。
    ///
    /// SourceDevice 是声源装置本身（BrokenGlass 实例）；接收方基于此维护
    /// "每 EnemyBase 对每 SourceDevice 的独立冷却"，避免一群敌人排队踩同一块玻璃时
    /// 每次广播都让所有敌人再次 Alerted。
    /// </summary>
    public class EnemyAnomalyEvent
    {
        public Vector2 SourcePos;
        public float Radius;
        public SceneObjBase Triggerer;
        public SceneObjBase SourceDevice;
    }
}
```

参照现有 `GameOverEvent`，纯数据类。

### 3.2 `IBattleState.cs` （新增）

路径：`Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/FSM/IBattleState.cs`

```csharp
namespace IndependentAgentProject
{
    /// <summary>
    /// FSM 标记接口：实现此接口的 FSMState 表示"角色处于战斗状态"，
    /// 不应被异常事件（EnemyAnomalyEvent 等）干扰。
    /// SceneObjBase.IsInBattle 会通过 (mCurState is IBattleState) 统一判定。
    /// 本期实现者：EnemyBase.ChaseState、EnemyBase.SearchingState。
    /// </summary>
    public interface IBattleState { }
}
```

同步修改 `SceneObjBase.cs`，在 `IsInvulnerable` 定义下方追加：

```csharp
/// <summary>
/// 当前状态是否"处于战斗中"（即 mCurState 实现 IBattleState）。
/// 用于异常事件（EnemyAnomalyEvent）等过滤：战斗中不响应异常吸引。
/// </summary>
public virtual bool IsInBattle => mCurState is IBattleState;
```

### 3.3 `BrokenGlass.cs` （新增）

路径：`Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Device/BrokenGlass.cs`

要点：

- 继承 `DeviceBase`（QFramework 语义可用 `this.SendEvent<T>()`）。
- `Name` = "碎玻璃"，`Desc` = "地上散落的碎玻璃，踩到会发出声响。"
- `IsInteractable => false`；`IsClickable => false`；`Interact` 沿用父类默认（返回 false）。
- 必须组件：`BoxCollider2D`（IsTrigger=true）。Box 的 size / offset 决定"进入即触发"的判定形状。
- 字段：
  - `[SerializeField] float mAttractRadius = 5f;` 声音传播半径，作为 `EnemyAnomalyEvent.Radius` 上送（**不用于**装置侧物理查询）。
  - `[SerializeField] float mCooldownSeconds = 3f;` 冷却时间。
  - （**不需要** `mEnemyLayerMask`；采用事件订阅，敌人识别由订阅方 EnemyBase 天然完成。）
- 私有状态：`float mCooldownEndTime = 0f;`
- `OnTriggerEnter2D(Collider2D other)`：
  - 若 `Time.time < mCooldownEndTime` 直接 return。
  - 通过 `SceneObjBase sceneObj = other.GetComponentInParent<SceneObjBase>()` 判定是否为场景对象（保证语义"任何 SceneObj 进入都触发"）；null 则 return。
  - 设 `mCooldownEndTime = Time.time + mCooldownSeconds;`
  - `this.SendEvent(new EnemyAnomalyEvent { SourcePos = transform.position, Radius = mAttractRadius, Triggerer = sceneObj, SourceDevice = this });`
  - `SourceDevice = this` 让接收方 EnemyBase 能按声源实例做"独立冷却"，见 §3.5.4。
- `OnDrawGizmos()`：
  - Trigger 区域：黄色 `Gizmos.DrawWireCube`。取 `GetComponent<BoxCollider2D>()`，用 `transform.TransformPoint(box.offset)` 作为中心，`Vector3.Scale((Vector3)box.size, transform.lossyScale)` 作为尺寸。若 Box 不存在则 skip。
  - 吸引半径：红色 `Gizmos.DrawWireSphere(transform.position, mAttractRadius)`。

**关于"不查询范围"的实现**：`BrokenGlass` 只 SendEvent，不做任何 `Physics2D.Overlap*` 调用，也不引用 `EnemyBase` 类型。距离过滤全部在 §3.4 EnemyBase 的订阅回调里做。这也回答"敌人不在范围内也要广播吗"—— 是的，**发送方无差别广播，接收方各自决定要不要响应**，这是事件订阅模式的核心。

### 3.4 `PatrolPointConfig.cs` （新增）

路径：`.../SceneObj/Chara/PatrolPointConfig.cs`

```csharp
namespace IndependentAgentProject
{
    public enum PatrolFacing { KeepCurrent, Left, Right, AutoByNextMove }

    public class PatrolPointConfig : MonoBehaviour
    {
        public PatrolFacing Facing = PatrolFacing.KeepCurrent;
    }
}
```

无 Update / Gizmos，纯数据组件。

### 3.5 `EnemyBase.cs` 改造

#### 3.5.1 新增字段

```csharp
[Header("异常调查配置")]
[SerializeField] private float mAlertedSeconds = 1f;
[SerializeField] private float mInspectSeconds = 5f;
[SerializeField] private float mInspectTurnInterval = 1.2f;
[SerializeField] private float mSameSourceCooldown = 15f;

private Vector2 mAnomalySource;
private Vector2 mLostSightPos;
private float mStateTimer = 0f;
private float mInspectTurnTimer = 0f;
private bool mArrivedFromPatrol = false;

// 仅警觉模式相关：由其他 EnemyBase 触发异常源时，只做警觉不做调查。
private bool mAlertOnly = false;
private string mPreAlertState = "Idle"; // "Idle" or "Move"

// 当前正在响应（Alerted/Investigate/Inspect）的声源装置。用于两处判定：
// 1) 同源事件不打断当前调查链（避免同一块玻璃反复重置计时）。
// 2) 链条结束时统一给该源写入冷却（详见 mSourceCooldowns 说明）。
// 在 Alerted 首次进入或"更换调查目标"时写入；在链条结束（Inspect 结束、
// Alerted 回 mPreAlertState、被 Chase/Stunned 抢占）时清空并写冷却。
private SceneObjBase mCurrentSourceDevice = null;

// 每个声源装置对本敌人的独立冷却截止时间。防止敌人刚检查完某块玻璃就立刻
// 又被同一块玻璃再次吸引。冷却在**调查链条结束时**写入，而不是在 Alerted
// 进入时写入，否则十几秒的 Investigate+Inspect 期间冷却就过完，起不到隔离效果。
private Dictionary<SceneObjBase, float> mSourceCooldowns = new Dictionary<SceneObjBase, float>();
```

`mArrivedFromPatrol` 标志用于 `OnIdleEnter` 判断本次 Idle 是否是"从巡逻点抵达"（在 Move → Idle 抵达处理里设 true；从 Chase/Searching/Inspect 走回 Idle 时保持 false）。

#### 3.5.2 新增状态类

- `AlertedState` / `InvestigateState` / `InspectState`：模式沿用 `ChaseState`（`OnEnter/OnUpdate/OnFixedUpdate/OnExit` 里 `if (o is EnemyBase e) e.OnXxx()`）；**不实现** `IUndetectableState / IImmovableState / IInvulnerableState / IBattleState`（保持可被视野发现、可被背刺、可被新异常打断）。
- `SearchingState`：**实现 `IBattleState`**（追丢状态语义上仍是战斗，不接受异常打断）。
- 同时把 **`ChaseState` 也补一个 `IBattleState` 实现**（v0.21.7 时未定义此接口，本期一并补上）。
- `Awake` 中 `RegisterState` 全部四个新状态。

#### 3.5.3 状态回调

- `OnAlertedEnter`：置 `mChaseTarget = null`；速度=0；`mStateTimer = 0`；`TurnBack(mAnomalySource.x - transform.position.x)`。
- `OnAlertedUpdate`：`mStateTimer += Time.deltaTime`；到 `mAlertedSeconds` 时按 §FR-2.4 表格选择去向：
  - `mAlertOnly == true`（进入 Alerted 前是 Idle/Move + 异敌触发） → **写冷却**：`WriteCooldownAndClearCurrentSource()`；`ChangeState(mPreAlertState)`（回 Idle 或 Move）。
  - `mAlertOnly == false`（完整调查，无论是首次进入还是完整调查中被打断） → `ChangeState("Investigate")`；`mAnomalySource` 与 `mCurrentSourceDevice` 已在 `OnHearAnomaly` 里按"是否保留原目标"规则处理好；冷却在 Inspect 结束时统一写入。
- `OnInvestigateEnter`：速度=0；`TurnBack(mAnomalySource.x - transform.position.x)`。
- `OnInvestigateFixedUpdate`：走向 `mAnomalySource.x`；X 轴对齐（`Mathf.Abs(dx) < kArriveEpsilonX`）→ 速度=0 → `ChangeState("Inspect")`；否则 `velocity = new Vector2(dir * mPatrolSpeed, vy)`。
- `OnInspectEnter`：速度=0；`mStateTimer = 0`；`mInspectTurnTimer = 0`。
- `OnInspectUpdate`：`mStateTimer += Time.deltaTime`；`mInspectTurnTimer += Time.deltaTime`。
  - `mInspectTurnTimer >= mInspectTurnInterval` → 翻朝向（`TurnBack(-Mathf.Sign(transform.localScale.x))`）；`mInspectTurnTimer = 0`。
  - `mStateTimer >= mInspectSeconds` → **完整调查链条结束**：`WriteCooldownAndClearCurrentSource()`；`SetTargetToFarthestPatrolPoint()` → `ChangeState("Idle")`（后续 `OnIdleUpdate` 里 `mIsReturningToPatrol` 分支会等待 `mWaitTime` 再切 Move）。
- `OnSearchingEnter`：速度=0；`TurnBack(mLostSightPos.x - transform.position.x)`。
- `OnSearchingFixedUpdate`：走向 `mLostSightPos.x`；对齐 → 速度=0 → `ChangeState("Inspect")`。使用 `mChaseSpeed`（保持追击语义）。
- `OnChaseEnter` / `OnStunnedEnter`：若 `mCurrentSourceDevice != null` → `WriteCooldownAndClearCurrentSource()`（把当前调查链条视为"被战斗/眩晕中断"，写冷却）。

`WriteCooldownAndClearCurrentSource()` 辅助方法：

```csharp
private void WriteCooldownAndClearCurrentSource()
{
    if (mCurrentSourceDevice != null)
    {
        mSourceCooldowns[mCurrentSourceDevice] = Time.time + mSameSourceCooldown;
        mCurrentSourceDevice = null;
    }
}
```

#### 3.5.4 `EnemyAnomalyEvent` 订阅入口

- `Awake()` 末尾：
  ```csharp
  this.RegisterEvent<EnemyAnomalyEvent>(OnEnemyAnomalyEventFired)
      .UnRegisterWhenGameObjectDestroyed(this);
  ```
- 回调（含距离过滤 + 当前源过滤 + 同源冷却过滤）：
  ```csharp
  private void OnEnemyAnomalyEventFired(EnemyAnomalyEvent evt)
  {
      // 1) 距离过滤
      if (Vector2.Distance(transform.position, evt.SourcePos) > evt.Radius) return;
      // 2) 当前调查源不打断：已在响应该源的调查链中，忽略同源新事件。
      //    避免"敌人正在走向 BrokenGlass X 或已在 X 处张望"时又被 X 重新惊动、重置计时。
      if (evt.SourceDevice != null && evt.SourceDevice == mCurrentSourceDevice) return;
      // 3) 同源冷却过滤：本敌人对本 SourceDevice 是否处于冷却期
      //    （冷却从上一次针对该源的调查链条结束/中断后开始计算）。
      if (evt.SourceDevice != null
          && mSourceCooldowns.TryGetValue(evt.SourceDevice, out float endTime)
          && Time.time < endTime)
      {
          return;
      }
      OnHearAnomaly(evt.SourcePos, evt.Triggerer, evt.SourceDevice);
  }
  ```
- 需要实测：`UnRegisterWhenGameObjectDestroyed` 是 QFramework 常用扩展；若当前项目版本没有该扩展，退化为在 `OnDestroy` 里手动 `UnRegisterEvent`。开发前先 grep `UnRegisterWhenGameObjectDestroyed` 与 `RegisterEvent` 的现有用法确认 API 存在。

`OnHearAnomaly(sourcePos, triggerer, sourceDevice)` 入口（按 FSM 接口过滤 + Triggerer 分流 + 保留原调查目标规则）：

```csharp
public void OnHearAnomaly(Vector2 sourcePos, SceneObjBase triggerer, SceneObjBase sourceDevice)
{
    // 1) 自触发忽略（EnemyBase 也会踩到 BrokenGlass 触发事件）。
    if (triggerer == this) return;
    // 2) 无法响应：Stunned（IsImmovable）、Dead。
    if (IsImmovable || IsDead) return;
    // 3) 战斗中（Chase / Searching）不受异常打扰。
    if (IsInBattle) return;

    // 4) 分流：其他敌人触发 → 仅警觉；其余 → 完整调查。
    bool eventIsAlertOnly = triggerer is EnemyBase && triggerer != this;

    // 5) 首次从 Idle/Move 进入 Alerted 时记录 mPreAlertState；
    //    已在 Alerted/Investigate/Inspect 中被打断时不覆盖，保持"最初出发点"。
    bool enteringFromIdleOrMove = (StateName == "Idle" || StateName == "Move");
    if (enteringFromIdleOrMove)
    {
        mPreAlertState = StateName;
    }

    // 6) mAlertOnly / mAnomalySource / mCurrentSourceDevice 的更新规则（FR-2.4 表格）：
    //    关键约束：
    //      A. 从 Idle/Move 首次进入：按 eventIsAlertOnly 直接赋值，覆盖 mAnomalySource
    //         与 mCurrentSourceDevice（首次没有"旧源"要写冷却）。
    //      B. 已在完整调查 (mAlertOnly==false) 中：
    //         B1. 被"仅警觉"型打断：保持 false；【不覆盖】mAnomalySource 与
    //             mCurrentSourceDevice（保留原调查目标；新的异敌源不成为当前源，
    //             也不写它的冷却——它只是让敌人抬头警觉一下）。
    //         B2. 被"完整调查"型打断：保持 false；【替换】mAnomalySource 为新源；
    //             【给旧的 mCurrentSourceDevice 写冷却】然后替换为新源。
    //      C. 已在仅警觉 (mAlertOnly==true) 中：
    //         C1. 被"完整调查"型打断：升级为 false；【替换】mAnomalySource；
    //             【给旧的 mCurrentSourceDevice 写冷却】然后替换为新源。
    //         C2. 被"仅警觉"型打断：保持 true；【替换】mAnomalySource；
    //             【给旧的 mCurrentSourceDevice 写冷却】然后替换为新源
    //             （Alerted 面朝新源；Alerted 结束仍回 mPreAlertState，届时再给
    //              新源写冷却）。
    if (enteringFromIdleOrMove)
    {
        mAlertOnly = eventIsAlertOnly;
        mAnomalySource = sourcePos;
        mCurrentSourceDevice = sourceDevice;
    }
    else if (!mAlertOnly && eventIsAlertOnly)
    {
        // B1: 完整调查中被异敌事件打断：保留原调查目标与原当前源，不动 mAlertOnly。
        // Alerted 会用当前 mAnomalySource 面朝——保留原目标意味着敌人面朝原调查方向
        // 做 Alerted 反应，短暂警觉后继续 Investigate 原目标。这一"面朝原方向"的
        // 行为是合理的：敌人被异敌短暂惊动，但注意力仍在原线索上。
        // 关键：这里不给新的 sourceDevice 写冷却（未成为当前源），下次它主动触发
        // 时如果 B/C 都空闲，仍能正常吸引。
    }
    else
    {
        // B2 / C1 / C2：需要替换当前源。
        // 先给旧的当前源写冷却（旧的调查链条视为"被新事件中断结束"）。
        if (mCurrentSourceDevice != null && mCurrentSourceDevice != sourceDevice)
        {
            mSourceCooldowns[mCurrentSourceDevice] = Time.time + mSameSourceCooldown;
        }
        if (!eventIsAlertOnly) mAlertOnly = false; // 完整调查覆盖仅警觉
        // else 仅警觉打断仅警觉 → mAlertOnly 保持 true
        mAnomalySource = sourcePos;
        mCurrentSourceDevice = sourceDevice;
    }

    ForceReenterAlerted();
}
```

行为说明（对应 §FR-2.4 表格）：
- `Idle / Move`（首次） + 完整调查事件 → `mAlertOnly=false`, `mAnomalySource=新`, `mCurrentSourceDevice=新`；Alerted → Investigate 新目标 → Inspect → 结束时写冷却。
- `Idle / Move`（首次） + 异敌事件 → `mAlertOnly=true`, `mAnomalySource=新`, `mCurrentSourceDevice=新`, `mPreAlertState=Idle/Move`；Alerted → `mPreAlertState`（Alerted 结束时写冷却）。
- `Investigate/Inspect`（完整调查中） + **同源**事件 → **完全忽略**（`OnEnemyAnomalyEventFired` 第 2 步过滤，不重进 Alerted）。
- `Investigate/Inspect`（完整调查中） + 异源 + 完整调查事件 → 给旧 `mCurrentSourceDevice` 写冷却；覆盖 `mAnomalySource` 与 `mCurrentSourceDevice` 为新源；Alerted → Investigate 新目标 → Inspect → 新源写冷却。
- `Investigate/Inspect`（完整调查中） + 异源 + 异敌事件 → **保留** `mAnomalySource` 与 `mCurrentSourceDevice`（新源不成为当前源）；Alerted 面朝原方向（用当前 `mAnomalySource`）→ Investigate 原目标继续。
- `Alerted / Investigate / Inspect`（仅警觉中） + 异源 + 完整调查事件 → 给旧 `mCurrentSourceDevice` 写冷却；升级 `mAlertOnly=false`；覆盖 `mAnomalySource` 与 `mCurrentSourceDevice` 为新源；Alerted → Investigate 新目标。
- `Alerted / Investigate / Inspect`（仅警觉中） + 异源 + 异敌事件 → 给旧 `mCurrentSourceDevice` 写冷却；覆盖 `mAnomalySource` 与 `mCurrentSourceDevice` 为新源；`mAlertOnly` 保持 true；Alerted 面朝新源 → `mPreAlertState`。
- `Chase / Searching`（`IBattleState`） → 忽略；此前若有 `mCurrentSourceDevice` 已在 `OnChaseEnter` 里写冷却清空。
- `Stunned`（`IImmovableState`）/ `Dead` → 忽略；`OnStunnedEnter` 同上写冷却。
- `triggerer == this`（自触发） → 忽略。
- 同一 `sourceDevice` 冷却期内的事件 → 忽略。
- 同源事件（正在响应中） → 忽略（不改任何状态）。

`ForceReenterAlerted` 辅助方法：

```csharp
private void ForceReenterAlerted()
{
    // 同名状态 ChangeState 一般会短路，不触发 OnExit/OnEnter。
    // 这里显式先切一个中转状态再切回 Alerted，保证 Enter 回调重跑，计时归零。
    // 开发前需实测 CharaFSM.ChangeState 对"目标状态==当前状态"是否直接 return；
    // 若确认会短路，则用如下写法：
    if (StateName == "Alerted")
    {
        // 通过 Idle 中转一步，Idle 不做副作用（velocity=0 + mWaitTimer 归零 + 因 mArrivedFromPatrol==false 不改朝向）。
        ChangeState("Idle");
    }
    ChangeState("Alerted");
}
```

**实测项**：`CharaFSM.ChangeState` 与 `FSMBase.ChangeState` 对"目标==当前"是否有短路检查？若有，`ForceReenterAlerted` 的 Idle 中转是必需的；若没有（会正常触发 Enter/Exit），则可以直接 `ChangeState("Alerted")`。开发前先跑一个最小片段确认。

#### 3.5.5 视野与 Chase 追丢改造

- `OnVisionEnter` 保持现状（Chase 优先）。视野发现玩家时无论当前状态（除 IsImmovable / Chase 自身）都升级为 Chase：将原 `if (StateName == "Chase") return;` 保持；不加"Alerted 中不 Chase"这类白名单。
- `OnVisionExit` 修改：
  ```csharp
  if (StateName != "Chase") return;
  PlayerBase player = other.GetComponentInParent<PlayerBase>();
  if (player == null || player != mChaseTarget) return;
  mLostSightPos = player.transform.position;
  ChangeState("Searching");
  ```
- `OnChaseFixedUpdate` 中 target 变 null / dead / undetectable 分支：
  ```csharp
  if (mChaseTarget == null) { ChangeState("Idle"); return; }
  if (mChaseTarget.IsDead || mChaseTarget.IsUndetectable)
  {
      mLostSightPos = mChaseTarget.transform.position;
      ChangeState("Searching");
      return;
  }
  ```
- `OnChaseExit`：**移除**对 `SetTargetToFarthestPatrolPoint()` 的调用（现在由 Inspect 结束时调用）。仅保留 `velocity=0` 与 `mChaseTarget=null`。

#### 3.5.6 `OnIdleEnter` / `OnMoveFixedUpdate` 抵达处理

- `OnMoveFixedUpdate` 抵达分支（`Mathf.Abs(dx) < kArriveEpsilonX`）在切 Idle 前设 `mArrivedFromPatrol = true`（仅当 `mIsReturningToPatrol == false` 时算"正常巡逻抵达"，回归路径复用旧逻辑但也算抵达巡逻点）。
- `OnIdleEnter` 改造：
  ```csharp
  if (mRigidbody2D != null)
      mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
  mWaitTimer = 0f;
  if (mArrivedFromPatrol)
  {
      ApplyPatrolPointFacing();
      mArrivedFromPatrol = false;
  }
  ```
- `ApplyPatrolPointFacing()` 实现：
  ```csharp
  if (mTargetPoint == null) return;  // 会被 SetNextPatrolTarget 覆盖，这里防御
  var cfg = mPatrolPoints[mCurrentPatrolIndex]?.GetComponent<PatrolPointConfig>();
  if (cfg == null) return;
  switch (cfg.Facing)
  {
      case PatrolFacing.KeepCurrent: return;
      case PatrolFacing.Left:  TurnBack(-1f); return;
      case PatrolFacing.Right: TurnBack(1f); return;
      case PatrolFacing.AutoByNextMove:
          if (mPatrolPoints.Count <= 1) return;
          int next = (mCurrentPatrolIndex + 1) % mPatrolPoints.Count;
          var nextPt = mPatrolPoints[next];
          if (nextPt == null) return;
          TurnBack(nextPt.position.x - transform.position.x);
          return;
  }
  ```

**注意抵达 → Idle 的顺序**：`OnMoveFixedUpdate` 里当前是"设 `mTargetPoint = null; ChangeState("Idle")`"，`ApplyPatrolPointFacing` 需要 `mCurrentPatrolIndex` 与 `mPatrolPoints`，改为**先应用朝向再置空 mTargetPoint**：

```csharp
mIsReturningToPatrol = false;
mArrivedFromPatrol = true;
mTargetPoint = null;
ChangeState("Idle");
```

`OnIdleEnter` 里再基于 `mCurrentPatrolIndex` 读 `PatrolPointConfig`。

#### 3.5.7 Stunned 与四个新状态的冲突

`OnStunnedEnter` 已有 `mChaseTarget = null; mTargetPoint = null; mIsReturningToPatrol = false;` — 补一行 `mArrivedFromPatrol = false;` 与四个 timer 归零，避免下次退 Stunned（本期不做退 Stunned，但代码健壮性上留手）。

### 3.5 场景 / Prefab 配置（由你手工完成）

- `BrokenGlass` prefab：挂 `BoxCollider2D`（IsTrigger=true），Box 的 size 决定"进入即触发"的形状；`mAttractRadius` = 5（可视半径，红色 Gizmos）；`mCooldownSeconds` = 3。
- 巡逻点 Transform 需要固定朝向的，加 `PatrolPointConfig` 组件并选择枚举值。
- 不需要新建 Enemy Layer；EnemyBase 通过订阅 `EnemyAnomalyEvent` 自动响应，Layer 只影响 Trigger 进入判定（本期 Box 触发判定的层交给已有 `Physics2D` 层矩阵，`PlayerBase` 与其他 SceneObj 能进入 Trigger 即可）。

## 4. 实现步骤

1. 新增 `EnemyAnomalyEvent.cs`（纯数据事件类）。
2. 新增 `IBattleState.cs`；`SceneObjBase.cs` 追加 `IsInBattle` 属性。
3. 新增 `PatrolPointConfig.cs`（纯数据组件）。
4. 新增 `BrokenGlass.cs`（BoxCollider2D 触发 + SendEvent + Gizmos）。
5. 改造 `EnemyBase.cs`：
   - 新增字段与 4 个 FSM 状态类；`ChaseState` 与 `SearchingState` 实现 `IBattleState`。
   - `Awake` 里 `RegisterEvent<EnemyAnomalyEvent>` 并绑定生命周期。
   - 实现 `OnAlerted* / OnInvestigate* / OnInspect* / OnSearching*` 回调（Alerted 结束按 `mAlertOnly` 分流 Investigate vs `mPreAlertState`）。
   - 实现 `OnEnemyAnomalyEventFired`（距离过滤 + 同源冷却过滤） + `OnHearAnomaly(sourcePos, triggerer, sourceDevice)` + `ForceReenterAlerted` 入口，包含"自触发忽略 / 异敌仅警觉 / 完整调查不降级 / 完整调查中被异敌打断保留原目标 / 同源冷却"五条分流规则。
   - 改造 `OnVisionExit / OnChaseFixedUpdate / OnChaseExit / OnMoveFixedUpdate / OnIdleEnter`。
   - 添加 `ApplyPatrolPointFacing`。
6. 编译通过后：Unity Editor 中在测试场景放一个 EnemyBase + 两个巡逻点 + 一个 BrokenGlass，手工跑用户流程。
7. （文档）若 `AGENTS.md` §四 或某处列举了 EnemyBase 状态，补充新增的 4 个状态。

**开发前实测（遵守 AGENTS.md §开发纪律"第三方库参数 / API 必须先实测再写入代码"）**：

- 打开一个现有使用 `this.RegisterEvent<GameOverEvent>` 的文件（如 `PlayerBase.cs`），确认调用签名与项目引用的 QFramework 版本一致。
- 检查是否有 `UnRegisterWhenGameObjectDestroyed` 扩展；若无，就在 `OnDestroy` 手动 `UnRegisterEvent`。
- 实测 `CharaFSM.ChangeState` 对"目标==当前"是否短路，决定 `ForceReenterAlerted` 是否需要 Idle 中转。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| Chase 优先级判断误伤：Alerted 中被视野发现应能升级为 Chase | `OnVisionEnter` 现在只挡 `IsImmovable` 与 `StateName == "Chase"`；Alerted/Investigate/Inspect/Searching 中 IsImmovable=false，会正常升级到 Chase ✓ |
| Inspect / Searching 状态中被背刺 | 四个新状态都不实现 IImmovableState/IUndetectableState/IInvulnerableState，走原有 `Interact("Back")` → Stunned 流程 ✓ |
| 新异常源在 Alerted / Investigate / Inspect 中出现 | `OnHearAnomaly` 通过 `IsInBattle` 过滤，Chase/Searching 中忽略；其他状态一律"重进 Alerted"以新的 sourcePos 从头开始 ✓ |
| Chase / Searching 中出现异常源 | 两个状态都实现 `IBattleState`，`OnHearAnomaly` 直接 return，不打断战斗节奏 ✓ |
| EnemyBase 自己踩到 BrokenGlass 触发事件，导致自己被吸引 | `OnHearAnomaly` 第一层过滤 `triggerer == this` 直接 return ✓ |
| 一个 EnemyBase 触发异常源，导致全场敌人集体聚集 | 异敌触发时进入"仅警觉"模式，Alerted 结束后回原 Idle/Move 或继续原调查目标，不走 Investigate 新位置 ✓ |
| 一群 EnemyBase 排队踩同一块 BrokenGlass，每次都让全体停下 | 每个 EnemyBase 对每个 SourceDevice 维护 `mSameSourceCooldown=5f` 独立冷却，且冷却**从调查链条结束时开始**（Inspect 结束/Alerted 回 mPreAlertState 时写入），而不是进入 Alerted 时就写入；这样能真正隔离"敌人刚检查完就又被同一块玻璃吸引"的场景 ✓ |
| EnemyBase 正在调查 BrokenGlass X 途中，X 又被踩了一次（玩家来回穿越或另一敌人踩到 X） | `OnEnemyAnomalyEventFired` 通过 `mCurrentSourceDevice == evt.SourceDevice` 直接忽略，不重进 Alerted、不重置计时 ✓ |
| 完整调查中被"异敌仅警觉"事件误改成新的仅警觉目标 | `OnHearAnomaly` 完整调查中收到异敌事件时**不覆盖 mAnomalySource**，Alerted 结束继续走原目标 ✓ |
| 已在完整调查中被"异敌仅警觉"打断，误降级为仅警觉 | `OnHearAnomaly` 明确"完整调查不降级"：`!alertOnly` 才覆盖 `mAlertOnly` ✓ |
| 已在仅警觉中，玩家触发完整调查异常源 | 明确"仅警觉可以升级为完整调查"：新事件 `alertOnly=false` 时覆盖旧的 `mAlertOnly=true` ✓ |
| QFramework `RegisterEvent` API 版本差异 | 实现前先 grep 项目已有用法确认；`UnRegisterWhenGameObjectDestroyed` 若不存在则退化为 `OnDestroy` 手动 `UnRegisterEvent` |
| 敌人在 Disable / Dead / Stunned 时也会收到 EnemyAnomalyEvent 广播 | `OnHearAnomaly` 入口已过滤：`IsImmovable || IsDead` 或 `IsInBattle` 直接 return，广播成本可忽略 |
| Chase → Searching → Inspect 中若中途 target 复活 / 重新出现视野 | `OnVisionEnter` 覆盖：`IsImmovable=false && StateName != "Chase"` 的状态都能升级 Chase，路径可自然回到追击 |
| `AutoByNextMove` 在 `mPatrolPoints.Count <= 1` 时未定义 | 明确退化为 KeepCurrent（代码 return） |
| Chase 状态直接切 Idle（防御路径，如 target 直接 null） | 保留 Chase→Idle 分支；Idle 侧 `mIsReturningToPatrol` 为 false 时也不会走"回巡逻"逻辑，通过 `mArrivedFromPatrol` 判定不应用朝向；下轮 `SetNextPatrolTarget` 会正常继续 |

回退方案：本期改动集中在 EnemyBase.cs + SceneObjBase.cs 两个已有文件 + 四个新文件（`EnemyAnomalyEvent.cs` / `IBattleState.cs` / `PatrolPointConfig.cs` / `BrokenGlass.cs`）；如果发现严重回归，`git revert` 相关 commit 即可回到 v0.22.0 状态。

## 6. 测试建议

- **自测**（本期功能需要在 Unity 中运行，无法脱离 Unity 客户端；无 Python 侧改动，无需联调 Agent）：
  1. 场景 A：一个 EnemyBase + 两个巡逻点 + 一个 BrokenGlass。让 PlayerBase 踩碎玻璃，观察敌人 Alerted → Investigate → Inspect → Idle → 回最远巡逻点。（通过）
  2. 场景 B：EnemyBase 处于 Chase 状态，玩家躲进柜子（Hidden 状态 → IsUndetectable=true）。观察敌人切 Searching → 走到玩家最后位置 → Inspect → 回最远巡逻点。（通过）
  3. 场景 C：EnemyBase 在 Chase 中玩家离开视野。观察 Searching → Inspect → 回最远巡逻点。（通过）
  4. 场景 D：两个巡逻点，其中一个挂 `PatrolPointConfig.Facing=Left`。观察敌人到达该点 Idle 时立即面向左；从该点移动到下一个点时面朝转向。（通过）
  5. 场景 E：`PatrolPointConfig.Facing=AutoByNextMove`；单点场景与多点场景各测一次。
  6. 场景 F：碎玻璃冷却期内玩家来回穿越 Trigger，敌人只被吸引一次。（通过）
  7. 场景 G：EnemyBase 处于 `Alerted` / `Investigate` / `Inspect` 时，第二块 BrokenGlass 被触发（新位置）。观察敌人打断当前流程、面朝新源、走向新源、张望新源。（通过）
  8. 场景 H：EnemyBase 处于 `Chase` 或 `Searching` 时，任意 BrokenGlass 被触发。观察敌人**忽略**异常事件、维持战斗节奏（继续追或搜索）。（通过）
  9. 场景 I（自触发忽略）：EnemyBase A 自己踩到 BrokenGlass。观察 A **不进入** Alerted，维持当前巡逻或状态。（通过）
  10. 场景 J（异敌仅警觉）：两个 EnemyBase A/B 都在 BrokenGlass 半径内；A 踩到 BrokenGlass。观察 A 不响应（自触发忽略）、B 进入 Alerted 后回原 Idle/Move 上一状态而**不**进入 Investigate/Inspect。（通过）
  11. 场景 K（升级路径）：B 处于"异敌仅警觉"Alerted 中；此时 Player 踩到另一块 BrokenGlass。观察 B 升级为完整调查，最终去最远巡逻点而不是回 A 触发时记录的 `mPreAlertState`。（通过）
  12. 场景 L（不降级路径）：B 处于"Player 完整调查"Alerted / Investigate / Inspect 中；此时 A 踩到 BrokenGlass。观察 B 依然按完整调查 Investigate → Inspect → 最远巡逻点，**且 mAnomalySource 保持为 Player 触发时的位置**（Alerted 面朝原方向，然后回原调查目标）。（通过）
  13. 场景 M（同源冷却 · 一群敌人排队）：三个 EnemyBase 排成一列途经同一 BrokenGlass。第一个 A 踩到玻璃触发广播 → A/B/C 都在半径内。观察：
    - A 自触发忽略。（通过）
    - B/C 各自进入 Alerted（异敌路径，仅警觉），`mCurrentSourceDevice = thisGlass`；此时**不写**冷却。（通过）
    - Alerted 结束回 `mPreAlertState`（Idle/Move）时，B/C 才把 `thisGlass` 写入自己的 `mSourceCooldowns`（cooldown 从此刻起 5s）。
    - BrokenGlass 冷却过后（3s），B 走到 BrokenGlass 上再次触发广播；A/B/C 都在半径内，此时 B/C 对该 SourceDevice 处于冷却期（假设 5s 未过），因此**全部忽略**这次事件（B 也自触发忽略）。（通过）
    - 冷却过后（5s 之后）再触发才会正常吸引。
  14. 场景 N（同源冷却 · 玩家完整调查后重踩）：Player 踩碎玻璃触发一次，EnemyBase A 进入 Alerted → Investigate → Inspect（十几秒）→ 结束写冷却（此刻起 5s）。Inspect 结束后 3s BrokenGlass 冷却过、Player 再踩一次；观察 A 在 5s 冷却期内**不会**被同一块玻璃二次警觉；再过 2s 后 Player 又踩一次，A 正常响应（因为冷却已过）。若使用旧方案"Alerted 时立即写冷却"，则 Investigate+Inspect 十几秒早已把 5s 冷却耗完，无法起到"刚检查完不应立刻再被同源吸引"的效果。
  15. 场景 O（当前源不打断 · 途中反复触发）：EnemyBase A 正在从 Idle 走向 BrokenGlass X（`Investigate` 状态）。此时另一玩家或另一敌人再次踩到 X（触发新广播，同一 SourceDevice）。观察 A：
    - **不**重进 Alerted（`mCurrentSourceDevice == X` 直接忽略）。
    - **不**重置 `mStateTimer`。
    - 继续走向 X → Inspect → 结束时写冷却。
    - 反之若途中有另一块 BrokenGlass Y 被玩家踩到（异源+完整调查），A 应放弃 X（旧当前源 X 写冷却）→ 转向 Y → Investigate Y → Inspect Y。
  16. 场景 P（当前源不打断 · Alerted 期间同源再触发）：EnemyBase A 处于 Alerted（`mCurrentSourceDevice = X`），Alerted 计时已跑了 0.5s。此时 X 再次被踩。观察 A：Alerted 计时**继续跑**（不重置），到 `mAlertedSeconds` 后按 `mAlertOnly` 正常分流。
- **不涉及**：Python、协议、记忆、Agent 工具。
- **建议**：因涉及 Unity FSM 与物理触发，不建议加 unit test；以场景手测为主。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-07-02 | 完成初始实现：新增 EnemyAnomalyEvent / IBattleState / PatrolPointConfig / BrokenGlass 四个文件；改造 EnemyBase（4 状态 + 异常事件订阅 + 巡逻朝向 + Chase 追丢改造）。 |
| 2026-07-03 | 联调修复（1）：IsInBattle 从 SceneObjBase 移到 EnemyBase；BrokenGlass 加 `other.isTrigger` 过滤修复子 Trigger 误触发；Alerted 回 Idle 时复用 `mArrivedFromPatrol` 恢复站岗朝向。 |
| 2026-07-03 | 联调修复（2）：`Start` 加 `mPatrolPoints.RemoveAll(p => p == null)` 清洗 null 占位，修复单点站岗被误判为多点导致的 Idle->Move->Idle 瞬切循环。 |
| 2026-07-03 | 同源冷却范围修正：`mSameSourceCooldown` 改为仅对异敌触发链写冷却、仅对异敌触发事件检查冷却（详见 `solution_fix_cooldown_scope.md`）。 |
| 2026-07-10 | 用户验收通过。大部分测试场景已标注通过（见 §6）。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
