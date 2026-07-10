# EnemyBase 巡逻与异常调查逻辑梳理

> 用途：帮助理解 v0.22.1 后 EnemyBase 的完整状态机、flag 语义与跳转规则。
> 对应代码：`Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/EnemyBase.cs`
> 最后更新：2026-07-10

---

## 一、状态总览

EnemyBase 共 8 个 FSM 状态，分 3 组：

| 组 | 状态 | 实现的标记接口 | 语义 |
|----|------|----------------|------|
| **巡逻组** | `Idle` | - | 巡逻点停顿等待 / 单点站岗 / 异常调查后回归中转 |
| | `Move` | - | 走向下一个巡逻点或回归目标点 |
| **战斗组** | `Chase` | `IBattleState` | 追击玩家 |
| | `Searching` | `IBattleState` | 追丢后走向玩家最后已知位置 |
| **异常调查组** | `Alerted` | - | 面朝异常源短暂警觉 |
| | `Investigate` | - | 走向异常源 |
| | `Inspect` | - | 在异常源处张望一段时间 |
| **失控组** | `Stunned` | `IUndetectableState`, `IImmovableState` | 被背刺永久击晕 |
| | `Dead` | (CharaBase 继承) | 死亡 |

**标记接口的作用**：
- `IBattleState`：战斗中不响应异常事件（`OnHearAnomaly` 直接 return）。
- `IImmovableState`：不可移动，屏蔽玩家输入与异常响应。
- `IUndetectableState`：不可被敌人视野发现。

---

## 二、关键字段速查

### 2.1 巡逻相关

| 字段 | 类型 | 含义 |
|------|------|------|
| `mPatrolPoints` | `List<Transform>` | 巡逻点列表；`Start` 时清洗 null；Count<=1 为单点站岗 |
| `mCurrentPatrolIndex` | int | 当前目标巡逻点索引 |
| `mTargetPoint` | `Transform` | 当前移动目标（巡逻点或回归点） |
| `mIsReturningToPatrol` | bool | 是否在"回归巡逻"路径（Chase/Inspect 结束后回最远点） |
| `mArrivedFromPatrol` | bool | 本次 Idle 是否由"巡逻抵达"触发（用于应用朝向配置） |
| `mWaitTimer` | float | Idle 停顿计时 |

### 2.2 异常调查相关

| 字段 | 类型 | 含义 |
|------|------|------|
| `mAnomalySource` | `Vector2` | 当前调查的异常源坐标 |
| `mCurrentSourceObj` | `SceneObjBase` | 当前调查链对应的声源装置（如 BrokenGlass 实例） |
| `mAlertOnly` | bool | **当前调查链**是否为"仅警觉"模式（异敌触发）。true=Alerted 结束回出发状态；false=走完整调查。同时作为"是否写同源冷却"的判据 |
| `mPreAlertState` | string | 进入 Alerted 前的状态（"Idle" 或 "Move"），仅警觉结束后回哪个 |
| `mSourceCooldowns` | `Dict<SceneObjBase, float>` | 每个声源装置对该敌人的冷却截止时间（仅异敌触发链才写入） |
| `mStateTimer` | float | Alerted / Inspect 的通用计时 |
| `mInspectTurnTimer` | float | Inspect 期间翻朝向计时 |
| `mLostSightPos` | `Vector2` | Chase 追丢时记录的玩家最后位置 |

---

## 三、状态跳转全表

### 3.1 巡逻组内部跳转

| 当前状态 | 触发条件 | 目标状态 | 备注 |
|----------|----------|----------|------|
| `Idle` | `mWaitTime` 到期 + 多点巡逻 | `Move` | `SetNextPatrolTarget` 设下一巡逻点 |
| `Idle` | `mWaitTime` 到期 + 回归路径(`mIsReturningToPatrol`) | `Move` | 沿用已设的 `mTargetPoint` |
| `Idle` | 单点站岗(`Count<=1`) + 非回归 | 不跳转 | Idle 无时限 |
| `Move` | 抵达目标(`\|dx\|<epsilon`) | `Idle` | 设 `mArrivedFromPatrol=true` 触发朝向配置 |
| `Move` | `mTargetPoint==null` | `Idle` | 防御路径 |

### 3.2 视野触发（战斗组入口）

