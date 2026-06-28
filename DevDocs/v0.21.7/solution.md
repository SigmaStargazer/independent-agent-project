# 技术方案 — v0.21.7 Idle Wakeup 首次唤醒优化 + 新装置与新角色

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-06-28
> **后续修复**：
> - `DevDocs/v0.21.7_fix_1/solution_actionsequence_immovable_fix.md`（ActionSequence × IImmovableState 守卫漏洞，已实现）
> - `DevDocs/v0.21.7_fix_2/solution_enemybase_issues_fix.md`（EnemyBase 巡逻跳/退追间隔/状态判定/Zone 自动收集说明，已实现并验收通过）

---

## 1. 方案概述

需求一（Idle Wakeup 首次唤醒优化）仅涉及 Python `agent_interuptible.py` 中 `_schedule_idle_wakeup` 逻辑与配置文件 `idle_wakeup.json` 的变更。需求二、三全为 Unity C# 侧新增/修改文件，不涉及协议修改。

**本期不动 SceneObjInfo 抽象**：`SceneObjInfoModel / Mapper / Renderer` 维持原状，EnemyBase 仅以「本体单点方位」渲染给 AI Player，视野/攻击/背刺三个判定框只用于 Unity 内部碰撞与背刺交互判定。多范围抽象推迟到后续版本。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python 配置 | `config/idle_wakeup.json` | 新增字段 |
| Python | `agent_framwork/agents/agent_interuptible.py` — `_schedule_idle_wakeup`, `_asend_message`, `_cancel_idle_wakeup` | 修改 |
| Unity | `LaserGridAuto.cs` — 新文件 | 新增 |
| Unity | `EnemyBase.cs` — 新文件（含 `EnemyZoneForwarder` 子物体转发器） | 新增 |
| Unity | `IUndetectableState.cs` — FSMState 标记接口（不可检测） | 新增 |
| Unity | `IImmovableState.cs` — FSMState 标记接口（不可移动） | 新增 |
| Unity | `Cabinet.cs` — 新文件（无 FSM 状态） | 新增 |
| Unity | `SceneObjBase.cs` — 新增 `IsUndetectable` / `IsImmovable` 虚属性 | 修改 |
| Unity | `CharaBase.cs` — `DeadState` 实现 `IUndetectableState` + `IImmovableState` | 修改 |
| Unity | `PlayerBase.cs` — 新增 `HiddenState`（实现两接口）与 Hidden Hook | 修改 |
| Unity | `HumanPlayer.cs` — Hidden/Dead 下用 `IsImmovable` 屏蔽移动输入 | 修改 |
| Unity | `AIPlayer.cs` — `IsImmovable` 下 Move/Follow 入口返回失败；交互入口 `ChangeState("Idle")` 改为非 `IsImmovable` 才切 | 修改 |
| Unity | `SceneObjInfo*`（Model/Mapper/Renderer） | **不修改**（本期不渲染 EnemyBase 三个判定框） |
| 协议 | `Tools/message.proto` | 无（本期纯 Python/Unity 侧逻辑） |

## 3. 详细设计

### 3.1 Idle Wakeup 首次唤醒（需求一）

#### 3.1.1 配置文件

`config/idle_wakeup.json` 改为：

```json
{
  "enabled": true,
  "first_delay_min_seconds": 25,
  "first_delay_max_seconds": 35,
  "delay_min_seconds": 120,
  "delay_max_seconds": 300,
  "summary_max_events": 3,
  "summary_timeout_seconds": 5,
  "ignore_self_events": true,
  "message_template": "你已经空闲了一段时间，可以稍微留意一下周围。\n{world_event_summary}\n如果没有值得行动或交流的事情，可以继续保持当前状态。"
}
```

#### 3.1.2 load_idle_wakeup_config

在 `agent_interuptible.py` 顶部的加载函数中新增对 `first_delay_min_seconds` / `first_delay_max_seconds` 的解析：

- 默认值：min=25，max=35。
- 兼容旧版：若字段不存在，使用默认值。
- 校验：`first_delay_min > 0`、`first_delay_max >= first_delay_min`，否则用默认。

#### 3.1.3 Agent 类新增状态字段

```python
self._pending_first_wakeup = False  # 标记：下次调度应使用首次短间隔
```

#### 3.1.4 调度逻辑修改

- **`_asend_message`**：在最前面 `self._cancel_idle_wakeup()` 后，立刻设置 `self._pending_first_wakeup = True`。表示「刚收到外界消息」。
- **`_schedule_idle_wakeup`**：根据 `_pending_first_wakeup` 选择区间：

```python
def _schedule_idle_wakeup(self):
    self._cancel_idle_wakeup()
    if not self._can_schedule_idle_wakeup():
        return
    if self._pending_first_wakeup:
        min_delay = IDLE_WAKEUP_CONFIG["first_delay_min_seconds"]
        max_delay = IDLE_WAKEUP_CONFIG["first_delay_max_seconds"]
        self._pending_first_wakeup = False
    else:
        min_delay = IDLE_WAKEUP_CONFIG["min_delay_seconds"]
        max_delay = IDLE_WAKEUP_CONFIG["max_delay_seconds"]
    delay = random.uniform(min_delay, max_delay)
    seq = self._idle_wakeup_seq
    self._idle_wakeup_task = asyncio.create_task(self._idle_wakeup_after_delay(seq, delay))
    print(f"[{self.name}] idle wakeup scheduled in {delay:.1f}s")
```

- **`_enqueue_idle_wakeup_message`**：成功注入空闲感知消息后，**不**设置 `_pending_first_wakeup = True`（确保下一次 idle 走长间隔，避免自激）。
- **`astart()`**：保持 `_pending_first_wakeup = False`（启动时首次走长间隔，避免刚启动 30 秒就被唤醒）。

#### 3.1.5 边界场景验证

| 场景 | 期望行为 | 实现说明 |
|------|----------|----------|
| 1. Agent 首次 `astart()` | 长间隔 (120~300s) | `_pending_first_wakeup=False` 初始；astart 调度走 else 分支 |
| 2. 用户发送一条消息，Agent 回复完 | 短间隔 (25~35s) | `_asend_message` 已置位 flag；本轮 graph 结束 finally 中 `_schedule_idle_wakeup` 走 if 分支 |
| 3. 收到 feedback 也算外界信息 | 短间隔 | `_asend_message` 是 `asend_message` / `asend_feedback` 的共用底层，都会置位 |
| 4. 短间隔到期未触发（被新消息打断） | 重新短间隔 | 新消息再次调用 `_asend_message`，flag 再次为 True |
| 5. 短间隔触发后无新外界消息 | 下次长间隔 | flag 在 `_schedule_idle_wakeup` 时被清空，且 `_enqueue_idle_wakeup_message` 不重新置位 |
| 6. Agent 收到空闲感知后自行调用工具继续多轮思考 | 工具结束后 graph 结束，走长间隔 | flag 已清空；finally 调度长间隔 |
| 7. `ainterrupt` 流程 | **不重置 flag**，保留之前的标记 | `ainterrupt` 内部仅做取消调度（`_cancel_idle_wakeup()`），不读不写 `_pending_first_wakeup`；下次 `astart` 后由 `aprocess_message` 跑完一轮在 finally 中调度时按现 flag 决定短/长间隔 |

