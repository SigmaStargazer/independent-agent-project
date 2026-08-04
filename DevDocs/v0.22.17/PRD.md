# PRD - v0.22.17 SceneObjAnimator 进阶：单状态内多动画流转

> **状态**：待确认
> **对应需求**：`requirements/`（用户口头需求）
> **最后更新**：2026-08-04

---

## 1. 背景与目标

### 1.1 现状

v0.22.16 的 `SceneObjAnimator` 实现了「FSM 状态名 -> Animator 状态名」的一对一跳转：收到 `OnStateChanged(newState)` 后 `CrossFade(newState)`。这能处理 Idle/Move/Dead 等状态的直接切换，但无法处理三类进阶场景：

1. **Idle 随机插播小动作**：FSM 仍是 `Idle`，但动画需要在待机循环中随机播 variation
2. **状态转换边动画**：Idle->Move 时播起跑动作、Move->Idle 时播刹车动作
3. **跳跃内部物理阶段**：起跳/上升/最高点/下落/着地，是同一 FSM 状态内的物理阶段切换，不触发 `OnStateChanged`

### 1.2 问题本质

三类需求对应三种不同的动画内部流转模式：

| 类型 | 触发源 | 当前组件能否处理 |
|------|--------|----------------|
| 随机插播 | Animator 自身时间/随机 | 能（纯 Animator 配置，组件不改） |
| 转换边动画 | `(oldState, newState)` 组合 | 不能（当前只看 newState） |
| 物理阶段 | `velocity.y` / `grounded` 等物理量 | 不能（当前不透传物理参数） |

### 1.3 目标

在不破坏 v0.22.16 现有能力的前提下，扩展 `SceneObjAnimator`：

1. **明确分层**：FSM 状态间的切换仍由组件 `CrossFade` 驱动（不建 Transition）；单个 FSM 状态的动画内部流转由 Animator 自管（可建 Transition）
2. **转换边动画**：组件用 `(oldState, newState)` 查转换映射表，命中则播过渡动画入口
3. **物理参数透传**：组件每帧向 Animator 写 `velY` / `grounded` / `speed`，供 Animator 用 Transition 条件 / Blend Tree 管理跳跃等内部阶段

## 2. 范围

### 2.1 本期包含

- 扩展 `SceneObjAnimator`：新增转换映射表（`(oldState, newState) -> 过渡动画入口`）
- 扩展 `SceneObjAnimator`：新增物理参数透传（`velY` / `grounded` / `speed`）
- 明确 Animator 配置规范：哪些用组件跳转、哪些用 Animator 内部 Transition
- 提供 Idle 随机插播、起跑/刹车、跳跃阶段的 Animator 配置示例

### 2.2 本期不包含

- **不改 FSM**：不新增 Jump/Launch/Fall 等 FSM 状态（跳跃内部阶段是 Animator 管，不是 FSM）
- **不处理 Follow 内部走/停**：Follow 重构见需求池条目 11，不在本期
- **不提供动画素材**
- **不处理多 Animator 层合成**

### 2.3 依赖

- v0.22.16 的 `SceneObjAnimator` 已实现并验收

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 关卡/美术 | 配 Idle 随机小动作 | 纯 Animator 配置，组件不改 |
| 关卡/美术 | 配起跑/刹车过渡 | 组件转换映射表填 `(Idle,Move)->Move_Start`，Animator 内部建 Start->Loop 过渡 |
| 关卡/美术 | 配跳跃阶段 | Animator 用 velY/grounded 条件建子流转，组件只进 Jump 入口 |
| 玩家观察 | 角色待机偶尔动一下 | 自然不违和 |
| 玩家观察 | 跑起步/刹住 | 有过渡动作，不生硬 |
| 玩家观察 | 跳跃全过程 | 起跳->上升->下落->着地流畅 |

## 4. 功能需求

### 4.1 分层原则（定版）

| 层 | 谁负责 | 建 Transition 吗 |
|----|--------|-----------------|
| FSM 状态之间（Idle->Move->Dead） | 组件 `CrossFade` 按名跳转 | **不建** |
| 单个 FSM 状态的动画内部（Idle variation、Run_Start->Run_Loop、跳跃阶段） | Animator 自管 | **可以建** |

组件只负责「进入某 FSM 状态对应的动画入口」，入口之后的内部流转由 Animator 自管。

### 4.2 Idle 随机插播（纯 Animator 配置，组件不改）

Animator 内部：
```
Idle_Base (Loop) ──Transition(exitTime 5~10s + 随机)──> Idle_Var1
Idle_Base ──Transition──> Idle_Var2
Idle_Var1/2 (No Loop) ──Transition(exitTime=1.0)──> Idle_Base
```
组件映射：FSM `"Idle"` -> `"Idle_Base"`（入口）。组件 `CrossFade("Idle_Base")` 进入后，Animator 自动随机流转。组件不感知 variation。

