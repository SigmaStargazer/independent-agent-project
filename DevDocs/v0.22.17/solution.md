# 技术方案 - v0.22.17 SceneObjAnimator 进阶：单状态内多动画流转

> **状态**：已确认
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-04

---

## 1. 方案概述

在 v0.22.16 的 `SceneObjAnimator` 基础上，扩展两项能力：

1. **转换边动画**：`HandleStateChanged` 用 `(oldState, newState)` 查转换映射表，命中则播过渡动画入口
2. **物理参数透传**：每帧向 Animator 写 `velY` / `grounded`，供 Animator 自管跳跃等内部阶段

核心原则不变：FSM 状态间的切换由组件 `CrossFade` 驱动；单个 FSM 状态的动画内部流转由 Animator 自管。本期只加能力，不改 FSM。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | - | 无 |
| Unity | `SceneObjAnimator.cs` | 扩展（新增转换映射 + 物理参数透传） |
| Unity | 各 Animator Controller 资产 | 配置转换边动画 / 跳跃阶段（美术/关卡） |
| 协议 | 无 | 无 |

**明确不改**：任何 FSM 类、v0.22.16 现有配置字段语义。

## 3. 详细设计

### 3.1 新增字段

在 v0.22.16 已有字段基础上新增：

```csharp
// === 转换边动画 ===
[SerializeField] private List<TransitionMapping> _transitions = new();

// === 物理参数透传 ===
[SerializeField] private string _velYParam = "";        // 留空则不写
[SerializeField] private string _groundedParam = "";    // 留空则不写
[SerializeField] private Collider2D _groundCollider;    // 拖入 GroundCheck 子物体的 CircleCollider2D（Trigger）
[SerializeField] private LayerMask _groundLayerMask = 0;
```

> **GroundCheck 约定**：在角色 GameObject 下创建名为 `GroundCheck` 的子物体，挂 `CircleCollider2D`（勾选 IsTrigger）。把该 Collider 拖到 `_groundCollider` 字段。组件每帧用 `IsTouchingLayers(_groundLayerMask)` 判定是否触地。

### 3.2 新增数据结构

```csharp
[Serializable]
public class TransitionMapping
{
    public string fromState;      // 旧 FSM 状态名，如 "Idle"
    public string toState;        // 新 FSM 状态名，如 "Move"
    public string animState;      // 过渡动画入口名，如 "Move_Start"
    public bool crossFade = true; // 是否 CrossFade（默认过渡）
}
```

运行时建 `(fromState + "->" + toState)` 为 key 的字典。

### 3.3 HandleStateChanged 增强

v0.22.16 的 `HandleStateChanged` 只看 `newState`，直接 `CrossFade(newState)`。本期增强为：**先查 `(oldState, newState)` 转换映射，命中则播过渡动画入口，未命中走原逻辑。**

```csharp
private void HandleStateChanged(SceneObjBase obj, string oldState, string newState)
{
    // 1. 先查转换映射 (oldState, newState)
    string key = oldState + "->" + newState;
    if (_transitionMap.TryGetValue(key, out var t))
    {
        // 命中：播放过渡动画入口（如 Move_Start），之后由 Animator 内部 exitTime 自动转到目标 Loop
        PlayAnim(t.animState, t.crossFade);
        return;
    }
    // 2. 未命中：v0.22.16 行为不变，直接播 newState 对应动画
    PlayState(newState);
}
```

**完整时序（以 Idle -> Move 起跑为例）**：

```
时刻 T0: FSM 调用 ChangeState("Move")
  └─ SceneObjBase 触发 OnStateChanged(obj, "Idle", "Move")
     └─ 组件 HandleStateChanged 收到 (oldState="Idle", newState="Move")
        └─ 查转换映射 "Idle->Move"，命中 animState="Move_Start"
        └─ CrossFade("Move_Start", 0.1f)  ← 组件只做这一步

时刻 T0 ~ T0+0.3s: Animator 播放 Move_Start 动画（起跑动作）
  └─ 组件不再介入，FSM 也不切状态
  └─ 物理参数仍每帧更新（velY 随起跳变化）

时刻 T0+0.3s: Move_Start 播完（Clip 结束）
  └─ Animator 内部 Transition（HasExitTime, exitTime=1.0）自动触发
  └─ Move_Start -> Move_Loop  ← Animator 自己跳，组件不参与
  └─ 之后角色持续播放 Move_Loop 循环走动
```