**确认结论**：`ainterrupt` 流程**保留 flag 状态不变**。即在中断逻辑中**只取消已调度的 idle wakeup 任务**，不要清空 `_pending_first_wakeup`。具体保证点：

- `ainterrupt()` 中调用 `_cancel_idle_wakeup()` 取消 task 与递增 `_idle_wakeup_seq`，但**不**写 `_pending_first_wakeup`。
- 中断恢复后由 `aprocess_message` 跑完一轮在 finally 中重新 `_schedule_idle_wakeup()`，此时按现 flag 决定区间。
- 这保证了「用户消息打断 Agent 思考」这种典型链路依旧走短间隔：`_asend_message` 置 flag → `ainterrupt` 不动 flag → 重新跑完一轮后调度 → 走短间隔。

### 3.2 LaserGridAuto（需求二）

新文件：`Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Device/LaserGridAuto.cs`

类名 `LaserGridAuto`，装置 `Name` = 「自动开关的激光网」。

```csharp
using UnityEngine;

namespace IndependentAgentProject
{
    public class LaserGridAuto : DeviceBase
    {
        public override string Name => "自动开关的激光网";
        public override string Desc => "似乎会按某种节律开关。";
        public override bool IsInteractable => false;

        [Header("激光配置")]
        [SerializeField] private GameObject mLaser;
        [SerializeField] [Tooltip("游戏开始时是否激活")] private bool mStartActive = true;
        [SerializeField] private float mActiveDuration = 5f;
        [SerializeField] private float mInactiveDuration = 5f;

        private float mTimer = 0f;
        public bool IsActive { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            RegisterState(new ActiveState());
            RegisterState(new InactiveState());
            if (mLaser != null) mLaser.SetActive(mStartActive);
        }

        protected override void Start()
        {
            base.Start();
            ChangeState(mStartActive ? "Active" : "Inactive");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying && mLaser != null)
                mLaser.SetActive(mStartActive);
        }
#endif

        #region FSM Hook
        public void OnActiveEnter()
        {
            IsActive = true;
            mTimer = 0f;
            if (mLaser != null) mLaser.SetActive(true);
        }
        public void OnActiveUpdate()
        {
            mTimer += Time.deltaTime;
            if (mTimer >= mActiveDuration)
                ChangeState("Inactive");
        }
        public void OnInactiveEnter()
        {
            IsActive = false;
            mTimer = 0f;
            if (mLaser != null) mLaser.SetActive(false);
        }
        public void OnInactiveUpdate()
        {
            mTimer += Time.deltaTime;
            if (mTimer >= mInactiveDuration)
                ChangeState("Active");
        }
        #endregion

        #region FSM State
        public class ActiveState : FSMStateBase
        {
            public override string Name => "Active";
            public override void OnEnter(SceneObjBase o) { if (o is LaserGridAuto l) l.OnActiveEnter(); }
            public override void OnUpdate(SceneObjBase o) { if (o is LaserGridAuto l) l.OnActiveUpdate(); }
        }
        public class InactiveState : FSMStateBase
        {
            public override string Name => "Inactive";
            public override void OnEnter(SceneObjBase o) { if (o is LaserGridAuto l) l.OnInactiveEnter(); }
            public override void OnUpdate(SceneObjBase o) { if (o is LaserGridAuto l) l.OnInactiveUpdate(); }
        }
        #endregion
    }
}
```

说明：

- 不继承 `ITriggerable`，无 `Trigger()` 方法。
- 激光本体复用现有 `Laser.cs`（其 `OnTriggerEnter2D` 在 Active 状态下生效；Inactive 时 `mLaser.SetActive(false)` 整个游戏物体被禁用，碰撞器也随之失效，玩家通过时不会触发死亡）。
- FSM 计时使用 `OnXxxUpdate` 累计 `Time.deltaTime`，与 `MovingPlatformAuto.OnIdleUpdate` 同风格。

### 3.3 EnemyBase（需求三）

#### 3.3.1 继承关系

`EnemyBase` 继承 `CharaBase`（而非 `PlayerBase`），因此自动获得 Idle/Move/Dead/Follow 四个基础状态。但 `EnemyBase` **不使用 Follow 状态**，而是新增 Chase 状态用于追击。

新文件：`Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/EnemyBase.cs`

#### 3.3.2 状态与行为

| FSM 状态 | 来源 | 行为 |
|----------|------|------|
| Idle | CharaBase | 在路径点等待，计时到后切 Move 前往下一路径点 |
| Move | CharaBase | 向 `mTargetPoint` 移动，到达后切 Idle |
| Dead | CharaBase | 不追击（DeadState 实现 IUndetectableState） |
| Chase | 本文件新增 | 向视野框内的 PlayerBase 移动 |
| Stunned | 本文件新增（IUndetectableState + IImmovableState） | 被背刺后**永久击晕**：停止一切行为，不自动恢复 |
| Follow | CharaBase（继承但不使用） | 不注册 |

**巡逻逻辑**（参考 `MovingPlatformAuto`）：

- `mPatrolPoints`: `List<Transform>` 路径点列表
- `mPatrolSpeed`: 巡逻速度
- `mWaitTime`: 在路径点等待时间
- `mCurrentPatrolIndex`: 当前路径点索引
- `mWaitTimer`: Idle 状态等待计时

巡逻流程：
```
Start → Idle(计算最近路径点，开始等待)
  → (OnIdleUpdate 计满 mWaitTime) → Move(向下一路径点移动)
  → (OnMoveFixedUpdate 到达) → Idle(等待)
  → ...
```

Chase 追人逻辑：

- 视野框：`mVisionRange` (Collider2D, IsTrigger)，只在 `Chase` 状态外做 `OnTriggerEnter2D` 检测。
- 追击速度：`mChaseSpeed`
- 追人移动：`OnChaseFixedUpdate` 向 PlayerBase 方向 MoveTowards

攻击判定：

- 攻击框：`mAttackRange` (Collider2D, IsTrigger)
- `OnTriggerEnter2D` 检测到 PlayerBase → `player.Die()`

脱战流程：

```
OnTriggerExit2D(mVisionRange) 且 state != Chase → 忽略（视野框的碰撞器原先可能在 Chase 状态下启用）
OnTriggerExit2D(mVisionRange) 且 state == Chase → 进入 OnLostTarget():
  → Idle → 找到最近路径点 → ChangeState("Move") 设置目标 → 到达后继续正常巡逻
```

