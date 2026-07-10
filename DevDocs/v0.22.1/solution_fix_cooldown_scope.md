# Flag 梳理与冷却范围修正方案（v2）

> **状态**：已实现
> **背景**：v0.22.1 同源冷却范围偏离需求；用户指出 flag 设计混乱，要求全面梳理后再改。
> **最后更新**：2026-07-10

---

## 1. 当前所有异常相关 flag 的完整梳理

### 1.1 字段清单

| 字段 | 类型 | 含义 | 何时赋值 | 何时读取 |
|------|------|------|----------|----------|
| `mAnomalySource` | `Vector2` | 当前调查的异常源坐标 | `OnHearAnomaly` 更新时 | `OnAlertedEnter` / `OnInvestigateEnter` / `OnInvestigateFixedUpdate` 里面朝与走向 |
| `mCurrentSourceDevice` | `SceneObjBase` | 当前调查链对应的声源装置实例 | `OnHearAnomaly` 更新时 | `OnEnemyAnomalyEventFired` 同源不打断判定；`WriteCooldownAndClearCurrentSource` 写冷却 |
| `mAlertOnly` | `bool` | **当前调查链**是否为"仅警觉"模式（异敌触发，Alerted 结束回 mPreAlertState 而不进 Investigate） | `OnHearAnomaly` 按事件是否异敌触发赋值 | `OnAlertedUpdate` 分流（true->回 mPreAlertState，false->Investigate） |
| `mPreAlertState` | `string` | 进入 Alerted 前的状态（"Idle" 或 "Move"），仅警觉结束后回哪个 | `OnHearAnomaly` 首次从 Idle/Move 进入时记录 | `OnAlertedUpdate` 仅警觉分支 `ChangeState(mPreAlertState)` |
| `mSourceCooldowns` | `Dict<SceneObjBase, float>` | 每个声源装置对该敌人的冷却截止时间 | `WriteCooldownAndClearCurrentSource` / `OnHearAnomaly` 替换源时 | `OnEnemyAnomalyEventFired` 冷却检查 |

### 1.2 关键发现：`mAlertOnly` 已经携带了"是否异敌触发"的信息

`mAlertOnly` 的赋值规则（`OnHearAnomaly`）：
- `eventIsAlertOnly = triggerer is EnemyBase && triggerer != this`
- 首次进入：`mAlertOnly = eventIsAlertOnly`
- 替换源（B2/C1/C2）：`if (!eventIsAlertOnly) mAlertOnly = false;`（完整调查覆盖仅警觉）

**所以 `mAlertOnly == true` 当且仅当当前调查链是异敌触发的仅警觉模式。** 用户说的对：不需要再加 `mCurrentSourceIsAlertOnly`，`mAlertOnly` 本身就是"当前链是否异敌触发"的标志。

### 1.3 当前 `!mAlertOnly && eventIsAlertOnly` 分支的含义与问题

```csharp
else if (!mAlertOnly && eventIsAlertOnly)
{
    // 完整调查中被异敌打断：保留原目标与原当前源，不写冷却。
}
```

**设计意图**：敌人正在完整调查（玩家踩玻璃 X）途中，另一个敌人踩了玻璃 Y（异敌触发）。此时：
- 不应放弃原调查目标 X（玩家触发更重要）。
- 不应把 Y 设为当前源（Y 只是异敌警觉，不该接管调查）。
- Alerted 面朝**原 mAnomalySource（X 的位置）**做一次警觉反应，然后继续 Investigate X。

**这条分支本身是合理的**，但它暴露了 flag 的一个语义重叠：

- `mAlertOnly` 同时承担了两个语义：
  1. "Alerted 结束后是否进 Investigate"（状态机分流用）。
  2. "当前链是否异敌触发"（决定是否写冷却）。
- 在 `!mAlertOnly && eventIsAlertOnly` 分支里，`mAlertOnly` 保持 false（正确，因为要继续完整调查），但**这次事件本身是异敌触发的**。如果用 `mAlertOnly` 判断"是否写冷却"，这条分支不写冷却是正确的（因为没替换源）--**所以用 `mAlertOnly` 判断冷却在这条分支恰好也是对的**。

**结论：用 `mAlertOnly` 替代 `mCurrentSourceIsAlertOnly` 是可行的，逻辑自洽。** 我之前加 `mCurrentSourceIsAlertOnly` 是冗余设计。

## 2. 修正后的方案（v2）

### 2.1 核心原则

**不新增字段**。利用 `mAlertOnly` 已有的语义：
- `mAlertOnly == true`：当前链是异敌触发的仅警觉 -> 链条结束时**写冷却**。
- `mAlertOnly == false`：当前链是玩家/装置触发的完整调查 -> 链条结束时**不写冷却**。

### 2.2 改动点（共 3 处，均在 `EnemyBase.cs`）

#### 改动 1：`WriteCooldownAndClearCurrentSource` -- 只对异敌触发链写冷却

