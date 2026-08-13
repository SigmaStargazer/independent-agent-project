# SceneObjAnimator 配置指南

> 面向策划/美术。教你如何给角色和装置配动画。
> 对应组件：`SceneObjAnimator.cs`（v0.22.16 基础 + v0.22.17 进阶）

---

## 一、快速上手（3 步配通一个角色）

以 HumanPlayer 为例，只需 3 步：

### 第 1 步：建 Animator Controller

在 Project 窗口右键 -> Create -> Animator Controller，命名如 `AC_HumanPlayer`。双击打开，建以下 State：

```
Idle    (Motion: 待机动画, Loop 勾选)
Move    (Motion: 走动动画, Loop 勾选)
Dead    (Motion: 死亡动画, Loop 不勾)
```

**不要连线**（State 之间不画 Transition）。State 名字必须和 FSM 状态名一致（区分大小写）。

### 第 2 步：挂组件 + 拖引用

在角色 Prefab 上（或带 Animator 的子节点上）：

1. Add Component -> `SceneObjAnimator`
2. 拖入以下引用：

| 字段 | 拖什么 | 说明 |
|------|--------|------|
| `_target` | 角色身上的 `SceneObjBase` 组件（如 `HumanPlayer`） | 一般就是自身或父节点 |
| `_animator` | 角色身上的 `Animator` 组件 | 可以在子节点上 |

### 第 3 步：把 Animator Controller 拖给 Animator

在 Animator 组件的 Controller 字段拖入 `AC_HumanPlayer`。

**完成**。运行游戏，角色 Idle/Move 切换时动画自动跟随。

---

## 二、所有配置项一览

配置项按 Inspector 分区排列，与 `PlayerAnimator` 组件 Inspector 自上而下的顺序一致：

### 基础配置

| 字段 | 类型 | 作用 | 必填 | 默认 |
|------|------|------|------|------|
| `_target` | SceneObjBase | 订阅哪个对象的状态 | 是 | - |
| `_animator` | Animator | 驱动哪个 Animator | 是 | - |
| `_crossFadeDuration` | float | 过渡时间（秒），仅 crossFade=true 时生效 | 否 | 0.1 |
| `_crossFadeByDefault` | bool | 状态切换默认用过渡还是瞬切 | 否 | false（瞬切） |
| `_actionLayerIndex` | int | Action Layer 索引（Base Layer = 0）。美术调整 Animator 层级后这里同步改 | 否 | 1 |

### 参数透传（每帧向 Animator 写参数）

| 字段 | 类型 | 作用 | 必填 | 默认 |
|------|------|------|------|------|
| `_facingParam` | string | 朝向参数名（Float），角色用，装置留空 | 否 | "dirX" |
| `_velYParam` | string | 垂直速度参数名（Float），跳跃用 | 否 | ""（不写） |
| `_groundedParam` | string | 触地参数名（Bool），跳跃用 | 否 | ""（不写） |
| `_groundCollider` | Collider2D | 触地判定用的 Collider | 否 | null |
| `_groundLayerMask` | LayerMask | 地面 Layer | 否 | 0 |

### 状态映射

| 字段 | 类型 | 作用 | 必填 | 默认 |
|------|------|------|------|------|
| `_mappings` | List | 状态名映射表（见 §三） | 否 | 空 |

### 转换边动画

| 字段 | 类型 | 作用 | 必填 | 默认 |
|------|------|------|------|------|
| `_transitions` | List | 转换边动画映射表（见 §四） | 否 | 空 |

### 动作动画映射（仅 PlayerAnimator）

| 字段 | 类型 | 作用 | 必填 | 默认 |
|------|------|------|------|------|
| `_actionMappings` | List | 交互动画标签 -> 上层 Animator 状态名映射（见 §十一） | 否 | 空 |

**简单口诀**：必填只有 `_target` 和 `_animator`，其余全部有默认值，留空即不生效。装置挂 `SceneObjAnimator` 看不到动作动画映射分区；Player 角色（`PlayerAnimator`）多出最后一个分区。

---

## 三、状态映射表 `_mappings`

### 什么时候需要配

**默认**：FSM 状态名 = Animator State 名，不需要配映射表。

**需要配的场景**：FSM 状态名和 Animator State 名不一致，或需要标记某些状态跳过动画。

### 配法