### 4.3 转换边动画（组件增强）

新增转换映射表：`(oldState, newState) -> 过渡动画入口名`。

组件 `HandleStateChanged(obj, oldState, newState)` 逻辑：
1. 先查转换映射 `(oldState, newState)`：
   - 命中 -> `CrossFade` 到过渡动画入口（如 `Move_Start`）；该动画播完后由 Animator 内部 Transition（exitTime）自动跳到 `Move_Loop`
   - 未命中 -> 直接 `CrossFade(newState 入口)`（v0.22.16 行为不变）

Animator 内部：
```
Move_Start (No Loop) ──Transition(exitTime=1.0)──> Move_Loop (Loop)
Move_Loop ──Transition(组件 CrossFade 不会走这条)──> ...
Move_Brake (No Loop) ──Transition(exitTime=1.0)──> Idle_Base (Loop)
```

注意：`Move_Loop -> Move_Brake` 的 Transition **不会被组件触发**（组件直接 CrossFade 到 `Move_Brake`），所以这条 Transition 在组件方案下不生效；它只在纯 Animator 驱动时才有用。组件方案下 Move_Brake 由转换映射驱动。

### 4.4 物理参数透传（组件增强）

组件每帧向 Animator 写以下参数（均可配参数名，留空则不写）：

| 参数 | 来源 | 用途 |
|------|------|------|
| `velY` | `Rigidbody2D.velocity.y` | 上正下负；Animator 据此区分上升/下落 |
| `grounded` | Collider 触地判定（需配置） | true=着地、false=空中 |
| `speed` | `Rigidbody2D.velocity.x` 绝对值（归一化） | 走/跑混合；也解决 Follow 类走/停（见需求池 11） |

跳跃 Animator 配置示例：
```
Jump_Base (入口)
  grounded=true & velY>0  -> Jump_Launch  (No Loop -> 转 Jump_Rise)
  grounded=false & velY>0 -> Jump_Rise    (Loop)
  velY < 0                -> Jump_Fall     (Loop)
  grounded: false->true   -> Jump_Land    (No Loop -> 回 Jump_Base 或 Idle)
```

组件只负责进入 Jump 状态时 `CrossFade("Jump_Base")`，之后由 `velY`/`grounded` 参数驱动 Animator 内部流转。

### 4.5 grounded 判定方式

`grounded` 需要查 Collider 是否触地。提供两种可配方式：

- **方式 A**：组件持有 `Collider2D` 引用，每帧用 `IsTouchingLayers(groundLayerMask)` 判定
- **方式 B**：由外部脚本（如角色控制器）写入 `SceneObjAnimator.IsGrounded` 属性，组件只读

倾向 A（组件自洽），需配置 `groundLayerMask`。

## 5. 非功能需求

- 兼容 v0.22.16 现有配置（不填转换映射 / 不填物理参数名时行为与 v0.22.16 完全一致）
- 文件 UTF-8
- 物理参数透传只在配置了参数名且对象有 Rigidbody2D 时生效，Device 无 RigidBody 自动跳过

## 6. 验收标准

- [ ] 转换映射表：`(Idle,Move)->Move_Start`，Idle->Move 时播起跑动画，播完自动转 Move_Loop
- [ ] 转换映射表：`(Move,Idle)->Move_Brake`，Move->Idle 时播刹车动画，播完自动转 Idle_Base
- [ ] 未配转换映射的状态切换，行为与 v0.22.16 一致
- [ ] `velY` 参数正确写入（跳跃时正负切换）
- [ ] `grounded` 参数正确写入（离地 false、着地 true）
- [ ] `speed` 参数正确写入（移动时非零、停止时零）
- [ ] 跳跃全过程：起跳->上升->下落->着地，由 Animator 内部 Transition 驱动，组件不切状态
- [ ] Device 无 Rigidbody2D 时不写物理参数、不报错
- [ ] v0.22.16 现有配置零改动即可工作

## 7. 待确认问题

- [ ] **`grounded` 判定方式**：组件自持 Collider + LayerMask（方式 A），还是外部写入（方式 B）？倾向 A。
- [ ] **`speed` 归一化**：除以 `moveSpeed` 还是可配 `maxSpeed`？倾向可配 `maxSpeed`，默认用 `moveSpeed`。
- [ ] **转换映射打断**：起跑动画播到一半被 FSM 切走（如进 Chase），是否直接 CrossFade 打断？倾向是（FSM 权威优先）。
- [ ] **跳跃入口命名**：FSM 状态名是否就叫 `Jump`？当前代码无 Jump 状态，需确认是否本期新增 Jump FSM 状态，还是仅做组件能力（Jump 状态由后续版本加）。倾向：**仅做组件能力**，Jump FSM 状态另起版本。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
