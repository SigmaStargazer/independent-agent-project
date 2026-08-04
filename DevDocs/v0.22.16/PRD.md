# PRD - v0.22.16 SceneObj 状态动画驱动组件

> **状态**：已实现
> **对应需求**：`requirements/`（用户口头需求）
> **最后更新**：2026-08-03

---

## 1. 背景与目标

### 1.1 现状

项目所有 `SceneObjBase` 派生类（Chara、Device）已经有一套统一的有限状态机：

- `SceneObjBase` 提供 `mStates` / `ChangeState` / `OnStateChanged` 事件、`OnIdleEnter` 等 hook。
- `CharaBase` 扩展 `Dead` / `Follow`；`PlayerBase` 扩展 `Hidden`；`EnemyBase` 扩展 `Chase` / `Searching` / `Stunned` / `Alerted` / `Investigate` / `Inspect`。
- `DeviceBase` 子类各自注册状态：`SignalLight`（GreenLight/RedLight）、`Safebox`（Open/Close）、`LaserGrid`/`LaserGridAuto`（Active/Inactive）等。

但目前**没有任何组件把这些状态切换驱动到 Animator**。角色的 Idle/Move/Dead、敌人的 Chase/Alerted、装置的 Open/Close 等都只有逻辑状态，没有对应动画播放。

### 1.2 问题

用户希望给 Chara、Device 的各个状态都增加动画。需要决定 Animator 控制逻辑放在哪里：

| 候选位置 | 问题 |
|----------|------|
| 放进 `SceneObjBase` | 基类已承担 FSM、交互区域、范围方位、免疫语义等职责，继续塞动画会膨胀；且并非所有 SceneObj 都有 Animator |
| 放进 `HumanPlayer` | 只能管一个子类；`EnemyBase`、`AIPlayer`、所有 Device 都得各写一遍 |
| 放进 `CharaBase` | 只覆盖 Chara 一半，Device（`SignalLight`/`Safebox` 等）完全用不上 |

### 1.3 目标

**单独写一个动画控制脚本组件**（工作名 `SceneObjAnimator`），挂在带 Animator 的节点上，订阅 `SceneObjBase.OnStateChanged` 事件驱动动画。达成：

1. **零侵入**：不改 `SceneObjBase` / `CharaBase` / `PlayerBase` / `EnemyBase` / `DeviceBase` 任何现有逻辑。
2. **统一复用**：Chara 与 Device 共用同一组件，因为它们都走 `SceneObjBase.ChangeState`。
3. **可选挂载**：不需要动画的 SceneObj 不挂即可，基类不被迫承担 Animator 字段。
4. **状态名直驱**：组件尽量直接用 FSM 状态名（如 `"Idle"`、`"Open"`）作为动画状态/参数，减少配置成本。

## 2. 范围

### 2.1 本期包含

- 新增 Unity 组件 `SceneObjAnimator`（路径见 solution §3）
- 组件能力：
  - 订阅 `SceneObjBase.OnStateChanged`，收到 `(obj, oldState, newState)` 后驱动 Animator
  - 支持「状态名 → 动画状态名」映射（默认同名，允许覆盖）
  - 支持循环态（Idle/Move/Chase 等）与一次性态（Dead/Stunned/Open 等）两种播法
  - 支持角色朝向参数（读 `transform.localScale.x` 或 `CharaBase.IsRight`）写入 Animator
  - Animator 引用可在 Inspector 拖拽（允许 Animator 在子节点）
- 为现有主要状态梳理动画状态清单（见 §4.2），供美术/关卡配置 Animator Controller 时参照

### 2.2 本期不包含（明确不做）

- **不改协议 / Python / Agent 工具链路**：纯 Unity 表现层
- **不提供动画素材**：只提供驱动组件与配置规范，Clip/Animator Controller 由美术/关卡填入
- **不改任何现有 FSM 状态类**：`IdleState` / `MoveState` / `DeadState` 等保持不动
- **不做动画事件回调到逻辑**：不通过 AnimationEvent 反向触发状态切换（保持 FSM 单向权威）
- **不处理多 Animator 层合成**：本期仅单层状态驱动；混合树/多层叠加后续再说

### 2.3 后续可能迭代