#### 3.3.3 玩家不可检测状态保护

在 `SceneObjBase` 上新增统一判定（详见 §3.4）：

```csharp
public virtual bool IsUndetectable => mCurState is IUndetectableState;
```

EnemyBase 视野/攻击检测时只需读 `player.IsUndetectable`，不再单独枚举状态名（`Dead`、`Hidden`、`Stunned` 通过实现 `IUndetectableState` 自动生效）。

```csharp
// EnemyBase 视野检测分支
if (player != null && !player.IsDead && !player.IsUndetectable)
{
    mChaseTarget = player;
    ChangeState("Chase");
}
```

> 不再在子类显式 `override IsUndetectable`：所有不可检测语义统一通过「FSMState 实现 IUndetectableState」表达。

### 3.4 状态接口：IUndetectableState / IImmovableState（需求三）

为了避免在业务代码里枚举状态名做判定，把「该状态是否具备某种语义」放在 **FSMState 的标记接口**上。

#### 3.4.1 IUndetectableState

新文件：`Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/Core/IUndetectableState.cs`

```csharp
namespace IndependentAgentProject
{
    /// <summary>
    /// 实现此接口的 FSMState 不会被 EnemyBase 等敌对单位检测/追击。
    /// 命名加 State 后缀，表明这是 FSMStateBase 的标记接口（而非角色/对象本身的接口）。
    /// </summary>
    public interface IUndetectableState { }
}
```

**SceneObjBase 新增统一判定**（建议放在 `SceneObjBase.cs`，所有继承者通用）：

```csharp
public virtual bool IsUndetectable => mCurState is IUndetectableState;
```

**实现 `IUndetectableState` 的状态**：

- `CharaBase.DeadState`（在 `CharaBase.cs` 内追加接口）
- `PlayerBase.HiddenState`（新增）
- `EnemyBase.StunnedState`（新增）

**禁止**在 `PlayerBase` 中 `override IsUndetectable` 用状态名做判定——统一用接口语义。

#### 3.4.2 IImmovableState

新文件：`Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/Core/IImmovableState.cs`

```csharp
namespace IndependentAgentProject
{
    /// <summary>
    /// 实现此接口的 FSMState 表示「该状态下角色不可主动移动」。
    /// 用于在 HumanPlayer 输入读取、AIPlayer 的 Move/Follow 工具与 ActionSequence
    /// 的 MoveAction/FollowAction 入口统一屏蔽位移行为。
    /// </summary>
    public interface IImmovableState { }
}
```

**SceneObjBase 新增统一判定**：

```csharp
public virtual bool IsImmovable => mCurState is IImmovableState;
```

**实现 `IImmovableState` 的状态**：

| 状态 | 所在文件 | 备注 |
|------|---------|------|
| `CharaBase.DeadState` | `CharaBase.cs` | 死亡当然不能移动 |
| `PlayerBase.HiddenState` | `PlayerBase.cs` | 躲藏中不可移动 |
| `EnemyBase.StunnedState` | `EnemyBase.cs` | 被击晕不可移动 |

> 对 `Dead`/`Hidden`/`Stunned` 三个状态，`IUndetectableState` 与 `IImmovableState` **同时实现**（它们既不可被检测，也不可移动）。

#### 3.4.3 设计取舍

为什么用「FSMState 实现标记接口」而不是「在 CharaBase 上加 `IsImmovable` 虚属性然后子类覆写状态名判断」：

- **不可移动是状态的属性**：同一个 CharaBase 在不同状态下是否可移动不同。把判定挂到状态上，新增不可移动状态时只需让该 State 实现接口，业务代码（HumanPlayer/AIPlayer 入口）零修改。
- **与 `IUndetectableState` 形成对称**：同一套「FSMState 标记接口 + SceneObjBase 虚属性」模式，降低心智负担。
- **避免字符串枚举**：不再写 `if (StateName == "Hidden" || StateName == "Dead" || StateName == "Stunned")`，对 typo 友好。

### 3.5 PlayerBase Hidden 状态（需求三）

`PlayerBase.cs` 中新增：

```csharp
protected override void Awake()
{
    base.Awake();
    RegisterState(new HiddenState());
    // 其余原有逻辑保持
}

#region Hidden Hook
public virtual void OnHiddenEnter()
{
    if (mRigidbody2D != null)
        mRigidbody2D.velocity = Vector2.zero;
    // 子类可覆写：隐藏渲染、关闭碰撞等
}
public virtual void OnHiddenUpdate() { }
public virtual void OnHiddenFixedUpdate() { }
public virtual void OnHiddenExit() { }
#endregion

public class HiddenState : FSMStateBase, IUndetectableState, IImmovableState
{
    public override string Name => "Hidden";
    public override void OnEnter(SceneObjBase o) { if (o is PlayerBase p) p.OnHiddenEnter(); }
    public override void OnUpdate(SceneObjBase o) { if (o is PlayerBase p) p.OnHiddenUpdate(); }
    public override void OnFixedUpdate(SceneObjBase o) { if (o is PlayerBase p) p.OnHiddenFixedUpdate(); }
    public override void OnExit(SceneObjBase o) { if (o is PlayerBase p) p.OnHiddenExit(); }
}
```

进入/退出逻辑由柜子 `Interact` 调用 `player.ChangeState("Hidden"/"Idle")` 驱动。

#### 3.5.1 移动屏蔽

统一原则：**所有「主动位移」入口都用 `IsImmovable` 守卫**，不再写状态名硬编码。

**HumanPlayer.GetInput()** 改为：

```csharp
private void GetInput()
{
    if (mMode == PlayerMode.Chatting) return;

    // 不可移动状态（Hidden / Dead 等）：禁止移动输入，但仍允许交互
    if (IsImmovable)
    {
        if (!IsDead && Input.GetButtonDown("Interact"))
            DoInteract();
        return;
    }

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
    if (Input.GetButtonDown("Interact"))
        DoInteract();
}
```

> 这里同时覆盖了 Dead 场景：Dead 也是 `IImmovableState`，原先 `PlayerBase` 死亡后已通过 `CharaBase.ChangeState` 阻止状态变更，但输入读取并未屏蔽；现在用 `IsImmovable` 一并拒绝移动指令，更稳。

**AIPlayer 移动相关入口**在执行前增加 `IsImmovable` 守卫，统一返回失败：

```csharp
// 公共守卫
private bool RejectIfImmovable(string toolName, string requestId)
{
    if (!IsImmovable) return false;
    string reason = IsDead ? "你已经死了，无法移动。"
                   : StateName == "Hidden" ? "你正躲在柜子里，无法移动。"
                   : "当前状态无法移动。";
    AgentService.Instance.SendToolResultMessage(Name, toolName, requestId, false, reason);
    return true;
}
```

调用点（伪代码）：