| 当前状态 | 触发条件 | 目标状态 | 备注 |
|----------|----------|----------|------|
| 任意（非 Immovable、非 Chase） | 视野发现玩家 | `Chase` | `OnVisionEnter`；战斗优先 |
| `Chase` | 玩家离开视野 | `Searching` | `OnVisionExit`；记录 `mLostSightPos` |
| `Chase` | 目标 Dead/Undetectable | `Searching` | `OnChaseFixedUpdate`；记录 `mLostSightPos` |
| `Chase` | 目标突然 null | `Idle` | 防御路径 |

### 3.3 战斗组内部跳转

| 当前状态 | 触发条件 | 目标状态 | 备注 |
|----------|----------|----------|------|
| `Searching` | 到达 `mLostSightPos` | `Inspect` | 在最后位置张望 |

### 3.4 异常事件触发（调查组入口）

异常事件经 `OnEnemyAnomalyEventFired` 三层过滤后进入 `OnHearAnomaly`：

**过滤层（按顺序）**：
1. 距离过滤：超出 `evt.Radius` -> 忽略。
2. 同源不打断：`evt.SourceObj == mCurrentSourceObj` -> 忽略（不重置计时）。
3. 同源冷却：仅异敌触发事件检查；玩家/装置触发跳过此检查。

**`OnHearAnomaly` 入口过滤**：
- `triggerer == this` -> 忽略（自触发）。
- `IsImmovable || IsDead` -> 忽略（Stunned/Dead）。
- `IsInBattle` -> 忽略（Chase/Searching）。

**通过过滤后强制重进 Alerted**（`ForceReenterAlerted`）。

### 3.5 调查组内部跳转

| 当前状态 | 触发条件 | 目标状态 | 备注 |
|----------|----------|----------|------|
| `Alerted` | 计时到期 + `mAlertOnly==false` | `Investigate` | 完整调查：走向异常源 |
| `Alerted` | 计时到期 + `mAlertOnly==true` | `mPreAlertState` | 仅警觉：回出发状态；写冷却 |
| `Investigate` | 到达异常源 | `Inspect` | 张望 |
| `Inspect` | 计时到期 | `Idle` | 完整调查结束；写冷却；回最远巡逻点 |

### 3.6 失控组入口

| 当前状态 | 触发条件 | 目标状态 | 备注 |
|----------|----------|----------|------|
| 任意（非 Dead） | 背刺交互 | `Stunned` | `Interact("Back")` |

### 3.7 调查链被抢占

| 当前状态 | 触发条件 | 目标状态 | 冷却处理 |
|----------|----------|----------|----------|
| Alerted/Investigate/Inspect | 视野发现玩家 | `Chase` | `OnChaseEnter` 调 `WriteCooldownAndClearCurrentSource` |
| Alerted/Investigate/Inspect | 被背刺 | `Stunned` | `OnStunnedEnter` 调 `WriteCooldownAndClearCurrentSource` |

---

## 四、`OnHearAnomaly` 三分支决策表

收到通过过滤的异常事件后，按"敌人当前状态"与"事件类型"分三个分支：

| 分支 | 条件 | 场景 | 行为 |
|------|------|------|------|
| **A** | `Idle/Move`（首次进入） | 巡逻中被异常吸引 | 全新赋值：`mAlertOnly=事件类型`、`mAnomalySource=新`、`mCurrentSourceObj=新`、`mPreAlertState=当前` |
| **B** | `!mAlertOnly && eventIsAlertOnly` | 完整调查中被异敌打断 | **保留原目标**：不改任何字段（空分支）；Alerted 面朝原方向后继续 Investigate 原目标 |
| **C** | 其他（已在调查链中且非 B） | 替换当前源 | 旧源按 `mAlertOnly` 决定写不写冷却；替换为新源；`!eventIsAlertOnly` 时 `mAlertOnly=false` |

**分支 C 的三种子情况**：

| 子情况 | 当前链 | 新事件 | `mAlertOnly` 变化 |
|--------|--------|--------|-------------------|
| C-1 | 完整调查(false) | 完整调查(false) | 保持 false |
| C-2 | 仅警觉(true) | 完整调查(false) | true -> false（升级） |
| C-3 | 仅警觉(true) | 仅警觉(true) | 保持 true |