- 多层 Animator（如身体层 + 表情层）
- 动画完成事件驱动状态退出（如 Open 动画播完才真正可用）
- 运行时动态生成 Animator Controller / 参数绑定

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 关卡/美术 | 给角色或装置配动画 | 挂 `SceneObjAnimator`，拖 Animator，按状态名建 State，即可自动播放 |
| 玩家观察 | 角色 Idle/Move/Dead、敌人 Chase/Alerted、装置 Open/Close | 状态切换时动画同步切换，无肉眼延迟 |
| 开发者 | 新增一个 Device 状态 | FSM 里 `RegisterState` + `ChangeState` 即可，动画组件无需改代码（状态名同名时零配置） |

## 4. 功能需求

### 4.1 组件核心行为

1. **订阅状态变更**：`OnEnable` 时订阅目标 `SceneObjBase.OnStateChanged`；`OnDisable` 取消订阅。
2. **初始状态同步**：`Start` 时读取 `SceneObjBase.StateName`，播放当前状态对应动画（避免挂载后不动）。
3. **状态→动画驱动**：收到 `newState` 后，按映射表查动画状态名，调用 Animator 播放。
   - 用 `Animator.CrossFade(stateName, duration)` 或 `Animator.Play(stateName)` **按状态名直接跳转**
   - Animator Controller 里**不需要连线**（不画 Transition）、**不需要触发值/切换值**（不建 Trigger/Bool 参数做状态切换），只建孤立的 State
   - 循环态与一次性态的区分由 Animator Controller 里该 State 的 Clip 设置决定，组件不强控
4. **朝向参数**：角色类对象每帧把朝向写入 Animator 的 Float 参数（如 `dirX`），供混合树或左右翻转使用。Device 无朝向则跳过。
5. **显式跳过**：映射表支持 `skipAnimation=true`，命中时该状态不驱动 Animator、不告警。用于 FSM 有该状态但不需要动画的场景（如 `Hidden`、EnemyBase 已移除的 `Follow`）。
6. **缺失容错**：Animator 为空 / 状态名未配置 / 动画状态不存在时，打 Warning 不报错；与显式跳过区分开。
7. **过渡方式可配**：组件级 `_crossFadeByDefault` 控制默认过渡方式，映射表逐项 `crossFade` 覆盖。逐帧 Sprite 动画默认瞬切（`Play`）避免重影，骨骼动画或特意设计的淡入淡出可逐项打开 `CrossFade`。

### 4.2 状态动画清单（配置参照）

下表列出当前代码中所有注册的状态，供配置 Animator Controller 时参照。状态名为 FSM `Name`，默认动画状态名同名。

#### Chara 通用（SceneObjBase + CharaBase）

| FSM 状态名 | 注册位置 | 循环/一次性 | 说明 |
|-----------|----------|------------|------|
| `Idle` | `SceneObjBase` | 循环 | 站立/待机 |
| `Move` | `SceneObjBase` | 循环 | 移动 |
| `Dead` | `CharaBase` | 一次性 | 死亡 |
| `Follow` | `CharaBase` | 循环 | 跟随（EnemyBase 已移除） |

#### PlayerBase 扩展

| FSM 状态名 | 注册位置 | 循环/一次性 | 说明 |
|-----------|----------|------------|------|
| `Hidden` | `PlayerBase` | 循环 | 躲藏（进入时关 Renderer，动画可能不可见，见 §7） |

#### EnemyBase 扩展

| FSM 状态名 | 注册位置 | 循环/一次性 | 说明 |
|-----------|----------|------------|------|
| `Chase` | `EnemyBase` | 循环 | 追击 |
| `Searching` | `EnemyBase` | 循环 | 追丢走向最后已知位置 |
| `Stunned` | `EnemyBase` | 一次性 | 被背刺击晕（永久） |
| `Alerted` | `EnemyBase` | 循环 | 警觉停顿面朝异常源 |
| `Investigate` | `EnemyBase` | 循环 | 走向异常源 |
| `Inspect` | `EnemyBase` | 循环 | 张望、周期翻朝向 |

#### Device 示例（各子类自注册）