| 字段 | 作用 | 示例 |
|------|------|------|
| `fsmState` | FSM 状态名 | `"GreenLight"` |
| `animState` | Animator State 名（留空则同名） | `"Green"` |
| `isOneShot` | 是否一次性（仅记录，实际由 Clip 的 Loop 决定） | false |
| `crossFade` | 是否用过渡（覆盖 `_crossFadeByDefault`） | false |
| `skipAnimation` | 是否跳过该状态（不驱动 Animator） | false |

### 常见用例

**改名**：FSM 叫 `GreenLight`，Animator 里叫 `Green`：

| fsmState | animState |
|----------|-----------|
| `GreenLight` | `Green` |

**跳过状态**：`Hidden` 状态不需要动画（Renderer 被关看不见）：

| fsmState | skipAnimation |
|----------|---------------|
| `Hidden` | true |

**单个状态用过渡**：死亡动画需要淡入：

| fsmState | crossFade |
|----------|-----------|
| `Dead` | true |

---

## 四、转换边动画 `_transitions`（v0.22.17）

### 什么时候需要配

状态切换时需要**先播一段过渡动作**再进入目标循环。例如：

- Idle -> Move：先播起跑动作，再进入走动循环
- Move -> Idle：先播刹车动作，再进入待机循环

### 配法

| 字段 | 作用 | 示例 |
|------|------|------|
| `fromState` | 旧 FSM 状态名 | `"Idle"` |
| `toState` | 新 FSM 状态名 | `"Move"` |
| `animState` | 过渡动画入口 State 名 | `"Move_Start"` |
| `crossFade` | 是否用过渡进入 | true |

### 完整示例：起跑/刹车

**Animator Controller 里的 State**：

```
Idle_Base   (Loop)      待机循环
Move_Start  (No Loop)   起跑动作
Move_Loop   (Loop)      走动循环
Move_Brake  (No Loop)   刹车动作
```

**Animator 里需要建的 Transition**（只建这两条）：

| From | To | HasExitTime | ExitTime | Duration |
|------|----|-------------|----------|----------|
| Move_Start | Move_Loop | true | 1.0 | 0 |
| Move_Brake | Idle_Base | true | 1.0 | 0 |

**组件配置**：

状态映射 `_mappings`：

| fsmState | animState | 说明 |
|----------|-----------|------|
| `Idle` | `Idle_Base` | |
| `Move` | `Move_Loop` | 直接进 Move 时走循环（如从 Chase 回 Move） |

转换映射 `_transitions`：

| fromState | toState | animState |
|-----------|---------|-----------|
| `Idle` | `Move` | `Move_Start` |
| `Move` | `Idle` | `Move_Brake` |

**效果**：Idle->Move 播起跑再转走动；Move->Idle 播刹车再转待机；其他路径（如 Chase->Move）直接进走动循环。

### 重要：循环动画之间不要连线（反直觉）

这是本组件最容易踩的坑。组件用 `Animator.CrossFade`/`Play` **直接跳到目标 State**，会绕过 Animator 的大多数 Transition。所以关于哪些 Transition 该连、哪些不该连，必须记住一条规则：

> **一次性动画（No Loop）播完要接下一个状态时，必须手动画 `HasExitTime` 的 Transition。循环动画（Loop）之间不要连线。**

为什么？因为组件只负责「进入入口」，之后交给 Animator 自管：

- 一次性动画播完，如果没有 `HasExitTime` Transition 指向下一个状态，它会**停在最后一帧**，不会自动进循环。
- 循环动画本来就在无限循环，它的退出只能由组件在状态切换时再次 `Play`/`CrossFade` 触发。如果你给循环动画画了一条指向别的状态的 Transition，它会在**自己循环到满足条件时自动溜过去**，结果就是组件还没切状态，动画自己先跑了。

#### 案例：起跑 -> 走动循环 -> 刹车

这是典型三段式配置。Animator 里有三个 State：

```
Run_Start  (No Loop)   起跑
Run_Loop   (Loop)      走动循环
Run_End    (No Loop)   刹车
```

**该连的 Transition**（只有两条，都是一次性动画指向下一个）：

| From | To | HasExitTime | ExitTime | 说明 |
|------|----|-------------|----------|------|
| Run_Start | Run_Loop | true | 1.0 | 起跑播完进循环 |
| Run_End | Idle | true | 1.0 | 刹车播完回待机 |

**绝对不要连的 Transition**：