**关键点**：

- 组件只负责「进入过渡动画入口」这一步，之后 Animator 内部 Transition 接管。
- 过渡动画 `Move_Start` 到 `Move_Loop` 的跳转是 **Animator 内部 Transition**，配 `HasExitTime=true` + `Transition Duration=0`（播完即跳，不再过渡）。
- 过渡动画中途 FSM 又切状态（如 Move -> Chase）：组件收到 `(Move, Chase)`，查转换映射未命中（通常 Chase 没配过渡），直接 `CrossFade("Chase")` 打断起跑。这是期望行为（FSM 权威优先）。
- 如果 `(Move, Chase)` 也配了过渡动画（如 `Move_ToChase`），则播那段过渡。

### 3.4 物理参数透传

在 `Update` 中（与 v0.22.16 的 `dirX` 写入合并）：

```csharp
private void Update()
{
    if (_animator == null) return;

    // 朝向（v0.22.16 已有）
    if (!string.IsNullOrEmpty(_facingParam) && _target is CharaBase chara)
        _animator.SetFloat(_facingParam, chara.IsRight ? 1f : -1f);

    // 物理参数：只对有 Rigidbody2D 的对象写
    if (_target is CharaBase c && c.Rigidbody2D != null)
    {
        var vel = c.Rigidbody2D.velocity;
        if (!string.IsNullOrEmpty(_velYParam))
            _animator.SetFloat(_velYParam, vel.y);
    }

    // grounded
    if (!string.IsNullOrEmpty(_groundedParam) && _groundCollider != null)
        _animator.SetBool(_groundedParam, _groundCollider.IsTouchingLayers(_groundLayerMask));
}
```

**注意**：`CharaBase.Rigidbody2D` 当前是 `protected`，需确认是否暴露 public 访问器。若不便暴露，组件可自行 `GetComponent<Rigidbody2D>()` 缓存。倾向后者（组件自洽，不改 CharaBase）。

### 3.5 Animator 配置规范（给美术/关卡）

#### Idle 随机插播

```
Idle_Base (Loop)
  └─Transition(exitTime=5~10s, 随机)─> Idle_Var1 (No Loop)
  └─Transition(exitTime=5~10s, 随机)─> Idle_Var2
Idle_Var1/2 ─Transition(exitTime=1.0)─> Idle_Base
```
组件映射：FSM `"Idle"` -> `"Idle_Base"`。无需转换映射。

#### 起跑/刹车

**目标**：Idle -> Move 时先播起跑动作再进入走动循环；Move -> Idle 时先播刹车动作再进入待机循环。

**Animator Controller 里的 State 布局**：

```
┌──────────────┐         ┌──────────────┐         ┌──────────────┐
│  Idle_Base   │         │  Move_Start  │         │  Move_Loop   │
│  (Loop)      │         │  (No Loop)   │         │  (Loop)      │
└──────────────┘         └──────┬───────┘         └──────────────┘
       │                         │
       │                  HasExitTime
       │                  exitTime=1.0
       │                  Duration=0
       │                         │
       │                         ▼
       │                 ┌──────────────┐
       │                 │  Move_Loop   │
       │                 │  (Loop)      │
       │                 └──────────────┘
       │
       │  ┌──────────────┐
       │  │  Move_Brake  │
       │  │  (No Loop)   │
       │  └──────┬───────┘
       │         │
       │  HasExitTime
       │  exitTime=1.0
       │  Duration=0
       │         │
       └─────────▼
       ┌──────────────┐
       │  Idle_Base   │
       │  (Loop)      │
       └──────────────┘
```