| FSM 状态名 | 注册位置 | 循环/一次性 | 说明 |
|-----------|----------|------------|------|
| `GreenLight` / `RedLight` | `SignalLight` / `ClickableSignalLight` | 循环 | 信号灯颜色态 |
| `Open` / `Close` | `Safebox` | 一次性/循环 | 保险箱开关 |
| `Active` / `Inactive` | `LaserGrid` / `LaserGridAuto` | 循环 | 激光栅栏开关 |

### 4.3 朝向参数约定

- 参数名默认 `dirX`（可配）：`>0` 朝右，`<0` 朝左
- 数据来源：优先读 `CharaBase.IsRight`（`transform.localScale.x > 0`）；非 Chara 则不写
- 写入时机：`Update` 每帧（混合树需要连续值）或状态切换时（离散翻转）

## 5. 非功能需求

- **Unity 版本**：2021.3.8f1c1，URP 12.1.7
- **文件编码**：UTF-8（C# 无 BOM）
- **性能**：组件只在状态切换时调 Animator API，每帧仅写一个 Float（朝向），无额外开销
- **容错**：Animator 缺失 / 状态未映射时不得抛异常中断游戏
- **可观测**：状态映射失败时 `Debug.LogWarning` 一次（避免刷屏）

## 6. 验收标准

- [ ] 新增 `SceneObjAnimator` 组件，可挂载到任意带 Animator 的 GameObject
- [ ] 组件订阅 `SceneObjBase.OnStateChanged`，状态切换时 Animator 同步切换动画
- [ ] 组件 `Start` 时同步当前状态（挂载后立即处于正确动画）
- [ ] 角色朝向参数正确写入 Animator（左右移动时动画朝向跟随）
- [ ] Chara（HumanPlayer/EnemyBase）与 Device（SignalLight/Safebox 等）均可用同一组件
- [ ] Animator 未配置某状态时打 Warning 不报错
- [ ] 映射表 `skipAnimation=true` 的状态不驱动 Animator、不告警
- [ ] Animator Controller 无连线/无切换参数，仅靠组件按状态名跳转即可工作
- [ ] Sprite 状态默认瞬切（`_crossFadeByDefault=false`）无重影；逐项 `crossFade=true` 可过渡
- [ ] 不修改任何现有 FSM 类（`SceneObjBase`/`CharaBase`/`PlayerBase`/`EnemyBase`/`DeviceBase` 及子类）
- [ ] 至少一个角色 + 一个装置接入验证（如 HumanPlayer Idle/Move + SignalLight Red/Green）

## 7. 待确认问题

以下三项已通过 2026-08-03 对话确认，转为已决策：

- [x] **FSM 状态可否无对应动画机状态**：可以。映射表新增 `skipAnimation` 字段，命中则跳过、不告警；未在映射表命中且 Animator 也无该 State 时走 Warning 容错兜底。
- [x] **动画机是否需要连线/触发值**：不需要。组件用 `CrossFade`/`Play` 按状态名直接跳转，Animator Controller 只建孤立 State，不画 Transition、不建切换参数；约束一律平铺、不嵌套 Sub-State Machine。
- [x] **Sprite 是否全设 crossFade=false**：逐帧 Sprite 默认瞬切（`_crossFadeByDefault=false`）避免重影，但允许个别状态逐项打开 `CrossFade`，不一刀切。

仍待确认：

- [x] **`Hidden` 状态动画可见性**：按推荐方案，**接受无动画 + 用 `skipAnimation` 显式跳过 Hidden**。Hidden 语义就是看不见，不排除 Animator 节点。如未来需要可见，另起方案改 `PlayerBase`。
- [x] **一次性态结束处理**：按推荐方案，**不需要通知逻辑**。FSM 单向权威，动画只表现；`Dead`/`Stunned` 等一次性态播完即停（由 Animator Controller 的 Clip 非循环保证），不回写 FSM。
- [x] **`Follow` 状态**：按推荐方案，本期 `Follow` 用 `skipAnimation` 显式跳过（或建粗粒度 Follow 动画），不处理其内部走/停表现。`Follow` 作为 FSM 状态粒度过粗的设计问题已记录到 `DevDocs/需求池/backlog.md` 条目 11，后续版本重构为移动策略，届时动画组件无需改动。

**PRD §7 全部待确认问题已闭环，转为已确认状态。**

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