- `Move(...)` 工具入口：`if (RejectIfImmovable("Move", reqId)) return;`
- `ExecuteMoveAction(...)`：检测到 `IsImmovable` 时直接将 `mCurActionRuntime.Result` 设为失败并 `OnActionFinished`。
- `ExecuteFollowAction(...)` 同上（Follow 也属于位移）。

> 这套守卫天然覆盖未来新增的不可移动状态（如「定身」「眩晕」等），新状态只需实现 `IImmovableState`。

**不阻塞**的 AIPlayer 入口（`IsImmovable` 状态下仍允许执行）：

- `DoInteract(requestId)`、`DoSelect(...)`、`DoTextInput(...)`
- `ExecuteInteractAction(...)`、`ExecuteSelectAction(...)`、`ExecuteInputAction(...)`

> 但 Dead 状态下仍应禁止交互（角色已死）。这些动作内部本身会通过 `CharaBase.ChangeState` 卡 Dead，但建议在 AIPlayer 这一层也做一道 `if (IsDead) return failure;` 防御。

> 上述动作内部仅会 `ChangeState("Idle")` 然后执行 `SceneObjManager.Instance.Interact/Select/TextInput`。在 Hidden 下需要**保留 Hidden 状态**——把这些动作里的 `ChangeState("Idle")` 改为：

```csharp
if (!IsImmovable) ChangeState("Idle");
```

确保交互完成后玩家仍处于 Hidden（继续躲藏）或 Dead（不被错误地切回 Idle，虽 `CharaBase.ChangeState` 已保护，但语义统一更清晰）。柜子的退出交互依然有效（柜子内部走 `player.ChangeState("Idle")` 主动退出，不受 `IsImmovable` 影响——`ChangeState` 不读这个属性）。

### 3.6 柜子 Cabinet Device（需求三）

新文件：`Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Device/Cabinet.cs`

**极简设计**：柜子**无自身 FSM 状态**，仅持两个 `Transform` 锚点 + 改变玩家状态。

```csharp
using UnityEngine;

namespace IndependentAgentProject
{
    public class Cabinet : DeviceBase
    {
        public override string Name => "柜子";
        public override string Desc => "可以躲进去的柜子。";
        public override bool IsInteractable => true;

        [Header("玩家锚点")]
        [SerializeField] [Tooltip("玩家进入柜子时被瞬移到此位置")] private Transform mEnterAnchor;
        [SerializeField] [Tooltip("玩家离开柜子时被瞬移到此位置")] private Transform mExitAnchor;

        public override (bool success, string result) Interact(GameObject chara)
        {
            PlayerBase player = chara.GetComponent<PlayerBase>();
            if (player == null)
                return (false, "只有玩家才能使用柜子。");
            if (player.IsDead)
                return (false, "已经死了。");

            if (player.StateName != "Hidden")
            {
                // 进入柜子
                if (mEnterAnchor == null)
                    return (false, "柜子的进入位置未配置。");
                player.transform.position = mEnterAnchor.position;
                if (player.TryGetComponent<Rigidbody2D>(out var rb))
                {
                    rb.velocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                }
                player.ChangeState("Hidden");
                return (true, "你躲进了柜子里。");
            }
            else
            {
                // 离开柜子
                if (mExitAnchor == null)
                    return (false, "柜子的离开位置未配置。");
                player.transform.position = mExitAnchor.position;
                if (player.TryGetComponent<Rigidbody2D>(out var rb))
                {
                    rb.velocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                }
                player.ChangeState("Idle");
                return (true, "你从柜子里出来了。");
            }
        }
    }
}
```

说明：

- 柜子**不注册任何自定义 FSM 状态**，沿用 `SceneObjBase` 默认 Idle。
- 进入/离开锚点是独立子物体（如 `EnterPoint`、`ExitPoint`），便于关卡设计师摆放精确坐标（例如 `ExitAnchor` 略偏向柜子前方一格，避免重叠回流再进入）。
- 退出柜子时，调用 `player.ChangeState("Idle")` 让玩家状态自动回到正常。`PlayerBase.HiddenState.OnExit` 调用 `OnHiddenExit` Hook，子类可恢复视觉。
- **无开关门动画 / 状态切换**。

### 3.7 EnemyBase 完整实现