**Animator Transition 配置**（在 Animator 窗口里建这些连线）：

| From | To | Conditions | HasExitTime | ExitTime | Duration |
|------|----|-----------|-------------|----------|----------|
| Move_Start | Move_Loop | 无 | true | 1.0 | 0（播完即跳） |
| Move_Brake | Idle_Base | 无 | true | 1.0 | 0（播完即跳） |

注意：**不要建** Idle_Base -> Move_Start、Move_Loop -> Move_Brake 这类连线。这些跳转由组件 `CrossFade` 完成，不经过 Animator Transition。

**组件配置（Inspector）**：

状态映射表 `_mappings`：

| fsmState | animState | 说明 |
|----------|-----------|------|
| `Idle` | `Idle_Base` | Idle 的动画入口 |
| `Move` | `Move_Loop` | Move 的动画入口（直接进 Move 时用，如从 Chase 回 Move） |

转换映射表 `_transitions`：

| fromState | toState | animState | crossFade |
|-----------|---------|-----------|-----------|
| `Idle` | `Move` | `Move_Start` | true |
| `Move` | `Idle` | `Move_Brake` | true |

**完整时序**：

```
=== 起跑 ===
T0: FSM ChangeState("Move")
 -> 组件收到 (Idle, Move)
 -> 查转换映射命中 "Idle->Move" -> "Move_Start"
 -> CrossFade("Move_Start", 0.1f)
 -> Animator 从 Idle_Base 过渡到 Move_Start（0.1s 混合）

T0+0.1s: 完全进入 Move_Start，播放起跑动画

T0+0.4s（假设起跑 Clip 长 0.3s）: Move_Start 播完
 -> Animator 内部 Transition (HasExitTime) 触发
 -> Move_Start -> Move_Loop（瞬间跳，Duration=0）
 -> 角色开始循环走动

=== 刹车 ===
T1: FSM ChangeState("Idle")
 -> 组件收到 (Move, Idle)
 -> 查转换映射命中 "Move->Idle" -> "Move_Brake"
 -> CrossFade("Move_Brake", 0.1f)

T1+0.4s: Move_Brake 播完
 -> Animator 内部 Transition -> Idle_Base
 -> 角色回到待机循环

=== 被打断 ===
T2: 起跑动画 Move_Start 播到一半时，玩家被敌人发现
 -> FSM ChangeState("Chase")（假设 EnemyBase 有 Chase）
 -> 组件收到 (Move, Chase)
 -> 查转换映射 "Move->Chase" 未命中
 -> 直接 CrossFade("Chase", 0.1f)
 -> 起跑动画被打断，角色立即切到追击动画
```

**为什么 Move 状态映射到 `Move_Loop` 而不是 `Move_Start`**：

因为不是所有进入 Move 的路径都该播起跑。例如从 Chase 退出回到巡逻 Move，不需要起跑动作。只有 `(Idle, Move)` 这条转换边才需要起跑。所以：
- 状态映射 `Move -> Move_Loop`：直接进 Move 时播循环走动
- 转换映射 `(Idle, Move) -> Move_Start`：从 Idle 进 Move 时先播起跑

#### 跳跃阶段

> **本期范围说明**：仅交付组件能力（`velY` / `grounded` 透传 + Animator 配置规范）。当前 FSM 无 `Jump` 状态，本期**不新增** Jump FSM 状态；等后续版本加 Jump 状态时，组件能力可直接复用，无需再改。

**目标**：FSM 只有一个 `Jump` 状态，但动画需要按物理阶段自动流转：起跳 -> 上升 -> 最高点过渡 -> 下落 -> 着地。

**核心机制**：组件不切动画状态，而是每帧写 `velY`（垂直速度）和 `grounded`（是否触地）两个参数。Animator 用 Transition + Conditions（参数条件）自动选择当前播哪段动画。组件只在进入 `Jump` 状态时做一次 `CrossFade` 进入跳跃动画入口，之后全靠参数驱动。

