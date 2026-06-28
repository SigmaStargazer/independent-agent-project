# PRD — v0.21.7 Idle Wakeup 首次唤醒优化 + 新装置与新角色

> **状态**：已确认
> **对应需求**：`requirements/Idle Wakeup优化与新装置.md`
> **关联功能设计**：`DevDocs/feature-design/IdleWakeup.md`（需求一）
> **最后更新**：2026-06-24

---

## 1. 背景与目标

v0.21.3 实现了 Idle Wakeup 空闲唤醒机制，但当前配置的唤醒时间（120~300 秒）在所有空闲周期中都相同。在实际体验中，Agent 回应一条消息后，首次空闲等待较长（2~5 分钟），显得反应迟钝。需要优化为：首次唤醒使用更短的时间，后续若仍无外界信息则使用正常长间隔。

同时，需要在 Unity 侧新增：
- **LaserGridAuto**：不依赖 `ITriggerable` 的定时切换激光网装置，丰富场景机制；
- **EnemyBase**：可巡逻、追击玩家、攻击玩家的敌对角色，增加游戏挑战性；
- **PlayerBase 隐藏状态 + 柜子装置**：玩家可通过躲进柜子进入隐藏状态，规避敌人追击；
- **IUndetectableState / IImmovableState 接口**：统一标记哪些 FSMState 不会触发敌人追击 / 不可主动移动。

---

## 2. 范围

### 2.1 本期包含

- **需求一**：Idle Wakeup 首次唤醒时间可配置并缩短（30 秒），仅当无外界新消息时才回到正常间隔。
- **需求二**：新增 `LaserGridAuto`（DeviceBase），不继承 `ITriggerable`，通过 FSM 定时切换激活/关闭。
- **需求三**：新增 `EnemyBase`（CharaBase），含巡逻（路径点）、视野追人、返回最近路径点、攻击判定框、**背刺永久击晕交互框**。
- **需求三**：PlayerBase 增加 `Hidden` 状态（不可移动但仍可交互/选择/输入）。
- **需求三**：新增柜子 Device，玩家通过交互躲进柜子进入隐藏状态。
- **需求三**：设计 `IUndetectableState` / `IImmovableState` 接口，分别标记不会触发敌人追击 / 不可主动移动的 FSMState。
- **需求三**：EnemyBase 新增 `Stunned`（永久击晕）状态。

### 2.2 本期不包含

- 不改造 `_asend_message` 或 Agent 消息队列的语义。
- 不新增 Python 侧新工具或新协议。
- 不为 EnemyBase 做 AI 行为树或复杂状态机（仅使用现有的 FSM + 碰撞器）。
- 不做敌人的 spawn / despawn 管理。
- 不做多人或网络同步。
- 不改动现有 IdleWakeup.md 功能设计原则。
- **不改动 `SceneObjInfoMapper / SceneObjInfoRenderer`，本期 EnemyBase 不向 AI Player 渲染视野/攻击/背刺三个范围框的方位信息**（仅渲染本体方位，详见第 7 节「待确认问题」），相关抽象推迟到后续版本。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| Agent（AI） | 刚回复完一条消息，首次空闲 | 约 30 秒后即收到空闲感知，而非等待 2~5 分钟 |
| Agent（AI） | 空闲感知唤醒后仍无新消息，再次空闲 | 使用正常 120~300 秒间隔，避免频繁消耗 |
| 开发者 | 配置首次唤醒时间 | 在 `idle_wakeup.json` 新增字段，可调整 |
| 关卡设计 | 需要定时切换的激光网 | 拖入 `LaserGridAuto`，配置激活/关闭时长即可 |
| 玩家 | 在场景中被敌人追赶 | 找到柜子并交互，进入隐藏状态，敌人不再追击 |
| 玩家 | 躲入柜子后想出来 | 再次交互柜子，退出隐藏状态 |
| 玩家 | 接近敌人但被追击 | 绕到敌人身后，进入背刺交互框，对其交互 → 敌人被永久击晕 |
| 敌人（EnemyBase） | 在路径点巡逻 | 沿路径点 Idle→Move→Idle 循环 |
| 敌人（EnemyBase） | 视野框检测到玩家 | 进入追人状态，向玩家移动 |
| 敌人（EnemyBase） | 玩家死亡或躲入柜子 | 停止追击，返回最近路径点继续巡逻 |
| 敌人（EnemyBase） | 被背刺击晕 | 进入 `Stunned`，停止一切行为，**永久保持击晕状态**（直到关卡重置） |

---

## 4. 功能需求

### 4.1 Idle Wakeup 首次唤醒配置

**FR-1.1 首次唤醒时间可配置**