---

## 五、同源冷却规则

### 5.1 何时写冷却

冷却**只在调查链条结束/中断时**写入（不是 Alerted 进入时），且**仅当当前链是异敌触发**（`mAlertOnly==true`）：

| 链条结束场景 | 调用位置 | `mAlertOnly` | 写冷却？ |
|--------------|----------|--------------|----------|
| 仅警觉 Alerted 结束回 `mPreAlertState` | `OnAlertedUpdate` | true | 是 |
| 完整调查 Inspect 结束 | `OnInspectUpdate` | false | 否 |
| 被 Chase 抢占 | `OnChaseEnter` | 看旧链 | 旧链 true 才写 |
| 被 Stunned 抢占 | `OnStunnedEnter` | 看旧链 | 旧链 true 才写 |
| 被新源替换（分支 C） | `OnHearAnomaly` | 看旧链 | 旧链 true 才写 |

### 5.2 何时检查冷却

| 事件触发者 | 检查冷却？ | 理由 |
|------------|------------|------|
| 玩家（PlayerBase） | 否 | 玩家行为应始终能吸引敌人 |
| 其他敌人（EnemyBase） | 是 | 避免一群敌人排队踩同一块玻璃集体停下 |
| 装置自身/未知 | 否 | 保守不限制 |

### 5.3 同源不打断规则

正在调查某源时，同源再次触发**一律忽略**（不分触发者），避免重置计时导致调查链永远走不完。玩家想让敌人重调查可踩另一块玻璃（异源）。

---

## 六、朝向控制规则

### 6.1 巡逻点朝向配置（`PatrolPointConfig`）

仅在"从 Move 抵达巡逻点进入 Idle"瞬间应用一次（`mArrivedFromPatrol==true`）：

| 配置值 | 行为 |
|--------|------|
| `KeepCurrent` | 不改朝向 |
| `Left` | 强制朝左 |
| `Right` | 强制朝右 |
| `AutoByNextMove` | 朝向下一个巡逻点方向（单点退化为 KeepCurrent） |

### 6.2 仅警觉结束后恢复朝向

`mAlertOnly==true` 的 Alerted 结束回 `mPreAlertState=="Idle"` 时，设 `mArrivedFromPatrol=true`，复用上述朝向配置逻辑恢复站岗朝向。

### 6.3 其他朝向控制点

- `Move` 进入/更新时：朝向移动目标。
- `Alerted` 进入时：朝向异常源。
- `Investigate` 进入/更新时：朝向异常源。
- `Inspect` 更新时：周期性翻朝向（张望）。
- `Chase`/`Searching`：朝向追击/搜索目标。

---

## 七、单点站岗特殊规则

当 `mPatrolPoints.Count <= 1`（清洗 null 后）：

- `Start` 不调 `SetNextPatrolTarget`，直接停在初始位置 Idle。
- `OnIdleUpdate` 中 `Count<=1` 直接 return，Idle 无时限。
- `Inspect` 结束调 `SetTargetToFarthestPatrolPoint` 后回唯一巡逻点（回归路径），到达后继续站岗。
- `AutoByNextMove` 退化为 `KeepCurrent`。

---

## 八、典型时序示例

### 8.1 玩家踩玻璃（完整调查）

```
Idle -> [踩玻璃] -> Alerted(面朝源,1s) -> Investigate(走向源) -> Inspect(张望5s) -> Idle(回最远巡逻点)
```

### 8.2 异敌踩玻璃（仅警觉）

```
Idle -> [异敌踩玻璃] -> Alerted(面朝源,1s) -> Idle(回出发状态,恢复站岗朝向)
```

### 8.3 追丢玩家

```
Chase -> [离开视野] -> Searching(走向最后位置) -> Inspect(张望5s) -> Idle(回最远巡逻点)
```

### 8.4 完整调查中被异敌打断

```
Investigate(走向X) -> [异敌踩Y] -> Alerted(面朝X,1s) -> Investigate(继续走向X) -> Inspect(张望X) -> Idle
```

### 8.5 仅警觉中被玩家升级

```
Alerted(异敌X,仅警觉) -> [玩家踩Y] -> Alerted(面朝Y,重置计时) -> Investigate(走向Y) -> Inspect(张望Y) -> Idle
```