| From | To | 为什么不能连 |
|------|----|-------------|
| Run_Loop | Run_End | Run_Loop 是循环动画，连了之后它播完一轮就自动溜进 Run_End，组件还没切状态呢，刹车动画就自己跑了 |
| Run_Loop | Idle | 同上，循环动画不该有出口 |

**组件配置**（`_transitions` + `_mappings`）：

状态映射 `_mappings`：

| fsmState | animState | 说明 |
|----------|-----------|------|
| `Idle` | `Idle` | |
| `Move` | `Run_Loop` | 直接进 Move 时走循环（如从其他状态回 Move） |

转换映射 `_transitions`：

| fromState | toState | animState | 说明 |
|-----------|---------|-----------|------|
| `Idle` | `Move` | `Run_Start` | 起跑 |
| `Move` | `Idle` | `Run_End` | 刹车 |

**运行效果**：

1. Idle -> Move：组件跳到 `Run_Start`，播完靠 Animator 的 `HasExitTime` Transition 进 `Run_Loop`，开始循环。
2. Move 持续：`Run_Loop` 一直循环，因为没有出口 Transition，不会自己溜走。
3. Move -> Idle：组件跳到 `Run_End`，播完靠 `HasExitTime` Transition 回 `Idle`。

**踩坑表现**：如果你把 `Run_Loop` 连到了 `Run_End`，会发现角色跑一下就自动刹车了--因为 `Run_Loop` 循环一轮后满足了 ExitTime 条件，自己溜进了 `Run_End`，而此时 FSM 还在 Move 状态。

---

## 五、朝向参数 `_facingParam`

### 作用

角色面朝方向写入 Animator 的 Float 参数，供 Blend Tree 或动画内左右翻转使用。

### 配法

- `_facingParam` 填 `"dirX"`（或你在 Animator 里建的参数名）
- Animator Parameters 里建一个 Float 参数 `dirX`
- 组件每帧写入：朝右 `1`，朝左 `-1`
- **只对角色（CharaBase）生效**，装置自动跳过

### 不需要时

`_facingParam` 留空字符串 `""` 即不写。

---

## 六、物理参数（跳跃用，v0.22.17）

### 作用

跳跃时区分起跳/上升/下落/着地等阶段。组件每帧写 `velY`（垂直速度）和 `grounded`（是否触地），Animator 用参数条件自动切换动画。

### 配法

1. 组件 Inspector 填：

| 字段 | 值 |
|------|-----|
| `_velYParam` | `"velY"` |
| `_groundedParam` | `"grounded"` |
| `_groundCollider` | 角色脚部的 Collider2D |
| `_groundLayerMask` | 勾选地面 Layer（如 `Ground`） |

2. Animator Parameters 建两个参数：`velY`（Float）、`grounded`（Bool）

3. Animator State 布局：

```
Jump_Launch (No Loop)  起跳
  └ HasExitTime ─> Jump_Rise (Loop)  上升
                    └ velY < 0 ─> Jump_Apex (No Loop)  过渡
                                    └ HasExitTime ─> Jump_Fall (Loop)  下落
                                                      └ grounded == true ─> Jump_Land (No Loop)  着地
                                                                          └ HasExitTime ─> Idle  回待机
```

4. 状态映射 `_mappings`：

| fsmState | animState |
|----------|-----------|
| `Jump` | `Jump_Launch` |

**不需要跳跃时**：`_velYParam` / `_groundedParam` / `_groundCollider` 全部留空/null，不写物理参数。

---

## 七、Idle 随机插播小动作

纯 Animator 配置，组件不需要改。

**Animator State 布局**：

```
Idle_Base (Loop)
  └ Transition ─> Idle_Var1 (No Loop)
  └ Transition ─> Idle_Var2 (No Loop)
Idle_Var1 ─ HasExitTime ─> Idle_Base
Idle_Var2 ─ HasExitTime ─> Idle_Base
```

**Transition 配置**：

| From | To | Conditions | HasExitTime | ExitTime |
|------|----|-----------|-------------|----------|
| Idle_Base | Idle_Var1 | 无 | true | 5.0（5 秒后触发） |
| Idle_Base | Idle_Var2 | 无 | true | 8.0（8 秒后触发） |
| Idle_Var1 | Idle_Base | 无 | true | 1.0 |
| Idle_Var2 | Idle_Base | 无 | true | 1.0 |