```csharp
public class EnemyBase : CharaBase
{
    [Header("巡逻配置")]
    [SerializeField] private List<Transform> mPatrolPoints = new();
    [SerializeField] private float mPatrolSpeed = 2f;
    [SerializeField] private float mWaitTime = 1f;

    [Header("追人配置")]
    [SerializeField] private float mChaseSpeed = 4f;

    [Header("感知子物体")]
    [SerializeField] [Tooltip("视野范围子物体：普通 GameObject，挂 Trigger Collider2D")] private GameObject mVisionZone;
    [SerializeField] [Tooltip("攻击范围子物体：普通 GameObject，挂 Trigger Collider2D")] private GameObject mAttackZone;
    [SerializeField] [Tooltip("背刺交互子物体：GameObject，挂 Trigger Collider2D + InteractionZone(ZoneTag=\"back\")")] private InteractionZone mBackstabZone;

    private int mCurrentPatrolIndex = 0;
    private float mWaitTimer = 0f;
    private Transform mTargetPoint;
    private bool mIsReturningToPatrol = false;
    private PlayerBase mChaseTarget = null;

    /// <summary>Dead / Stunned 状态下不可被交互（不接受背刺）。</summary>
    public override bool IsInteractable
        => !(mCurState is StunnedState) && !IsDead;

    protected override void Awake()
    {
        base.Awake();
        RegisterState(new ChaseState());
        RegisterState(new StunnedState());
        mStates.Remove("Follow");

        // 仅背刺区是 InteractionZone（供 GetActiveZoneTag 识别 "back"），注册到 SceneObjBase.mInteractionZones
        if (mBackstabZone != null && !mInteractionZones.Contains(mBackstabZone))
            mInteractionZones.Add(mBackstabZone);

        // 视野/攻击是普通子 GameObject + Trigger Collider2D：通过 Forwarder 把 Trigger 事件转发到父级
        if (mVisionZone != null)
            mVisionZone.AddComponent<EnemyZoneForwarder>().Init(this, EnemyZoneKind.Vision);
        if (mAttackZone != null)
            mAttackZone.AddComponent<EnemyZoneForwarder>().Init(this, EnemyZoneKind.Attack);
        // 背刺区无需 Trigger 事件，仅在 Interact 时通过 GetActiveZoneTag(chara) 检查
    }

    protected override void Start()
    {
        base.Start();
        if (mPatrolPoints.Count <= 1) return;
        mCurrentPatrolIndex = 0;
        SetNextPatrolTarget();
    }

    #region Idle — 巡逻等待
    public override void OnIdleUpdate()
    {
        if (mPatrolPoints.Count <= 1) return;
        mWaitTimer += Time.deltaTime;
        if (mWaitTimer >= mWaitTime)
        {
            mWaitTimer = 0;
            SetNextPatrolTarget();
        }
    }
    private void SetNextPatrolTarget()
    {
        if (mPatrolPoints.Count <= 1) return;
        mCurrentPatrolIndex = (mCurrentPatrolIndex + 1) % mPatrolPoints.Count;
        mTargetPoint = mPatrolPoints[mCurrentPatrolIndex];
        TurnBack((mTargetPoint.position - transform.position).x);
        ChangeState("Move");
    }
    #endregion

    #region Move — 巡逻或返回路径点
    public override void OnMoveEnter()
    {
        if (mTargetPoint != null)
            TurnBack((mTargetPoint.position - transform.position).x);
    }
    public override void OnMoveFixedUpdate()
    {
        if (mTargetPoint == null) { ChangeState("Idle"); return; }
        transform.position = Vector3.MoveTowards(
            transform.position, mTargetPoint.position, mPatrolSpeed * Time.fixedDeltaTime);
        if (Vector3.Distance(transform.position, mTargetPoint.position) < 0.02f)
        {
            transform.position = mTargetPoint.position;
            mIsReturningToPatrol = false;
            mTargetPoint = null;
            ChangeState("Idle");
        }
    }
    #endregion

    #region Chase — 追人
    public void OnChaseEnter() { mRigidbody2D.velocity = Vector2.zero; }
    public void OnChaseFixedUpdate()
    {
        if (mChaseTarget == null || mChaseTarget.IsDead || mChaseTarget.IsUndetectable)
        { mChaseTarget = null; ChangeState("Idle"); MoveToNearestPatrolPoint(); return; }
        Vector3 dir = (mChaseTarget.transform.position - transform.position).normalized;
        TurnBack(dir.x);
        transform.position = Vector3.MoveTowards(
            transform.position, mChaseTarget.transform.position, mChaseSpeed * Time.fixedDeltaTime);
    }
    public void OnChaseExit() { mRigidbody2D.velocity = Vector2.zero; }
    #endregion

    #region Stunned — 被背刺永久击晕
    public void OnStunnedEnter()
    {
        mRigidbody2D.velocity = Vector2.zero;
        mChaseTarget = null;
        mTargetPoint = null;
        // 不启动计时器：Stunned 是终态，直到关卡重置/角色销毁
    }
    public void OnStunnedUpdate() { /* 永久击晕，无需更新 */ }
    #endregion

    #region 来自子物体的 Trigger 事件回调（由 EnemyZoneForwarder 调用）
    public void OnVisionEnter(Collider2D other)
    {
        if (StateName == "Stunned" || StateName == "Dead" || StateName == "Chase") return;
        PlayerBase player = other.GetComponentInParent<PlayerBase>();
        if (player != null && !player.IsDead && !player.IsUndetectable)
        {
            mChaseTarget = player;
            ChangeState("Chase");
        }
    }
    public void OnVisionExit(Collider2D other)
    {
        if (StateName != "Chase") return;
        PlayerBase player = other.GetComponentInParent<PlayerBase>();
        if (player == null || player != mChaseTarget) return;
        mChaseTarget = null;
        ChangeState("Idle");
        MoveToNearestPatrolPoint();
    }
    public void OnAttackEnter(Collider2D other)
    {
        if (StateName != "Chase") return;
        PlayerBase player = other.GetComponentInParent<PlayerBase>();
        if (player != null && !player.IsDead) player.Die();
    }
    #endregion

    #region 背刺交互
    public override (bool success, string result) Interact(GameObject chara)
    {
        // Dead / Stunned 已通过 IsInteractable 在 SceneObjManager.Interact 上层被拦截
        // 这里只判断玩家是否从背后交互
        string zone = GetActiveZoneTag(chara);
        if (zone == "back")
        {
            ChangeState("Stunned");
            return (true, "你成功背刺了敌人！");
        }
        return (false, "无法交互。");
    }
    #endregion

    private void MoveToNearestPatrolPoint()
    {
        if (mPatrolPoints.Count == 0) return;
        mIsReturningToPatrol = true;
        Transform nearest = null;
        float minDist = float.MaxValue;
        foreach (var pt in mPatrolPoints)
        {
            if (pt == null) continue;
            float d = Vector3.Distance(transform.position, pt.position);
            if (d < minDist) { minDist = d; nearest = pt; }
        }
        mTargetPoint = nearest;
        if (mTargetPoint != null)
            TurnBack((mTargetPoint.position - transform.position).x);
        ChangeState("Move");
    }

    public class ChaseState : FSMStateBase
    {
        public override string Name => "Chase";
        public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnChaseEnter(); }
        public override void OnFixedUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnChaseFixedUpdate(); }
        public override void OnExit(SceneObjBase o) { if (o is EnemyBase e) e.OnChaseExit(); }
    }
    public class StunnedState : FSMStateBase, IUndetectableState, IImmovableState
    {
        public override string Name => "Stunned";
        public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnStunnedEnter(); }
        public override void OnUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnStunnedUpdate(); }
    }
}

/// <summary>感知子物体 Trigger 转发器：挂在 VisionRange / AttackRange 子物体上，
/// 把子物体上的 Trigger 事件按 kind 转发给父 EnemyBase。避免父 EnemyBase 用 other.name 判别。</summary>
public enum EnemyZoneKind { Vision, Attack }
public class EnemyZoneForwarder : MonoBehaviour
{
    private EnemyBase mOwner;
    private EnemyZoneKind mKind;
    public void Init(EnemyBase owner, EnemyZoneKind kind) { mOwner = owner; mKind = kind; }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (mOwner == null) return;
        switch (mKind)
        {
            case EnemyZoneKind.Vision: mOwner.OnVisionEnter(other); break;
            case EnemyZoneKind.Attack: mOwner.OnAttackEnter(other); break;
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (mOwner == null) return;
        if (mKind == EnemyZoneKind.Vision) mOwner.OnVisionExit(other);
    }
}
```

**关键设计要点**：

1. **不用 `other.name` 字符串判断子物体**：感知子物体的语义由 Inspector 拖入字段（`mVisionZone` / `mAttackZone` / `mBackstabZone`）显式声明；运行时通过 `EnemyZoneForwarder + EnemyZoneKind` 显式分发回调。子物体改名不会破坏逻辑。
2. **`IsInteractable` 动态屏蔽**：Dead / Stunned 时 `IsInteractable => false`，从源头拒绝交互（由 `SceneObjManager.Interact` 上层检查 `IsInteractable`）。`Interact` 函数只需处理「是否从背后交互」业务。
3. **背刺区不挂 Trigger Forwarder**：背刺是请求-应答式的，在 `Interact` 中由 `GetActiveZoneTag(chara) == "back"` 判定即可。
4. **`StunnedState` 同时实现 `IUndetectableState` + `IImmovableState`**。