- 在 `idle_wakeup.json` 新增 `first_delay_min_seconds` 和 `first_delay_max_seconds`，默认 25~35 秒（中心 30 秒左右）。
- 保留现有 `delay_min_seconds` / `delay_max_seconds`（120~300 秒）作为非首次唤醒间隔。

**FR-1.2 判断逻辑**

- `_asend_message()` 被调用时（即外部消息/反馈入队），标记「需要首次短间隔」。
- 下次 `_schedule_idle_wakeup()` 时，若标记存在，使用首次短间隔，并清除标记。
- 首次空闲感知触发后，下一轮调度使用正常长间隔。
- 若在首次短间隔期间有新的外部消息到达，重新从首次短间隔开始。

**FR-1.3 astart 调度**

- `astart()` 启动时调度的首个 idle wakeup 仍使用正常长间隔（Agent 刚启动、尚未处理任何消息时不需要短唤醒）。

### 4.2 LaserGridAuto

**FR-2.1 不继承 ITriggerable**

- 与 LaserGrid 功能相似（激活时显示激光、接触死亡），但不可被 Lever 等触发。

**FR-2.2 FSM 定时切换**

- 在 Active 与 Inactive 状态之间按配置时间自动切换。
- 在 `OnActiveUpdate` / `OnInactiveUpdate` 中累计时间，到达阈值后切换。
- 不协程。

**FR-2.3 配置项**

- `mActiveDuration`：激活时长（秒）
- `mInactiveDuration`：关闭时长（秒）

### 4.3 EnemyBase

**FR-3.1 巡逻行为**

- 持有路径点列表（`List<Transform>`），在路径点间 Idle→Move→Idle 循环。
- 不使用名称为 "Patrol" 的状态，保持 Idle/Move 切换（便于 StateChange 观察机制发现运动规律）。
- 巡逻速度可配置。

**FR-3.2 视野追人**

- 持有 Trigger Collider 作为视野框（矩形）。
- 当视野框内出现非 `Dead` / 非 `Hidden` 状态的 `PlayerBase` 时，进入 Chase（追人）状态。
- 追击速度可配置。
- Chase 注册为 FSMState，在 `OnChaseFixedUpdate` 中向 Player 移动。

**FR-3.3 脱战返回**

- 当 `PlayerBase` 脱离视野框（`OnTriggerExit2D`）时：
  - 先进入 Idle
  - 然后向最近的路径点移动（`ChangeState("Move")` + 设置目标为最近路径点）
  - 到达后恢复正常巡逻。

**FR-3.4 攻击判定**

- 持有另一个 Trigger Collider 作为攻击判定框。
- 当攻击框撞击到 PlayerBase 时，调用 `player.Die()`。

**FR-3.5 背刺交互框**

- 持有第三个 Trigger Collider 作为「背刺交互区域」，挂在 EnemyBase **身后**（基于 `IsRight` 朝向动态布置：朝右时背后即左侧，朝左时背后即右侧）。
- 实现方式：将背刺交互框挂载在子物体上，通过 InteractionZone 机制配合 `mInteractionZones` 注册到 SceneObjBase 的交互区域系统，或在 EnemyBase 自行实现 `Interact` 中判断玩家是否在背刺区。
- 玩家在背刺交互框内对 EnemyBase 进行交互（`Interact`）时：
  - EnemyBase 进入 `Stunned`（永久击晕）状态
  - 击晕期间停止巡逻、停止追人、停止攻击判定
  - **不自动恢复**——`Stunned` 是终态，直到关卡重置/角色销毁
- 玩家不在背刺交互框内时，对 EnemyBase 交互返回失败提示（如「无法从正面攻击」）。
- 击晕状态实现 `IUndetectableState` + `IImmovableState`，避免击晕中切换面朝方向后又被其他敌人/自身追人逻辑触发，也屏蔽位移类工具。

**FR-3.6 不触发追击的状态**

- 不影响 `Dead` 状态（已通过 `CharaBase.ChangeState` 保护）和 `Hidden` 状态。
- EnemyBase 自身处于 `Stunned` 状态时不应追人。

### 4.4 PlayerBase 隐藏状态

**FR-4.1 新增 Hidden 状态**

- 在 `PlayerBase` 中注册 `HiddenState`，实现 `IUndetectableState` + `IImmovableState`。
- 进入 Hidden 状态时：
  - **禁止移动**（通过统一 `IsImmovable` 判定屏蔽：HumanPlayer 输入读取、AIPlayer Move 工具与 `ExecuteMoveAction`、`ExecuteFollowAction` 等位移相关动作）
  - 速度归零
  - 仍**允许执行**：`DoInteract` / `DoSelect` / `DoTextInput`，以及 ActionSequence 的 `InteractAction` / `SelectAction` / `InputAction`
- 子类（HumanPlayer / AIPlayer）通过 Hook 自行决定视觉表现（如隐藏渲染、关闭碰撞器等），本期不要求强制视觉变化。