随机感可通过建多条 Transition + 不同 ExitTime 实现。组件映射 `Idle -> Idle_Base` 即可，之后 Animator 自动流转。

---

## 八、Sprite 动画注意事项

### 分辨率统一

同一角色的所有动画帧建议用**统一画布尺寸**（如 256×256）。不同分辨率会导致切换时尺寸跳变。

### Pivot 统一

所有 Sprite 的 pivot 设在同一位置（通常底部中心 `Bottom Center`）。pivot 不一致会导致切换时角色瞬移。

### crossFade 与重影

逐帧 Sprite 动画用 `CrossFade`（过渡）会同时渲染两帧产生重影。建议：

- `_crossFadeByDefault = false`（默认瞬切）
- 仅个别需要淡入淡出的状态在 `_mappings` 里单独设 `crossFade = true`

### Animator State 不嵌套

所有 State 建在 Layer 根层级，**不要用 Sub-State Machine**（子状态机）。组件按状态名跳转，嵌套需要写全路径，本期不支持。

---

## 九、各角色/装置配置速查

### HumanPlayer

挂 `PlayerAnimator`（继承自 `SceneObjAnimator`，多出动作动画映射）。

| 配置项 | 值 |
|--------|-----|
| `_target` | 自身 HumanPlayer |
| `_animator` | 子节点 Animator |
| `_crossFadeDuration` | 0.1 |
| `_crossFadeByDefault` | false（Sprite 瞬切） |
| `_actionLayerIndex` | 1（美术调整 Animator 层级后同步改） |
| `_facingParam` | "dirX" |
| `_velYParam` | "velY"（需跳跃时填，否则留空） |
| `_groundedParam` | "grounded"（需跳跃时填，否则留空） |
| `_groundCollider` | GroundCheck 子物体的 Collider2D（需跳跃时填） |
| `_groundLayerMask` | Ground（需跳跃时填） |
| `_mappings` | Idle->Idle_Base, Move->Move_Loop, Dead->Dead, Hidden->skipAnimation=true |
| `_transitions` | (Idle,Move)->Move_Start, (Move,Idle)->Move_Brake（如需起跑/刹车） |
| `_actionMappings` | Interact->Interact, Backstab->Backstab, Trade->Trade, Steal->Steal, Select->Select, TextInput->TextInput（不需要的不填） |

### EnemyBase

挂 `SceneObjAnimator`（基类，无动作动画映射）。

| 配置项 | 值 |
|--------|-----|
| `_target` | 自身 EnemyBase |
| `_animator` | 子节点 Animator |
| `_crossFadeDuration` | 0.1 |
| `_crossFadeByDefault` | false |
| `_actionLayerIndex` | 1（默认即可，装置/敌人一般不用 Action Layer） |
| `_facingParam` | "dirX" |
| `_mappings` | Idle/Move/Dead/Chase/Searching/Stunned/Alerted/Investigate/Inspect 各建同名 State |

### SignalLight

挂 `SceneObjAnimator`。

| 配置项 | 值 |
|--------|-----|
| `_target` | 自身 SignalLight |
| `_animator` | 子节点 Animator |
| `_crossFadeDuration` | 0.1 |
| `_crossFadeByDefault` | false |
| `_actionLayerIndex` | 1（默认即可） |
| `_facingParam` | ""（装置无朝向） |
| `_mappings` | RedLight, GreenLight 同名即可 |

### Safebox

挂 `SceneObjAnimator`。

| 配置项 | 值 |
|--------|-----|
| `_target` | 自身 Safebox |
| `_animator` | 子节点 Animator |
| `_crossFadeDuration` | 0.1 |
| `_crossFadeByDefault` | false |
| `_actionLayerIndex` | 1（默认即可） |
| `_facingParam` | "" |
| `_mappings` | Open, Close 同名即可 |

---

## 十、常见问题

**Q：动画不播？**

检查顺序：
1. `_animator` 是否拖了引用
2. Animator 组件的 Controller 是否拖了 Animator Controller
3. Animator State 名是否和 FSM 状态名一致（或映射表是否配对）
4. 看 Console 有无 Animator 的 Warning（如 `State does not exist`）

**Q：切换动画时有重影？**

Sprite 动画用了 crossFade。把 `_crossFadeByDefault` 设为 `false`，或在映射表里把该状态的 `crossFade` 设为 `false`。

**Q：角色朝向不对？**