**碰撞器架构**：EnemyBase **自身**保留原有物理 Collider2D（非 Trigger，用于普通碰撞）。视野/攻击/背刺三个判定框各自挂在**独立子物体**上，由父 EnemyBase 在 Inspector 中显式拖入对应字段（**不依赖子物体的 GameObject 名识别**）：

- `mVisionZone` 子物体（**`GameObject` 字段**）— 普通 GameObject，**只挂 BoxCollider2D（IsTrigger=true）**，视野矩形；运行时父 EnemyBase 给它挂 `EnemyZoneForwarder(Vision)` 转发 Trigger 事件。**不挂 `InteractionZone`**（视野不属于"玩家可交互"语义）。
- `mAttackZone` 子物体（**`GameObject` 字段**）— 普通 GameObject，**只挂 Trigger Collider2D**，攻击判定；运行时挂 `EnemyZoneForwarder(Attack)` 转发。**不挂 `InteractionZone`**。
- `mBackstabZone` 子物体（**`InteractionZone` 字段**）— 是 `InteractionZone` 体系内的子物体：BoxCollider2D + `InteractionZone(ZoneTag = "back")`，被注册到 `mInteractionZones`，供 `GetActiveZoneTag(chara)` 在 `Interact` 时识别；**不挂 Forwarder**。

> EnemyBase 自身的物理 Collider2D 与三个 Trigger 子物体互不干扰（Trigger 和 Non-Trigger 不会相互触发 OnTriggerEnter2D）。
> 子物体名可任意，代码不依赖。
> `InteractionZone` 是 SceneObjBase 体系里专门给「玩家交互判定」用的组件（参与 `GetActiveZoneTag` / `ContainsCharacter` 逻辑）。视野/攻击是"敌方主动感知"，与玩家交互语义无关，因此**不**用 `InteractionZone`，纯靠子 GameObject + Trigger Collider2D + Forwarder 实现。

**朝向跟随**：EnemyBase 通过 `TurnBack()` 翻转 `transform.localScale.x`，三个子物体的碰撞器都自动跟随镜像。在 Prefab 中按「面朝右」的初始朝向布置即可（视野/攻击在前，背刺区在后）。

**Hook 与 Layer 注意**：

- `mBackstabZone` 上的 `InteractionZone.TargetLayers` 需勾选 Player 所在 Layer，才能在 `ContainsCharacter` 中识别玩家。
- 视野/攻击 Trigger 子物体的 Layer 配置需保证 PlayerBase 的 Collider 能与之触发 Trigger 事件。
- 子物体 Trigger 事件由 `EnemyZoneForwarder` 显式分发，不再用 `other.name` 字符串判别。

## 4. 实现步骤

### 步骤 1：Idle Wakeup 首次唤醒优化（Python 侧）

1. 修改 `config/idle_wakeup.json`，新增 `first_delay_min_seconds` / `first_delay_max_seconds`。
2. 修改 `load_idle_wakeup_config()`，解析新字段并做默认值保护。
3. 在 `Agent.__init__` 中新增 `self._pending_first_wakeup = False`。
4. 修改 `_schedule_idle_wakeup`：根据 `_pending_first_wakeup` 选择区间，之后清空 flag。
5. 修改 `_asend_message`：在 `_cancel_idle_wakeup()` 后置 `_pending_first_wakeup = True`。
6. 验证 `astart()` 不置位 flag（首次启动走长间隔）。
7. 自测：模拟消息发送 → 走完图 → 验证调度延迟 ∈ [25,35]。

### 步骤 2：LaserGridAuto（Unity 侧）

1. 新建 `LaserGridAuto.cs`，按方案代码实现。
2. 在 Unity Editor 中创建 Prefab 或复用现有 LaserGrid 的 GameObject 结构，挂载 `LaserGridAuto` 脚本。
3. 测试：激活时激光触发 PlayerBase.Die()，关闭时安全通过。

### 步骤 3：IUndetectableState / IImmovableState 接口 + SceneObjBase 虚属性 + PlayerBase Hidden 状态

1. 新建 `IUndetectableState.cs`、`IImmovableState.cs`（均为空标记接口，命名带 `State` 后缀表明这是 FSMState 接口）。
2. 修改 `SceneObjBase.cs`：新增 `public virtual bool IsUndetectable => mCurState is IUndetectableState;` 与 `public virtual bool IsImmovable => mCurState is IImmovableState;`。
3. 修改 `CharaBase.cs`：`DeadState : FSMStateBase, IUndetectableState, IImmovableState`。
4. 修改 `PlayerBase.cs`：
   - 新增 `HiddenState : FSMStateBase, IUndetectableState, IImmovableState`。
   - 新增 `OnHiddenEnter/Update/FixedUpdate/Exit` Hook（`OnHiddenEnter` 中速度归零）。
   - 在 `Awake` 中 `RegisterState(new HiddenState())`。
5. **不要**在子类 `override IsUndetectable / IsImmovable`：统一使用 `SceneObjBase` 的实现。

### 步骤 4：HumanPlayer / AIPlayer 移动屏蔽

1. `HumanPlayer.GetInput()`：用 `if (IsImmovable) { ... return; }` 守卫；Dead 时也屏蔽 Interact，其他不可移动状态（如 Hidden）下仍允许 `DoInteract`。
2. `AIPlayer`：在所有移动相关入口（Move 工具、`ExecuteMoveAction`、`ExecuteFollowAction`）中调用 `RejectIfImmovable(...)`；若 `IsImmovable` 直接通过 `SendToolResultMessage`/`OnActionFinished` 返回失败结果（提示按状态差异化）。
3. `AIPlayer`：把 `DoInteract` / `DoSelect` / `DoTextInput` / `ExecuteInteractAction` / `ExecuteSelectAction` / `ExecuteInputAction` 里的 `ChangeState("Idle")` 改为 `if (!IsImmovable) ChangeState("Idle");`，保留 Hidden / Dead 等不可移动状态。建议在 AIPlayer 这一层对 `IsDead` 单独做一道 `if (IsDead) return failure;` 防御。
4. 自测：
   - Human 进柜子（Hidden）后无法移动，按 Interact 可正常出柜子；
   - Human 死亡（Dead）后无法移动、无法 Interact；
   - AI 进柜子后 Move 工具返回失败，InteractAction 仍能正常执行；
   - AI 死亡后 Move / Interact 工具均返回失败。

### 步骤 5：EnemyBase（Unity 侧）