**Animator Controller 里的 State 布局**：

```
                    ┌──────────────┐
          ┌────────>│  Jump_Land   │
          │         │  (No Loop)   │
          │         └──────┬───────┘
          │                │ HasExitTime
          │                ▼
          │         ┌──────────────┐
          │         │  Idle_Base   │← 着地后回到待机
          │         │  (Loop)      │  （或回 Jump_Base 待下一次跳）
          │         └──────────────┘
          │
     grounded: false->true
          │
┌─────────┴──────┐              ┌──────────────┐    velY<0     ┌──────────────┐
│  Jump_Fall     │<─HasExitTime─│  Jump_Apex   │<──velY<0──────│  Jump_Rise   │
│  (Loop)        │              │  (No Loop)   │               │  (Loop)      │
└────────────────┘              └──────────────┘               └──────┬───────┘
       ▲                                                              │
       │                                                        grounded=false
       │                                                        & velY>0
       │                                                              │
       │                                                    ┌───────────┴──────┐
       │                                                    │  Jump_Launch     │
       │                                                    │  (No Loop)       │
       │                                                    └──────┬───────────┘
       │                                                           │ HasExitTime
       │                                                           ▼
       │                                                    ┌──────────────┐
       └────────────────────────────────────────────────────│  Jump_Rise   │
                         （直切，无 Apex 时用）               │  (Loop)      │
                                                         └──────────────┘
```

`Jump_Apex` 是上升转下落之间的过渡动作（如空中转身/收腿）。如果不需要过渡动作，可以去掉 `Jump_Apex`，让 `Jump_Rise` 直接 `velY<0` 切到 `Jump_Fall`（图中下方虚线路径）。

**Animator Transition 配置**（关键连线）：

| From | To | Conditions | HasExitTime | 说明 |
|------|----|-----------|-------------|------|
| Jump_Launch | Jump_Rise | 无 | true | 起跳动作播完自动转上升 |
| Jump_Rise | Jump_Apex | `velY < 0` | false | 垂直速度变负（开始下落）时切到过渡动作 |
| Jump_Apex | Jump_Fall | 无 | true | 过渡动作播完自动转下落 |
| Jump_Fall | Jump_Land | `grounded == true` | false | 触地时切到着地动作 |
| Jump_Land | Idle_Base | 无 | true | 着地动作播完回待机 |

**无过渡动作的简化版**（去掉 `Jump_Apex`，Rise 直切 Fall）：

| From | To | Conditions | HasExitTime | Duration | 说明 |
|------|----|-----------|-------------|----------|------|
| Jump_Rise | Jump_Fall | `velY < 0` | false | 0.15s | 上升直接过渡到下落，无专门过渡动作 |

简化版用 Duration 做交叉淡入混合。**注意**：逐帧 Sprite 动画用 Duration 混合会产生重影（两帧同时渲染），骨骼动画则没问题。Sprite 项目建议用 `Jump_Apex` 过渡动作方案。

参数定义（Animator Parameters）：

| 参数名 | 类型 | 写入方 |
|--------|------|--------|
| `velY` | Float | 组件每帧写 `Rigidbody2D.velocity.y` |
| `grounded` | Bool | 组件每帧写 `Collider2D.IsTouchingLayers(groundMask)` |

**组件配置（Inspector）**：

状态映射表 `_mappings`：

| fsmState | animState | 说明 |
|----------|-----------|------|
| `Jump` | `Jump_Launch` | 进入跳跃时从起跳动作开始（假设跳跃一定从地面起跳） |

物理参数：

| 字段 | 值 | 说明 |
|------|-----|------|
| `_velYParam` | `"velY"` | 每帧写垂直速度 |
| `_groundedParam` | `"grounded"` | 每帧写触地状态 |
| `_groundCollider` | 拖入角色的脚部 Collider2D | 用于 `IsTouchingLayers` |
| `_groundLayerMask` | 勾选地面 Layer | 配错会导致 grounded 误判 |

**完整时序**：