检查 `_facingParam` 是否填了参数名，Animator Parameters 里是否建了对应的 Float 参数。组件只对 CharaBase 写朝向，装置不写。

**Q：起跑动画不播，直接进走动？**

检查 `_transitions` 是否配了 `(Idle, Move) -> Move_Start`。如果没配转换映射，组件会直接跳到 Move 对应的状态映射（如 `Move_Loop`），跳过起跑。

**Q：跳跃动画卡在上升不下落？**

检查 `_velYParam` 是否填了，Animator 里是否建了 `velY < 0` 的 Transition 条件。组件只负责写参数，切换由 Animator 条件驱动。

**Q：起跑/走动循环/刹车连了线，角色跑一下就自己刹车了？**

循环动画（Loop）之间不能连线。`Run_Loop` 如果连了一条指向 `Run_End` 的 Transition，它循环一轮后满足 ExitTime 条件就会自己溜进 `Run_End`，而此时 FSM 还在 Move 状态。只有一次性动画（No Loop）播完需要接下一个状态时，才手动画 `HasExitTime` 的 Transition。详见 §四「重要：循环动画之间不要连线」。

**Q：交互日志正常（success:True/False），但 Interact 动作动画就是不播？**

代码链路（`DoInteract → PlayOneShotByTag → PlayOneShot`）没报错、日志也打出来了，说明组件已经在 Action Layer 上调了播放，问题基本都出在 **Animator 的 Action Layer 配置**上。按隐蔽程度从高到低排查：

1. **Action Layer 的 Weight = 0**（最隐蔽，无任何报错）：这层权重 0 就完全不参与混合，动画播了也被抹掉。到 Animator 窗口左侧 Layers 面板，把 Action 层的 **Weight 改成 1**。
2. **Action Layer 的 Default State 不是 `Empty`**：如果默认状态是某个动作状态（如 `Interact`），进场景时上层就停在那，交互时对同名状态重播无效，看起来"没播"。右键 `Empty` → Set as Layer Default State。
3. **`_actionLayerIndex` 和实际 Layer 索引不一致**：组件默认在层 1 找状态，如果 Action Layer 不在第 2 层，`Play` 静默失败。确认 Animator 里 Action 层在第几层，把 Inspector 的 `_actionLayerIndex` 改成对应索引。
4. **动作状态没放在 Action Layer**：动作状态（Interact 等）必须建在 Action Layer 上，不是 Base Layer。Base Layer 只放状态动画。

详见 §11.3「Action Layer 三项必配」。

**Q：动作动画能播，但角色身上出现动作状态的静止帧或残影？**

动作动画播完没回 `Empty`。检查每个动作状态是否都连了 `HasExitTime` 的 Transition 回 `Empty`（ExitTime 1.0）。如果某动作状态没连，播完会停在最后一帧，把底层透不出来。

---

*本指南随 `SceneObjAnimator` 版本更新。对应版本：v0.22.16（基础）+ v0.22.17（进阶）+ v0.22.18（动作动画）。*

---

## 十一、动作动画与 PlayerAnimator（v0.22.18）

### 11.1 什么是动作动画

除了 FSM 状态动画（Idle/Move/Dead 等），角色还有**一次性动作动画**：按按钮、背刺、交易、盗窃、选择、文本输入。这些动作不对应 FSM 状态（角色 FSM 全程保持 Idle），由上层 Action Layer 独立播放，播完自动回空。

### 11.2 两个组件的区别

| 组件 | 挂谁 | 作用 |
|------|------|------|
| `SceneObjAnimator` | 装置 + 角色 | 底层 FSM 状态动画驱动（v0.22.16/v0.22.17 已有） |
| `PlayerAnimator` | **仅 Player 角色** | 继承 `SceneObjAnimator` 全部功能 + 动作动画映射 |

**装置**：挂 `SceneObjAnimator`，不需要动作动画。
**Player 角色**：把 `SceneObjAnimator` 换成 `PlayerAnimator`（继承的，原有配置不变）。

### 11.3 Animator Controller 怎么配

需要**两个 Layer**：

**Base Layer（层 0）**：和原来一样，放 FSM 状态动画（Idle/Move/Dead/Hidden 等）。

**Action Layer（层 1）**：放一次性动作动画。需要建一个名为 `Empty` 的空状态作为默认，再加上各动作状态：

