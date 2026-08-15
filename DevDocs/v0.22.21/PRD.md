# PRD — v0.22.21 HumanPlayer 高频换向时朝向错误修复

> **状态**：已验收
> **对应需求**：`requirements/需求文档.md`
> **最后更新**：2026-08-16

---

## 1. 背景与目标

### 1.1 现状

`HumanPlayer` 的移动与朝向由以下链路驱动：

```
HumanPlayer.GetInput (每帧 Update)
  → float horizontal = Input.GetAxisRaw("Horizontal")
  → horizontal != 0：moveRight = horizontal > 0; ChangeState("Move")
  → horizontal == 0：ChangeState("Idle")

PlayerBase.OnMoveEnter (Move 状态进入时)
  → float dir = moveRight ? 1f : -1f; TurnBack(dir)

CharaBase.TurnBack(dir)
  → 仅当「当前朝向与目标方向不一致」时才翻转 localScale.x

SceneObjBase.ChangeState
  → if (StateName == stateName) return;  // 同状态去重，不触发 OnExit/OnEnter
```

### 1.2 问题

频繁左右切换时，会出现**移动方向与朝向（`Scale.X` 正负号）不一致**；切换不频繁时正常。

### 1.3 目标

- 明确根因；
- 给出修复方案，使任何切换频率下移动方向与朝向保持一致；
- 覆盖 `HumanPlayer`，并评估对 `AIPlayer` / `EnemyBase`（同类 `TurnBack` 链路）的影响范围。

## 2. 范围

### 2.1 本期包含

- 根因分析（含完整场景枚举与时序证据）
- 修复方案：**转向判断完全在 `CharaBase` 的 FSM hook 内从 `velocity.x` 推导；子类只允许对速度/位移赋值；禁止 `MoveDirection`；零新增（只改现有 FSM hook）**，覆盖 `HumanPlayer` / `AIPlayer` / `EnemyBase`（待确认后实施）
- 回归验证

### 2.2 本期不包含

- 不改变 `TurnBack` 的翻转语义（面朝方向判定仍以 `localScale.x` 为准）
- 不改动 `message.proto` / Python 侧
- 不新增其他角色行为

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家 | 向右移动 | 面朝右（`Scale.X > 0`） |
| 玩家 | 向左移动 | 面朝左（`Scale.X < 0`） |
| 玩家 | 高频左右切换 | 每帧移动方向与朝向一致 |
| 玩家 | 低频切换（中间停顿） | 朝向正确（现状已正确） |

## 4. 功能需求

### 4.1 移动方向与朝向严格一致

任意切换频率下，`moveRight` 决定速度方向，`Scale.X` 正负号必须与速度方向一致。

### 4.2 错误朝向可被自愈

一旦因任何原因（含外部还原、初始值、历史残留）出现 `Scale.X` 与移动方向不一致，在移动状态下应自动纠正（自愈强度方案二选一，见 `solution.md §3.3.1`）。

### 4.3 修复范围（架构方向）

- **转向判断完全收敛到 `CharaBase` 的 FSM hook 内，由「当前速度方向（`velocity.x`）」推导**：`CharaBase.OnMoveFixedUpdate`（Move 状态 hook）读 `velocity.x` 决定是否转向；Follow 在 `CharaBase.OnFollowFixedUpdate` 内面向跟随目标。
- **`PlayerBase` / `AIPlayer` 只允许对「速度 / 位移」赋值**：子类算出速度矢量（`moveRight?1:-1`）并写 `mRigidbody2D.velocity`，通过调 `base.OnMoveFixedUpdate()` 等让 Chara 完成转向。子类不得出现 `MoveDirection` / `TurnBack` / `FaceByMoveDirection` / 直接读写 `localScale.x` 等转向判断代码。
- **零新增**：不新增任何方法、属性、接口、字段、常量；只允许修改现有 FSM hook（`OnXxxEnter` / `OnXxxUpdate` / `OnXxxFixedUpdate` / `OnXxxExit`）的实现。
- **Enemy 专属的 Chase / Searching / Investigate / Alerted / Inspect / 站岗 6 状态**：状态 Hook 在 `EnemyBase` 内自注册、自维护，**转向保留在 Enemy 的这些状态 Hook 内**（已确认豁免，不收敛到 Chara）。
- 具体设计见 `solution.md §3.3.4`。

## 5. 非功能需求

- 文件 UTF-8
- 不破坏 `AIPlayer`（Agent 移动工具/Follow）与 `EnemyBase`（巡逻/追击/调查）的既有朝向行为

## 6. 验收标准

- [ ] 高频左右切换时，任意时刻移动方向与 `Scale.X` 正负号一致
- [ ] 低频切换时行为不回归
- [ ] `AIPlayer` Move / Follow 朝向行为不回归
- [ ] `EnemyBase` 巡逻 / 追击 / 调查朝向行为不回归

## 7. 待确认问题

- [ ] 自愈强度采用写法 1（条件翻转）还是写法 2（强制对齐）？优劣分析见 `solution.md §3.3.1`，默认写法 1。
- [ ] Idle 时不自动回正（只修正移动中的朝向）——此项维持现状，无需改动。
- [ ] **架构红线**：转向判断完全在 `CharaBase` 的 FSM hook 内从 `velocity.x` 推导；子类只允许对速度/位移赋值；禁止 `MoveDirection`；零新增（只改现有 FSM hook）——详见 `solution.md §3.3.4` 待确认问题。
- [ ] **Enemy 专属 6 状态（Chase / Searching / Investigate / Alerted / Inspect / 站岗）**：**已确认（2026-08-16）**——状态 Hook 在 `EnemyBase` 内自注册、自维护，转向在 Enemy 的这些状态 Hook 内进行（维持现状，不收敛到 Chara）。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