1. 新建 `EnemyBase.cs` 与 `EnemyZoneForwarder` 子物体转发器。
2. 实现巡逻逻辑（`OnIdleUpdate`、`OnMoveFixedUpdate`、`SetNextPatrolTarget`）。
3. 实现 `ChaseState` + `OnChaseFixedUpdate`。
4. 实现 `StunnedState : FSMStateBase, IUndetectableState, IImmovableState` + `OnStunnedEnter`（**永久击晕，不启用计时器**）。
5. 实现 `OnVisionEnter/Exit`、`OnAttackEnter` 回调；在 `Awake` 中给 Vision/Attack 子物体挂 `EnemyZoneForwarder`，**不用 `other.name` 判别**子物体。
6. 实现脱战返回最近路径点逻辑（`MoveToNearestPatrolPoint`）。
7. 重写 `IsInteractable`：`Dead` / `Stunned` 时返回 `false`，由上层 `SceneObjManager.Interact` 拦截。
8. 实现背刺交互：`Interact(GameObject chara)` 中只根据 `GetActiveZoneTag(chara) == "back"` 判断（不再判 Dead/Stunned 状态名，已由 `IsInteractable` 兜底）。
9. 在 `Awake` 中 `mStates.Remove("Follow")`。
10. 在 Unity Prefab 中：
    - EnemyBase 自身保留原有物理 Collider2D。
    - 视野子物体：普通 GameObject + Trigger Collider2D（运行时挂 `EnemyZoneForwarder`），拖入 `mVisionZone`（`GameObject` 字段）。
    - 攻击子物体：普通 GameObject + Trigger Collider2D，拖入 `mAttackZone`（`GameObject` 字段）。
    - 背刺子物体：GameObject + Trigger Collider2D + **`InteractionZone(ZoneTag="back")`**，拖入 `mBackstabZone`（`InteractionZone` 字段）。
    - 路径点 Transform 拖入 `mPatrolPoints`。

### 步骤 6：Cabinet 柜子 Device（Unity 侧）

1. 新建 `Cabinet.cs`（**无自身 FSM 状态**）。
2. 序列化两个 `Transform mEnterAnchor` / `mExitAnchor`。
3. 在 `Interact` 中根据玩家当前是否 `Hidden` 切换玩家状态并瞬移坐标；瞬移后清零 Rigidbody2D 速度。
4. 在 Prefab 中布置柜子主体 + EnterPoint / ExitPoint 两个子 Transform。
5. 自测：玩家走到柜子前 → Interact → 瞬移到 EnterPoint，进入 Hidden；再次 Interact → 瞬移到 ExitPoint，回到 Idle。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `_pending_first_wakeup` flag 在打断/恢复流程中可能残留错误状态 | 在中断流程中明确不清空 flag（见 3.1.5 场景 7 分析） |
| EnemyBase 视野检测与攻击检测的碰撞器容易混淆 | 明确命名 `VisionRange` / `AttackRange`，在 Prefab 上区分颜色 |
| EnemyBase 脱战返回路径点时路径点可能被移动 | 使用 `Transform.position` 运行时读取，不缓存 |
| 旧 `idle_wakeup.json` 缺少新字段加载失败 | `load_idle_wakeup_config` 已做缺省值保护 |

## 6. 测试建议

### 6.0 Python 自测用例矩阵（v0.21.7 仅 Idle Wakeup 模块可在不启动 Unity 的情况下自测）

| # | 测试目标 | 前置 | 输入 / 步骤 | 期望输出 | 覆盖的真实风险 |
|---|---------|------|-------------|---------|----------------|
| T1 | `load_idle_wakeup_config` 默认值生效 | `idle_wakeup.json` 临时改名（或 mock 路径不存在） | 调用 `load_idle_wakeup_config()` | 返回 dict 含 `first_min_delay_seconds==25`、`first_max_delay_seconds==35`，且 `min/max_delay_seconds==120/300` | 防止读到旧配置时新字段缺失导致 KeyError |
| T2 | 真实 `idle_wakeup.json` 字段加载正确 | 仓库当前 `config/idle_wakeup.json` | 调用 `load_idle_wakeup_config()` | `first_min_delay_seconds==25`、`first_max_delay_seconds==35`、`min/max_delay_seconds==120/300` | 防止字段名拼写错误（`first_delay_min_seconds` vs `first_min_delay_seconds`） |
| T3 | `_pending_first_wakeup=True` 时走首次短间隔，并清空 flag | mock `random.uniform` 和 `asyncio.create_task` | 构造 FakeAgent，置 `_pending_first_wakeup=True`，调用 `_schedule_idle_wakeup()` | `random.uniform` 收到 `(25, 35)` 区间，`_pending_first_wakeup` 变为 False，`_idle_wakeup_task` 被赋值 | 防止首次唤醒退化成长间隔，且防止 flag 不清空导致永远短间隔 |
| T4 | `_pending_first_wakeup=False` 时走普通长间隔 | 同上 | 置 `_pending_first_wakeup=False`，调用 `_schedule_idle_wakeup()` | `random.uniform` 收到 `(120, 300)` 区间，`_pending_first_wakeup` 仍为 False | 防止普通空闲走错短间隔 |
| T5 | `_asend_message` 路径会置 flag | mock `_cancel_idle_wakeup`、`_run_inference_step`、队列、`_invoke_task` 等 | `_pending_first_wakeup=False` 起步，调用 `_asend_message("...")` | 在调用进入后 `_pending_first_wakeup==True`；`_cancel_idle_wakeup` 被调用 1 次 | 防止「外界消息到达后未标记首次短间隔」 |
| T6 | `ainterrupt` **不**清空 flag（关键纪律点） | T5 完成后，flag=True | 调用 `ainterrupt("reason")`（mock 内部 checkpoint / state 操作） | `_pending_first_wakeup` 仍为 True | 防止 ainterrupt 流程意外把 flag 清掉导致首次短唤醒失效（PRD §3.1.4 明确要求） |
| T7 | `afinish` 必须清空 flag | flag=True | 调用 `afinish()`（mock 队列 reset、checkpoint reset） | `_pending_first_wakeup==False` | 防止 agent 完全关停后残留 flag 影响下次启动 |
| T8 | `_cancel_idle_wakeup` 自身不动 flag | flag=True，`_idle_wakeup_task` mock 一个 task | 调用 `_cancel_idle_wakeup()` | flag 仍为 True；`_idle_wakeup_seq` +1；task 被 cancel | 防止把「单纯取消调度」误处理成「重置 flag」 |

**自测脚本位置**：`Src/PythonServer/test_v021_7_idle_wakeup.py`（一次性自测脚本，跑通后保留作为回归依据；可在版本完成后删除）。

**自测输出粘贴位置**：本节末尾的「自测执行记录」。

### 6.1 Idle Wakeup 首次唤醒（Unity / 真实联调验证项）

- [ ] 配置正确加载：新字段存在于 JSON 中，读取后值正确。
- [ ] 默认值：JSON 不包含新字段时，使用默认 25/35。
- [ ] 消息触发短间隔：Agent 收到消息 → 回复完 → 30 秒左右收到空闲感知。
- [ ] 无消息时走长间隔：空闲感知触发后无新消息，下次走 120~300s。
- [ ] 连续消息：连续两条消息，每次回复后都走短间隔。
- [ ] astart 首次：Agent 刚启动，未收到消息，idle 走长间隔。