> 注意：组件的 `_actionLayerIndex` 默认是 `1`，对应 Animator 里第二个 Layer。如果美术调整了 Layer 顺序（比如把 Action Layer 放到第三层），记得在 Inspector 里把 `_actionLayerIndex` 改成对应索引。Base Layer 固定是 `0`。

```
Empty          (默认空状态，透出底层)
Interact       (No Loop, 播完回 Empty)
Backstab       (No Loop, 播完回 Empty)
Trade          (No Loop, 播完回 Empty)
Steal          (No Loop, 播完回 Empty)
Select         (No Loop, 播完回 Empty)
TextInput      (No Loop, 播完回 Empty)
```

每个动作状态建一条 `HasExitTime` 的 Transition 回 `Empty`：

| From | To | HasExitTime | ExitTime |
|------|----|-------------|----------|
| Interact | Empty | true | 1.0 |
| Backstab | Empty | true | 1.0 |
| Trade | Empty | true | 1.0 |
| ... | ... | ... | ... |

**Action Layer 三项必配（缺一项动作动画就不播或行为异常）**：

| 项 | 值 | 漏配后果 |
|----|-----|----------|
| **Default State** | `Empty` | 进场景时上层就停在某个动作状态，交互时同名状态重播无效，看起来"没播" |
| **Weight** | **1.0** | 权重 0 时这层完全被抹掉，代码播了但渲染看不到（最隐蔽，无报错） |
| **Layer 索引** | 与组件 `_actionLayerIndex` 一致（默认 1） | 代码在错误的层上找状态，静默失败 |

Action Layer 的 Mask 视需求（推荐全身覆盖）。

> **自检提示（v0.22.18）**：Player 角色挂 `PlayerAnimator` 且配置了 Action Mappings 时，运行会自动检查 Action Layer 配置。上述三项任一项配错，Console 会打黄色警告（如 `Action Layer 权重为 0`、`默认状态是 "Interact"，应为 "Empty"`、`索引超出层数`），按警告提示修即可，不用靠猜。

### 11.4 组件配置

Player 角色上把 `SceneObjAnimator` 换成 `PlayerAnimator`，原有配置（基础配置 + 参数透传 + 状态映射 + 转换边）全部不变。多出一个 **Action Mappings** 列表：

| 字段 | 作用 | 示例 |
|------|------|------|
| `tag` | 交互动画标签（枚举） | `Interact` |
| `animState` | 上层 Action Layer 的 Animator State 名 | `"Interact"` |
| `crossFade` | 是否用过渡进入 | true |

### 11.5 完整配置示例

以 HumanPlayer 为例：

**Action Mappings**：

| tag | animState | crossFade |
|-----|-----------|-----------|
| Interact | Interact | true |
| Backstab | Backstab | true |
| Trade | Trade | true |
| Steal | Steal | true |
| Select | Select | true |
| TextInput | TextInput | true |

不需要的标签不填即可，运行时查不到就跳过。

### 11.6 动画标签怎么来的

动画标签由被交互物体在 `Interact()` / `Select()` / `TextInput()` 的返回值携带。玩家不需要关心，配好映射表就行：

| 交互场景 | 被交互物体返回的标签 | 播放的动画 |
|---------|-------------------|-----------|
| 按拉杆（成功） | `Interact` | Interact |
| 按拉杆（失败） | `None` | 不播 |
| 背刺敌人 | `Backstab` | Backstab |
| 商人正面交易 | `Trade` | Trade |
| 商人背面盗窃 | `Steal` | Steal |
| 信箱选信 | `Select` | Select |
| 保险箱输密码 | `TextInput` | TextInput |
| 进柜子 | `None` | 不播（靠 FSM 切 Hidden 驱动底层） |

> **约定（v0.22.18）**：所有可交互物体的 `Interact()` / `Select()` / `TextInput()` 返回时，**成功（success=true）返回具体的动画标签（默认 `Interact`），失败（success=false）返回 `None`**，失败不播动作动画。这个约定在基类（`DeviceBase` / `CharaBase`）默认实现里已生效；各实现类 override 时遵循同样规则，需要自定义动画标签时（如背刺 `Backstab`、盗窃 `Steal`）在成功分支返回对应标签即可。

### 11.7 状态切换时动作动画被打断

FSM 状态切换时（如交互中被击杀），Action Layer 会被自动清空（回到 `Empty`），底层新状态动画接管。这是设计预期，保证抢占正确。