**FR-4.2 从 Hidden 退出**

- 当玩家再次与柜子交互时退出 Hidden 状态，回到 Idle。

**FR-4.3 移动屏蔽实现要求**

统一原则：**所有「主动位移」入口都用 `IsImmovable` 守卫**，不再硬编码状态名。Dead 状态同样实现 `IImmovableState`，因此 Dead 时也会被同一套守卫拦下移动。

- `HumanPlayer.GetInput()`：在 `IsImmovable` 时跳过 Horizontal → `ChangeState("Move"/"Idle")` 的调用；Dead 时连 Interact 也屏蔽；Hidden 等其他不可移动状态仍允许 Interact。
- `AIPlayer.Move(...)` / `ExecuteMoveAction(...)` / `ExecuteFollowAction(...)`：在 `IsImmovable` 时直接返回失败结果（`success=false`，提示按状态差异化：「死亡」「躲藏中」等），并通过 `SendToolResultMessage` 反馈给 Python。
- `AIPlayer.DoInteract / DoSelect / DoTextInput / ExecuteInteractAction / ExecuteSelectAction / ExecuteInputAction`：**不阻塞**，照常执行；动作完成时的 `ChangeState("Idle")` 改为 `if (!IsImmovable) ChangeState("Idle");` 以保留 Hidden / Dead 状态。Dead 时建议另加 `if (IsDead) return failure;` 显式拦截。

> 这套守卫天然覆盖未来新增的不可移动状态（如「定身」「眩晕」等），新状态只需让对应 FSMState 实现 `IImmovableState`。

### 4.5 柜子 Device

**FR-5.1 极简交互流程（无状态）**

- 柜子（Cabinet）继承 `DeviceBase`，`IsInteractable = true`。
- **柜子本身没有任何 FSM 状态**（不注册自定义状态），所有切换由玩家状态驱动。
- 持有两个 `Transform` 子物体作为锚点：
  - `mEnterAnchor`：玩家进入柜子时被瞬移到此位置
  - `mExitAnchor`：玩家离开柜子时被瞬移到此位置
- 交互逻辑（基于交互玩家当前状态）：
  - 玩家当前不在 `Hidden` → 将玩家瞬移到 `mEnterAnchor.position` → `player.ChangeState("Hidden")`
  - 玩家当前在 `Hidden` → 将玩家瞬移到 `mExitAnchor.position` → `player.ChangeState("Idle")`

**FR-5.2 不做的事**

- 不做 Closed/Opened/HiddenInside 等柜子自身状态机
- 不做开/关柜门动画
- 不做柜子内只能容纳一名玩家的限制（关卡设计上避免）

### 4.6 状态标记接口

**FR-6.1 接口定义**

- `IUndetectableState`：空标记接口，实现该接口的 `FSMStateBase` 不会触发 EnemyBase 的追人。
- `IImmovableState`：空标记接口，实现该接口的 `FSMStateBase` 表示「该状态下角色不可主动移动」。
- 两个接口命名都加 `State` 后缀，表明这是 **FSMState 的标记接口**（不是角色/对象本身的接口）。

**FR-6.2 适用状态**

| 状态 | IUndetectableState | IImmovableState | 备注 |
|------|--------------------|-----------------|------|
| `CharaBase.DeadState` | ✓ | ✓ | 死亡：不可被检测，不可移动 |
| `PlayerBase.HiddenState` | ✓ | ✓ | 躲藏：不可被检测，不可移动 |
| `EnemyBase.StunnedState` | ✓ | ✓ | 击晕：不可被检测，不可移动 |

后续新增不可检测 / 不可移动状态时，只需让对应 FSMState 实现相应接口。

**FR-6.3 判断方式**

`SceneObjBase` 上提供统一虚属性：

```csharp
public virtual bool IsUndetectable => mCurState is IUndetectableState;
public virtual bool IsImmovable    => mCurState is IImmovableState;
```

业务代码（EnemyBase 视野检测、HumanPlayer/AIPlayer 移动入口）只读这两个属性，不再枚举状态名。

---

## 5. 非功能需求

- **不新增协议**：需求一仅改 Python 配置与逻辑，需求二/三全为 Unity 侧，无需新增 protobuf。
- **向后兼容**：旧配置文件缺少首次唤醒字段时，自动使用默认值。
- **UTF-8**：所有文件 UTF-8 编码。
- **低侵入**：EnemyBase 不修改 `SceneObjBase` / `CharaBase` 原有逻辑，仅扩展。

---

## 6. 验收标准