### 6.2 LaserGridAuto

- [ ] 配置激活/关闭时长，在 Scene 中观察自动切换。
- [ ] 激活时玩家接触激光死亡。
- [ ] 关闭时玩家安全通过。

### 6.3 EnemyBase

- [ ] 巡逻：沿路径点 Idle→Move→Idle 循环。
- [ ] 追人：视野内出现玩家，进入 Chase 状态向玩家移动。
- [ ] 脱战：玩家脱离视野，Idle → 返回最近路径点 → 继续巡逻。
- [ ] 攻击：攻击子物体命中玩家，玩家死亡。
- [ ] 不可检测：玩家 Dead 或 Hidden 时不触发追人（`player.IsUndetectable == true`）。
- [ ] 背刺：玩家在背刺交互子物体内交互 → EnemyBase Stunned。
- [ ] **Stunned 永久保持**：进入 Stunned 后不再恢复（停在 Stunned，不巡逻、不追人、不攻击）。
- [ ] `Dead` / `Stunned` 状态下 `IsInteractable == false`，再次交互被上层拦截。
- [ ] 玩家不在背刺框内交互 EnemyBase 返回失败提示「无法从正面攻击」。
- [ ] 三个 Trigger Collider 各挂子物体；**代码不依赖子物体 GameObject 名**（用 `EnemyZoneForwarder` 显式分发）。
- [ ] EnemyBase 自身物理 Collider 不被自身 Trigger 触发。

### 6.4 Cabinet + Hidden + Immovable

- [ ] 玩家与柜子交互一次 → 瞬移到 EnterAnchor → 进入 Hidden（IUndetectableState + IImmovableState）。
- [ ] 再次交互 → 瞬移到 ExitAnchor → 回到 Idle。
- [ ] 隐藏状态下 EnemyBase 不追击。
- [ ] 隐藏 / 死亡状态下：
  - HumanPlayer 无法通过 Horizontal 输入移动；
  - Hidden 下按 Interact 可正常退出柜子；Dead 下 Interact 也被屏蔽。
  - AIPlayer Move 工具调用返回失败结果（按状态差异化提示）；
  - AIPlayer 的 InteractAction / SelectAction / InputAction 仍可执行（动作完成不会强行 `ChangeState("Idle")` 切出 Hidden / Dead）。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-24 | 完成 v0.21.7 全部 6 个步骤开发：①Idle Wakeup 首次唤醒优化（`idle_wakeup.json` 新增 `first_delay_*`，`agent_interuptible.py` 新增 `_pending_first_wakeup` 标志，外部消息/反馈到达时置位，`afinish` 清空）；②`LaserGridAuto.cs` + `.meta`（不继承 ITriggerable，Active/Inactive 计时器自动切换）；③`IUndetectableState` / `IImmovableState` 标记接口 + `SceneObjBase` 新增 `IsUndetectable` / `IsImmovable` 虚属性，`DeadState`、`PlayerBase.HiddenState`、`EnemyBase.StunnedState` 实现两接口；④`HumanPlayer.GetInput` 在 `IsImmovable` 时屏蔽移动但允许 Interact，`AIPlayer` 新增 `RejectIfImmovable` / `RejectIfDead` 工具/Action 失败保护，`StopMovement` 与 ActionSequence 完成后不强切 Idle；⑤`EnemyBase.cs` + `EnemyZoneForwarder.cs` + 各 `.meta`（巡逻、视野追击、攻击致死、背刺→Stunned 永久态、Stunned/Dead 时 `IsInteractable=false`）；⑥`Cabinet.cs` + `.meta`（无自身 FSM，靠两个 Transform 锚点 + `ChangeState("Hidden"/"Idle")` 完成进出柜子，Dead 时禁止使用）。状态从「已确认」→「已实现」。 |
| 2026-06-24 | **补做自测纪律**：用户指出本版本仅记了「已通过 Python 侧自测」字样，未将测试用例矩阵写入方案、自测脚本未保留，违反 v0.21.4 事故记录确立的纪律。补救动作：在 §6.0 写入 8 条用例矩阵（T1~T8），重新创建 `Src/PythonServer/test_v021_7_idle_wakeup.py` 并实跑通过（见 §7.1 自测执行记录）。Unity 侧 5 个步骤无法在不启动 Unity 的条件下自测，仍依赖 §6.1~§6.4 联调验收。 |

### 7.1 自测执行记录（仅 Python 侧 Idle Wakeup 可在不启动 Unity 的条件下自测）

**脚本**：`Src/PythonServer/test_v021_7_idle_wakeup.py`
**命令**：`uv run python test_v021_7_idle_wakeup.py`
**执行时间**：2026-06-24
**结果**：8/8 PASS

```
test_T7_afinish_clears_pending_first_wakeup (AfinishClearsFlagTest) ... ok
test_T6_ainterrupt_does_not_clear_flag (AinterruptKeepsFlagTest) ... ok
test_T5_asend_message_sets_pending_first_wakeup (AsendMessageSetsFlagTest) ... ok
test_T1_missing_file_falls_back_to_defaults (IdleWakeupConfigLoaderTest) ... ok
test_T2_real_config_file_has_first_delay_fields (IdleWakeupConfigLoaderTest) ... ok
test_T3_first_wakeup_uses_short_range_and_clears_flag (IdleWakeupScheduleTest) ... ok
test_T4_normal_wakeup_uses_long_range_and_keeps_flag_false (IdleWakeupScheduleTest) ... ok
test_T8_cancel_only_does_not_change_flag (IdleWakeupScheduleTest) ... ok
----------------------------------------------------------------------
Ran 8 tests in 4.275s
OK
```

辅证日志（来自被测代码的 print，验证延迟落在预期区间）：

```
[T] idle wakeup scheduled in 30.0s (first)    # T3：随机 mock 取中点 → (25+35)/2=30
[T] idle wakeup scheduled in 210.0s (normal)  # T4：随机 mock 取中点 → (120+300)/2=210
```

**未通过自测的步骤**（必须 Unity 联调验收，对应 §6.2~§6.4）：

| 步骤 | 原因 |
|------|------|
| ②LaserGridAuto | 依赖 Unity Update / 物理 Trigger，无法离线复现 |
| ③`IUndetectableState` / `IImmovableState` + Hidden 状态 | 仅是 C# 接口/标记，行为依赖 Unity FSM 运行时 |
| ④HumanPlayer / AIPlayer 移动屏蔽 | 依赖 Unity Input / 物理 Rigidbody / AgentService RPC |
| ⑤EnemyBase | 依赖 Unity 物理 Trigger / Update / 物体引用 |
| ⑥Cabinet | 依赖 Unity Transform 瞬移 / PlayerBase FSM 实例 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*