```csharp
private void WriteCooldownAndClearCurrentSource()
{
    if (mCurrentSourceDevice != null)
    {
        // 同源冷却仅针对"其他敌人触发"的仅警觉链；玩家/装置触发的完整调查不写冷却。
        if (mAlertOnly)
        {
            mSourceCooldowns[mCurrentSourceDevice] = Time.time + mSameSourceCooldown;
        }
        mCurrentSourceDevice = null;
    }
}
```

#### 改动 2：`OnHearAnomaly` 替换源分支 -- 旧源只在该写冷却时才写

```csharp
else
{
    // B2 / C1 / C2：替换当前源。
    if (mCurrentSourceDevice != null && mCurrentSourceDevice != sourceDevice)
    {
        // 旧链是异敌仅警觉(mAlertOnly==true)才写冷却；玩家完整调查链被新源替换时不写。
        if (mAlertOnly)
        {
            mSourceCooldowns[mCurrentSourceDevice] = Time.time + mSameSourceCooldown;
        }
    }
    if (!eventIsAlertOnly) mAlertOnly = false;
    mAnomalySource = sourcePos;
    mCurrentSourceDevice = sourceDevice;
}
```

**注意**：这里检查的是**旧的** `mAlertOnly`（在 `if (!eventIsAlertOnly) mAlertOnly = false;` 之前），表示旧链是否异敌触发。赋值顺序保证正确。

#### 改动 3：`OnEnemyAnomalyEventFired` -- 冷却检查只对异敌触发事件生效

```csharp
private void OnEnemyAnomalyEventFired(EnemyAnomalyEvent evt)
{
    if (Vector2.Distance(transform.position, evt.SourcePos) > evt.Radius) return;
    if (evt.SourceDevice != null && evt.SourceDevice == mCurrentSourceDevice) return;

    // 同源冷却仅对"其他敌人触发"的事件生效；玩家/装置触发的事件跳过冷却检查。
    bool eventIsAlertOnly = evt.Triggerer is EnemyBase && evt.Triggerer != this;
    if (eventIsAlertOnly
        && evt.SourceDevice != null
        && mSourceCooldowns.TryGetValue(evt.SourceDevice, out float endTime)
        && Time.time < endTime)
    {
        return;
    }

    OnHearAnomaly(evt.SourcePos, evt.Triggerer, evt.SourceDevice);
}
```

### 2.3 不改动的部分

- **`mAlertOnly` 的赋值逻辑**（`OnHearAnomaly` 三个分支）：保持不变，已经正确。
- **`!mAlertOnly && eventIsAlertOnly` 分支**：保持不变。这条分支不替换源、不写冷却、不改 `mAlertOnly`，行为正确。
- **同源不打断规则**（`evt.SourceDevice == mCurrentSourceDevice`）：保持对所有来源生效（见 §3）。
- **字段集合**：不新增 `mCurrentSourceIsAlertOnly`，删除该提案。

## 3. 待确认问题

1. **同源不打断规则**：正在调查玻璃 X 时（无论玩家还是异敌触发），同源 X 再次触发一律忽略（不重进 Alerted、不重置计时）。是否保持？
   - **推荐保持**：避免玩家反复踩同一块玻璃导致调查链永远走不完；玩家想让敌人重调查可踩另一块玻璃（异源）。
2. **冷却时长**：修正后冷却只对异敌触发生效，15s 默认值是否合适？
   - **推荐先保持**，观察实测再调。

## 4. 验证场景

| 场景 | 触发 | 期望 |
|------|------|------|
| 玩家踩玻璃 X -> 调查结束 -> 立刻再踩 X | 玩家 | **响应**（玩家触发无冷却） |
| 敌人 A 踩玻璃 X -> 敌人 B 仅警觉 -> B 回 Idle -> 15s 内 A 再踩 X | 异敌 | B **忽略**（异敌同源冷却内） |
| 场景 2 后等 15s 过去 -> A 再踩 X | 异敌 | B **响应**（冷却已过） |
| 玩家踩 X 调查中 -> 异敌踩 Y（异源） | 异源替换 | 转向 Y；旧源 X 是玩家触发（mAlertOnly==false）不写冷却 |
| 异敌踩 X 仅警觉中 -> 玩家踩 Y（异源） | 异源替换 | 升级为完整调查 Y；旧源 X 是异敌触发（mAlertOnly==true）写冷却 |

---

## 5. 实现记录

| 日期 | 说明 |
|------|------|
| 2026-07-03 | 按 v2 方案实现：3 处改动（`WriteCooldownAndClearCurrentSource` 加 `mAlertOnly` 守卫、`OnHearAnomaly` 替换源分支加 `mAlertOnly` 守卫、`OnEnemyAnomalyEventFired` 冷却检查加 `eventIsAlertOnly` 前置条件）。不新增字段，复用 `mAlertOnly` 判断当前链是否异敌触发。同源不打断规则保持对所有来源生效；冷却时长保持 15s。 |

---

*本文档由 Cursor Agent 生成；**你确认后** Agent 方可按本方案修改代码。*