- [ ] `idle_wakeup.json` 新增 `first_delay_min_seconds` / `first_delay_max_seconds`，默认 25/35。
- [ ] Agent 处理完外部消息后首次 idle wakeup 使用配置的短间隔。
- [ ] 首次完成后后续无新消息的 idle 使用正常长间隔。
- [ ] `astart()` 启动时首次 idle 使用正常长间隔。
- [ ] 缺省配置自动使用默认值。
- [ ] `LaserGridAuto` 类名为 `LaserGridAuto`，装置 `Name` 为「自动开关的激光网」。
- [ ] `LaserGridAuto` 在场景中可配置激活/关闭时长，定时切换，不依赖 `ITriggerable`。
- [ ] `LaserGridAuto` 激光激活时可触发 PlayerBase.Die()，关闭时无害。
- [ ] `EnemyBase` 沿路径点 Idle→Move→Idle 巡逻。
- [ ] `EnemyBase` 视野子物体（普通 GameObject + Trigger Collider2D + `EnemyZoneForwarder(Vision)`）检测到 PlayerBase 后进入 Chase 追人；**视野/攻击子物体不挂 `InteractionZone`**，仅背刺子物体挂 `InteractionZone(ZoneTag="back")`；代码不依赖子物体 GameObject 名。
- [ ] `EnemyBase` 视野丢失 PlayerBase 后 Idle → 返回最近路径点 → 继续巡逻。
- [ ] `EnemyBase` 攻击子物体命中 PlayerBase 后 PlayerBase.Die()。
- [ ] `EnemyBase` 背后有背刺交互子物体（InteractionZone, ZoneTag="back"），玩家在其内交互可将 EnemyBase 切换到 `Stunned` 状态。
- [ ] `Stunned` 期间 EnemyBase 不巡逻、不追人、不攻击；**不会自动恢复**。
- [ ] **`Dead` / `Stunned` 状态下 `EnemyBase.IsInteractable == false`**，无法被再次交互（上层由 `SceneObjManager.Interact` 拦截）。
- [ ] 玩家不在背刺交互框内对 EnemyBase 交互返回失败提示。
- [ ] PlayerBase 新增 Hidden 状态（实现 `IUndetectableState` + `IImmovableState`）；Dead/Hidden 状态下 EnemyBase 不追击。
- [ ] HumanPlayer / AIPlayer 的 `IsImmovable` 守卫生效：Hidden / Dead 状态下 HumanPlayer 无法通过输入移动，AIPlayer 的 Move/Follow 工具调用返回失败。
- [ ] Hidden 状态下 HumanPlayer 的 `DoInteract` 与 AIPlayer 的 `DoInteract`/`DoSelect`/`DoTextInput`、`InteractAction`/`SelectAction`/`InputAction` 仍可正常执行；交互完成不会强行切回 Idle（Hidden 保留）。
- [ ] 柜子 Cabinet 无自身 FSM 状态；交互时根据玩家当前是否 Hidden 切换并瞬移到 `mEnterAnchor` / `mExitAnchor`。
- [ ] `IUndetectableState` / `IImmovableState` 接口被 `DeadState`、`HiddenState`、`StunnedState` 实现；`SceneObjBase.IsUndetectable / IsImmovable` 用 `mCurState is XxxState` 判定。

---

## 7. 待确认问题

- [x] LaserGridAuto 类名 = `LaserGridAuto`，装置 Name = 「自动开关的激光网」。
- [x] 柜子躲藏不需要动画过渡。
- [x] EnemyBase 视野框使用矩形（BoxCollider2D）。
- [x] EnemyBase 背刺：玩家在身后交互框内交互可击晕。
- [x] 击晕为永久状态，不自动恢复。
- [x] EnemyBase 的视野/攻击/背刺三个判断框各挂在子 GameObject 上（不与 EnemyBase 自身物理 Collider2D 冲突）。
- [x] `IsUndetectable` 判定统一为 `mCurState is IUndetectableState`（接口名加 `State` 后缀）。
- [x] Hidden 状态：不可移动但可交互/选择/输入。
- [x] Cabinet 无状态，仅两个进入/离开锚点 + 切换玩家状态。
- [x] **本期不修改 SceneObjInfo 抽象**：EnemyBase 的视野/攻击/背刺范围**不**渲染到 AI Player 的 SceneObjInfo（本体仍走单点方位）；该抽象推迟到后续版本评估。
- [x] **`Dead` / `Stunned` 通过 `IsInteractable => false` 屏蔽交互**，不在 `Interact` 函数里枚举状态名。
- [x] **新增 `IImmovableState` 标记接口**：`Dead` / `Hidden` / `Stunned` 均实现此接口；统一通过 `SceneObjBase.IsImmovable` 在 HumanPlayer/AIPlayer 移动入口拦截。
- [x] EnemyBase 子物体 Trigger 事件通过 `EnemyZoneForwarder` 显式分发，**不**用 `other.name` 判别子物体。
- [x] `ainterrupt` 流程不重置 `_pending_first_wakeup` flag（仅取消已调度的 wakeup 任务）。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*