```
T0: 玩家按跳跃键，FSM ChangeState("Jump")
 -> 组件收到 (*, Jump)
 -> 查转换映射未命中（跳跃通常不需要转换边动画）
 -> PlayState("Jump") -> 状态映射命中 -> CrossFade("Jump_Launch")
 -> Animator 播放起跳动画（No Loop）

  此刻组件的活干完了。之后每帧：
  -> 组件 Update() 写 velY = Rigidbody2D.velocity.y
  -> 组件 Update() 写 grounded = Collider.IsTouchingLayers(groundMask)

T0+0.1s: 角色离地（Rigidbody 给了向上速度）
 -> velY > 0, grounded = false
 -> Animator 参数更新

T0+0.2s（起跳 Clip 播完）:
 -> Animator 内部 Transition (HasExitTime) 触发
 -> Jump_Launch -> Jump_Rise（Loop）
 -> 角色播放上升循环动画

T0+0.5s（到达最高点，开始下落）:
 -> velY 从正变负
 -> Animator 检测到条件 velY < 0 满足
 -> Jump_Rise -> Jump_Apex（No Loop，过渡动作，如空中转身）
 -> 角色播放上升转下落的过渡动画（0.1~0.15s，建议短）

T0+0.65s（Apex 过渡动作播完）:
 -> Animator 内部 Transition (HasExitTime) 触发
 -> Jump_Apex -> Jump_Fall（Loop）
 -> 角色播放下落循环动画

T0+0.8s（着地）:
 -> grounded 从 false 变 true
 -> Animator 检测到条件 grounded == true
 -> Jump_Fall -> Jump_Land（No Loop）
 -> 角色播放着地动画

T0+1.0s（着地 Clip 播完）:
 -> Animator 内部 Transition (HasExitTime)
 -> Jump_Land -> Idle_Base
 -> 角色回到待机

  如果 FSM 在着地前就退出 Jump（如被击杀）:
 -> FSM ChangeState("Dead")
 -> 组件收到 (Jump, Dead)
 -> CrossFade("Dead")，打断跳跃动画
 -> Animator 立即切到死亡动画，velY/grounded 参数不再影响
```

**为什么用参数驱动而不是 FSM 子状态**：

起跳/上升/下落/着地是**物理阶段**，不是逻辑状态。FSM 不需要知道「角色正在上升还是下落」--这纯粹是表现层的事。如果把它们拆成 FSM 状态，每帧都要在 `FixedUpdate` 里检测 `velY` 并 `ChangeState`，既污染逻辑层又增加状态数量。用参数驱动把表现细节留在 Animator 内部，FSM 保持干净。

**Jump_Apex 过渡动作与物理同步**：

`Jump_Apex` 在 `velY < 0`（开始下落）时触发，但此时角色物理上已经在下落了，而动画还要播 0.1~0.15s 的过渡动作。这会导致动画比物理「慢半拍」。缓解方式：

- **过渡动作要短**（0.1~0.15s），玩家基本感知不到不同步
- **接受轻微不同步**：多数 2D 平台跳跃游戏（如 Celeste、Hollow Knight）都有类似处理，玩家不会注意
- **极端精确方案**（不推荐）：用 `velY < 阈值`（如 `< 0.5`）在接近最高点时提前触发 Apex，但阈值需逐角色调参，不够通用

**无过渡动作的替代**：如果上升和下落动画姿态接近（不需要专门的转身动作），可以去掉 `Jump_Apex`，让 `Jump_Rise` 直接用 `velY < 0` 条件 + `Duration=0.15s` 过渡到 `Jump_Fall`。骨骼动画用 Duration 混合平滑；逐帧 Sprite 动画用 Duration 会重影，仍建议用 `Jump_Apex` 方案。

**Jump_Base 入口的替代方案**：

如果跳跃不一定从地面起跳（如从平台边缘直接走出后下落），可以把入口设为 `Jump_Base`（空 State），进入后立刻由 `grounded` / `velY` 条件分流：

```
Jump_Base (空, 瞬间)
  grounded=true & velY>0  -> Jump_Launch（从地面起跳）
  grounded=false & velY>0 -> Jump_Rise（空中直接进入上升，如蹬墙跳）
  grounded=false & velY<0 -> Jump_Fall（空中直接进入下落，如走出平台边缘）
```

此时状态映射 `Jump -> Jump_Base`，Transition 条件用 `grounded` 和 `velY`。

### 3.6 与 v0.22.16 的兼容性

- 不填 `_transitions`：`HandleStateChanged` 走原逻辑（直接 `PlayState(newState)`）
- 不填 `_velYParam` / `_groundedParam`：不写物理参数
- 不填 `_groundCollider`：不写 grounded
- v0.22.16 的 `_mappings` / `_crossFadeByDefault` / `_facingParam` 语义不变

**v0.22.16 已配置的 Prefab 零改动即可继续工作。**

## 4. 实现步骤

1. 扩展 `SceneObjAnimator.cs`：新增 `TransitionMapping` + `_transitions` + 物理参数字段
2. `HandleStateChanged` 增加转换映射查询分支
3. `Update` 增加物理参数写入
4. 自测：依赖 Unity 引擎运行时，需联调
5. 接入验证：Idle 随机插播、起跑/刹车、跳跃阶段

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `CharaBase.Rigidbody2D` 是 protected，组件访问不到 | 组件自行 `GetComponent<Rigidbody2D>()` 缓存，不改 CharaBase |
| 转换映射的过渡动画被打断后 Animator 卡在过渡态 | 组件下次 `CrossFade` 会强制跳走，不会卡；Animator 内部 Transition 不依赖组件 |
| `grounded` 误判（LayerMask 配错） | `groundLayerMask` 留 0 时不写 grounded；配错时 Animator 会表现出错误阶段，易发现 |
| v0.22.16 配置不兼容 | 所有新字段留空即不生效，零改动兼容 |

**回退**：删除新增字段 + 还原 `HandleStateChanged` / `Update` 到 v0.22.16 即可。

## 6. 测试用例矩阵

| 测试目标 | 前置条件 | 输入 | 期望输出 | 覆盖风险 |
|----------|----------|------|----------|----------|
| 转换边-起跑 | 转换映射 `(Idle,Move)->Move_Start` | `Idle->Move` | 播 Move_Start，播完转 Move_Loop | 转换映射 |
| 转换边-刹车 | 转换映射 `(Move,Idle)->Move_Brake` | `Move->Idle` | 播 Move_Brake，播完转 Idle_Base | 转换映射 |
| 转换未命中 | 无转换映射 | `Idle->Move` | 直接 CrossFade Move_Loop | 兼容性 |
| 起跑被打断 | 起跑播放中 | FSM 切 Chase | 立即 CrossFade 到 Chase，打断起跑 | FSM 权威 |
| velY 透传 | 配 velYParam | 跳跃 | velY 正负切换 | 物理参数 |
| grounded 透传 | 配 groundedParam + Collider + LayerMask | 跳跃落地 | false->true | 物理参数 |
| Device 无物理参数 | Device 无 Rigidbody2D | - | 不写物理参数不报错 | 容错 |
| v0.22.16 兼容 | 旧配置不填新字段 | 切状态 | 行为与 v0.22.16 一致 | 向后兼容 |

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-04 | 实现 v0.22.17 全部能力：新增 `TransitionMapping` + `_transitions` 转换边动画；新增 `_velYParam`/`_groundedParam`/`_groundCollider`/`_groundLayerMask` 物理参数透传；`HandleStateChanged` 增加转换映射查询分支（FSM 权威优先，命中播过渡入口，未命中走 v0.22.16 原逻辑）；`Update` 增加物理参数写入；`PlayAnim` 抽出供转换映射复用。Rigidbody2D 用 `GetComponent<Rigidbody2D>()` 自缓存，不改 CharaBase。无 lint 错误。需 Unity 联调验证。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
