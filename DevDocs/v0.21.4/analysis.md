# v0.21.4 分析文档 — 范围物体位置语义与浮板上岸失败

> **状态**：待讨论  
> **问题来源**：用户口述 + 终端日志片段 `2026-06-20_18-30-19.log` / 当前终端快照  
> **最后更新**：2026-06-21  
> **阶段说明**：本文只做原因分析与方案候选，不作为 PRD；讨论确认后再进入 PRD。

---

## 1. 问题概述

用户指令：让 AI Player 借助浮板通过陷阱。

实际现象：AI Player 能够等待浮板、移动到浮板上，但随后使用了 `FollowTarget` 跟随浮板；浮板到达对岸后，AI Player 没有主动离开浮板上岸，而是持续停留在 `FollowTarget` 状态。

修正后的核心判断：

- `FollowTarget` 的出现不是“过陷阱方案缺少 FollowTarget 结束条件”的证据，而是 AI Player / Agent **选错工具**的证据；
- 借助浮板渡过陷阱，本质上应是一个可规划、可确认、可完成的动作序列：等待浮板到合适位置 → 移动到浮板上 → 等待浮板到对岸 → 移动上岸；
- AI Player 之所以倾向于用 `FollowTarget`，很可能是因为现有 ActionSequence condition 难以表达“浮板边界与陷阱边界/岸边边界的关系”；
- AI Player 在自然语言环境信息中看到的是范围物体的左右边界相对位置；
- 但动作序列 `condition` 中只能使用 `objects[i].Position`，该字段实际是物体 `transform.position`，通常更接近物体中心或根节点位置；
- 对浮板、陷阱这类范围物体而言，“中心点位置”无法表达“左边缘/右边缘是否已经越过陷阱边界/岸边边界”；
- 因此 Agent 在生成动作序列条件时，基于它看到的范围边界信息做推理，却只能写中心点条件，导致 ActionSequence 难以稳定覆盖完整过陷阱流程，进而诱发错误工具选择。

---

## 2. 日志证据

当前可见的终端快照保留了目标日志片段。关键事实如下。

### 2.1 已完成的动作序列

日志中反复出现：

```text
# 进行中的动作序列:
## 动作序列状态: Completed
## 动作序列详情:
0.动作名:Wait
结束条件:objects[3].State == "Idle" && objects[3].Position.x < 7
动作状态:Done

1.动作名:Move
结束条件:displacement >= 4.5
动作状态:Done

# 进行中的动作:
动作名:FollowTarget
结束条件:无
动作状态:Doing
跟随目标:3. 自动移动的平台
```

含义：

1. Agent 先等待 `objects[3]` 自动移动的平台满足某个状态/位置条件；
2. 然后向右移动 4.5m，应该是移动到浮板上；
3. 动作序列到此就结束了，并没有继续规划“等待浮板到对岸”和“移动上岸”；
4. 当前仍在执行独立的持续动作 `FollowTarget`，目标是 `3. 自动移动的平台`；
5. 对“通过浮板渡过陷阱”这个任务而言，`FollowTarget` 本身就是错误工具选择：它是持续跟随目标，而不是可验证、可完成的过障动作序列。

### 2.2 浮板已经多次到达陷阱右侧附近

日志中，陷阱和浮板均以范围方式渲染：

```text
2. 陷阱
范围: 从你的 left方向6.181079m 到你的 right方向1.40548m 范围

3. 自动移动的平台
范围: 从你的 left方向0.4215498m 到你的 right方向1.365951m 范围
```

这段环境信息说明：

- AI Player 当时仍在浮板附近；
- 陷阱的右边界在 AI Player 右侧约 1.405m；
- 浮板覆盖 AI Player 左侧约 0.422m 到右侧约 1.366m；
- 也就是说，AI Player 看到的是“范围边界相对自身的位置”，而不是对象中心点相对自身的位置。

更早/更晚片段显示陷阱范围相对 AI Player 的左右边界不断变化，例如：

```text
陷阱: 从你的 left方向0.4310789m 到你的 right方向7.15548m 范围
平台: 从你的 left方向0.4215491m 到你的 right方向1.365951m 范围
```

以及：

```text
陷阱: 从你的 left方向6.181079m 到你的 right方向1.40548m 范围
平台: 从你的 left方向0.4215498m 到你的 right方向1.365951m 范围
```

这表示 AI Player 已随浮板横向移动很长距离；但系统没有一个明确、可写入动作序列条件的“浮板右边缘是否接近/越过陷阱右边缘”的字段。

---

## 3. 当前实现梳理

### 3.1 环境渲染：范围物体使用 Collider bounds 左右边界

`SceneObjInfoMapper` 中：

- 如果 `sceneObj.UseRangeDirection && sceneObj.RangeCollider != null`：
  - 取 `sceneObj.RangeCollider.bounds.min.x` 作为左边界；
  - 取 `sceneObj.RangeCollider.bounds.max.x` 作为右边界；
  - 分别计算左右边界相对于 AI Player 的方向和距离；
- 否则使用 `sceneObj.transform.position.x` 计算对象方位和距离。

这意味着自然语言环境信息对范围物体是“边界语义”。

### 3.2 动作序列条件：表达式视图只有中心 Position

`ConditionContext.RefreshViews()` 会把 `SceneObjBase` 转成 `SceneObjExprView`：

- `myself`：`ExprViewFactory.From(MyselfSrc)`；
- `objects`：`ObjectsSrc.Select(ExprViewFactory.From).ToList()`。

`ExprViewFactory.From(sceneObj)` 目前只暴露：

- `Position = new Vector2(sceneObj.transform.position.x, sceneObj.transform.position.y)`；
- `Velocity = rb.velocity` 或 `Vector2.zero`；
- `State = sceneObj.StateName`。

因此动作序列表达式中的：

```text
objects[3].Position.x
```

实际是 `objects[3].transform.position.x`，不是环境渲染中平台范围的左/右边界。

### 3.3 Python 工具侧的 condition schema 也只允许 Position / Velocity / State

`Src/PythonServer/agent_framwork/tools/action_sequence_model/core/types.py` 中：

```text
objects.members = {"Position", "Velocity", "State"}
```

因此即使 Unity 侧未来有字段，当前 Python/Pydantic 校验也不允许 Agent 写：

```text
objects[3].LeftPosition.x
objects[3].RightPosition.x
```

目前 Agent 能写的对象位置字段只有 `Position`。

### 3.4 FollowTarget 在本场景中是错误工具选择

`FollowTarget` 当前是持续动作：

- 开始后设置 `TargetFollowing`、`FollowMinDistance`、`FollowMaxDistance`；
- `OnFollowFixedUpdate()` 每帧根据 `TargetFollowing.transform.position.x - transform.position.x` 调整自身速度；
- 距离在范围内则速度置 0；
- 只有目标消失或撞击等错误条件才会结束。

但这不意味着本问题应通过“给 FollowTarget 增加到岸结束条件”来解决。

对“借助浮板渡过陷阱”这个任务来说，正确工具应是 `plan_action_sequence_cmd` / `start_action_sequence_cmd` 这类可规划动作序列，而不是 `follow_target_cmd`：

1. 等待浮板到当前岸边可上板的位置；
2. 移动到浮板上；
3. 等待浮板移动到对岸可上岸的位置；
4. 向安全方向移动上岸。

因此，`FollowTarget` 出现在日志里应被视为“Agent 因 ActionSequence 表达能力不足或工具描述约束不足而选错工具”的结果。v0.21.4 的修复重点应是让 ActionSequence 能自然表达浮板/陷阱范围边界条件，并在工具描述中明确此类过障任务不应使用 FollowTarget。

---

## 4. 根因判断

### 4.1 直接根因

AI Player 不上岸的直接原因是：

1. Agent 没有把“通过浮板过陷阱”规划成完整 ActionSequence，而是在上板后改用了 `FollowTarget`；
2. `FollowTarget` 是持续跟随工具，不适合表达“等待浮板到对岸后上岸”的可完成流程；
3. Agent 之所以没有稳定使用完整 ActionSequence，一个重要诱因是：现有 condition 能表达的 `objects[i].Position` 与 Agent 实际看到的范围边界信息不一致，导致 Agent 难以写出可靠的到岸判定。

### 4.2 更深层根因：同一物体的“观察语义”和“控制语义”不一致

对非范围物体：

- 观察：方位/距离基于 `transform.position.x`；
- 条件：`Position.x` 也基于 `transform.position.x`；
- 二者语义基本一致。

对范围物体：

- 观察：显示左右边界相对自己的方向与距离；
- 条件：只能使用中心/根节点 `Position.x`；
- 二者语义不一致。

这会给 Agent 造成“它看到的不是它能判断的，能判断的不是它看到的”的断裂。

### 4.3 为什么在浮板/陷阱场景特别明显

过陷阱的关键不是“平台中心在哪里”，而是范围边界关系：

- 平台右边缘是否已经接近陷阱右边缘；
- 平台右边缘是否已经越过陷阱右边缘并接近对岸；
- AI Player 自身是否仍在平台范围内；
- 向右移动时是否会离开平台落到安全地面，而不是落入陷阱。

如果只看中心点，平台宽度、陷阱宽度和实际可站立边界都被隐藏了。对于宽度不小、碰撞体/根节点不一定在几何中心的物体，中心点判断很容易失真。

---

## 5. 触发场景枚举与期望行为

| 场景 | 物体类型 | Agent 看到的信息 | condition 当前能用 | 期望行为 | 当前风险 |
|---|---|---|---|---|---|
| 普通门/按钮/检查点 | 非范围物体 | 方位 + 距离，基于中心点 | `Position` | 可用中心点粗略判断位置 | 风险较低 |
| 人类玩家/NPC | 非范围物体/角色 | 方位 + 距离 + 朝向 | `Position` / `State` | 可追踪角色中心位置 | 风险较低 |
| 墙 | 可能是范围物体，也可能非范围 | 若开启范围，则看到左右边界 | 只能用 `Position` | 判断墙边界/绕过位置 | 若墙很宽，中心点误导 |
| 陷阱 | 范围物体 | 左边界/右边界相对自己 | 只能用 `Position` | 判断是否越过陷阱边界 | 高风险：中心点不能代表安全边界 |
| 自动移动平台/浮板 | 范围物体 + 移动物体 | 左边界/右边界相对自己 + 速度/状态 | 只能用 `Position` / `Velocity` / `State` | 判断平台边缘与陷阱/岸边边缘关系 | 高风险：无法准确决定何时上岸 |
| 浮板承载 AI Player 期间 | 范围物体 + AI Player 位于范围内部 | 平台范围通常横跨 AI Player 左右两侧 | 只能用平台中心与自身中心 | 在 ActionSequence 中等待到岸并向安全侧移动 | 容易改用 FollowTarget，或过早/过晚下平台 |
| 多个范围物体相互比较 | 范围物体 A/B | 各自左右边界相对自己 | 只能比较中心点 | 比较边界距离，例如平台右边缘与陷阱右边缘 | 当前无法自然表达 |

---

## 6. 方案候选

### 6.1 方案 A：给 condition 增加范围边界字段，保留 Position

新增字段：

- `LeftPosition: Vector2`
- `RightPosition: Vector2`

语义：

- 对 `UseRangeDirection && RangeCollider != null` 的范围物体：
  - `LeftPosition.x = RangeCollider.bounds.min.x`
  - `RightPosition.x = RangeCollider.bounds.max.x`
  - `LeftPosition.y / RightPosition.y` 可先取 bounds center y，或分别取同一 y 值；
- 对非范围物体：
  - `LeftPosition == Position`
  - `RightPosition == Position`

优点：

- 兼容现有 condition，不破坏已有技能；
- Agent 可写出用户示例中的条件：

```text
objects[3].State == "Idle" && objects[3].RightPosition.x - objects[2].RightPosition.x < 0.5
```

- 非范围物体也可使用统一字段，不需要 Agent 每次判断对象类型。

缺点：

- 若保留 `Position`，Agent 仍可能继续误用中心点；
- 需要在工具描述中明确“范围物体判断边界时优先用 LeftPosition/RightPosition”。

适用性：推荐作为兼容性较好的第一步。

### 6.2 方案 B：范围物体禁止在 condition 使用 Position，强制使用 LeftPosition/RightPosition

用户提出的核心方向：如果 `sceneObjInfo` 是范围物体，则不允许 condition 使用 `Position`，而是使用左侧/右侧 Position。

理论收益：

- 从规则上消除范围物体中心点误用；
- Agent 会被迫使用边界语义，与环境渲染一致。

实现难点：

- Python 的 Pydantic 静态校验只看到表达式字符串，不知道 `objects[3]` 在当前 Unity 场景快照里是否是范围物体；
- Unity 规划校验阶段可以知道 `objects[3]` 的真实类型/配置，因此可以做语义校验；
- 但执行阶段对象列表是规划时快照，若对象消失/替换，需要考虑错误提示；
- 对已有技能/记忆中使用 `objects[i].Position` 的动作序列可能产生兼容性影响。

如果采用该方案，建议做成“Unity 规划校验报错 + 明确提示修正”，而不是 Python schema 直接禁止所有 `Position`：

- 对非范围物体：允许 `Position`；
- 对范围物体：如果 condition 中出现 `objects[i].Position`，规划阶段返回错误：

```text
objects[3](自动移动的平台) 是范围物体，不能使用 Position 判断边界；请改用 LeftPosition 或 RightPosition。
```

### 6.3 方案 C：统一重定义 Position，使范围物体的 Position 表示中心/边界之一

可能做法：

- 对范围物体让 `Position` 表示 bounds.center；或
- 让 `Position` 表示“面向 AI Player 的最近边界”；或
- 让 `Position` 表示某个更符合观察的点。

不推荐，原因：

- `Position` 名称无法表达到底是中心、左边界还是右边界；
- 不同任务需要不同边界，单一 Position 不够；
- 会破坏已有表达式对 `Position` 的隐含理解；
- Agent 仍难以写“平台右边缘 - 陷阱右边缘”这种条件。

### 6.4 方案 D：强化工具选择约束，明确浮板过陷阱应使用 ActionSequence

本场景暴露的不是 `FollowTarget` 功能不足，而是 Agent 在任务分解时选错了工具。因此应考虑强化工具描述和示例：

1. 在 `follow_target_cmd` 描述中明确：该工具适合持续跟随移动目标，不适合“乘坐移动平台渡过陷阱/深渊/障碍并上岸”这类需要明确阶段和完成条件的任务；
2. 在 `plan_action_sequence_cmd` 描述中加入浮板过陷阱示例，展示如何使用 `Wait + Move + Wait + Move` 表达完整流程；
3. 在 condition 描述中说明：范围物体的到达、越过、接近应使用 `LeftPosition` / `RightPosition`；
4. 如后续仍发生误选，可考虑在 prompt 或工具描述中加入更强约束：“借助平台通过危险区域时，优先规划动作序列，不要使用 FollowTarget”。

优点：

- 修复方向直接对应错误工具选择；
- 不需要把 `FollowTarget` 复杂化为另一套 ActionSequence；
- 与边界字段方案配合后，Agent 有能力写出完整、可验证的过陷阱流程。

缺点：

- 工具描述属于软约束，不能 100% 保证模型不误选；
- 若 ActionSequence 仍缺乏必要边界字段，单纯改描述无法解决根因。

建议：作为 v0.21.4 的配套修复，与范围边界字段一起做；不建议本版本扩展 `FollowTarget` 协议或完成条件。

---

## 7. 推荐方向

推荐采用“两层修复”：

### 第一层：补齐 condition 的范围边界表达能力

在 Unity `SceneObjExprView` 中增加：

- `LeftPosition`
- `RightPosition`

在 Python `CONDITION_VARIABLES` 中同步允许：

- `LeftPosition`
- `RightPosition`

在 condition 描述中明确：

- `Position`：物体中心/根节点位置，仅适合非范围物体或粗略判断；
- `LeftPosition`：物体范围左边界位置；非范围物体等同 `Position`；
- `RightPosition`：物体范围右边界位置；非范围物体等同 `Position`；
- 对陷阱、平台、墙等范围物体，判断是否越过、接近、离开时应优先使用左右边界。

### 第二层：规划阶段阻止范围物体误用 Position

在 Unity `ConditionEvaluator.ValidateAll()` 中增加语义检查：

- 扫描 condition 中的 `objects[i].Position`；
- 如果 `context.ObjectsSrc[i].UseRangeDirection == true` 且有 `RangeCollider`：
  - 返回 Error；
  - 提示改用 `objects[i].LeftPosition` 或 `objects[i].RightPosition`；
- `myself.Position` 不受影响；
- 非范围物体 `objects[i].Position` 不受影响。

这与用户想法一致，但落点更明确：

- Python 侧负责“允许新字段”；
- Unity 侧负责“结合当前场景快照做范围物体 Position 禁用校验”。

### 第三层：修正工具选择引导，避免过陷阱场景误用 FollowTarget

v0.21.4 不应扩展 `FollowTarget` 的协议/动作类型，而应明确其边界：

- `FollowTarget` 适合“持续跟随某个移动目标”；
- “借助浮板/移动平台渡过陷阱并上岸”应优先使用 ActionSequence；
- `plan_action_sequence_cmd` 应提供浮板过陷阱示例；
- `follow_target_cmd` 应明确不适合此类需要阶段完成条件的过障任务。

边界字段生效后，Agent 应能把过陷阱写成完整动作序列：

1. 等待浮板到当前岸边；
2. 移动到浮板上；
3. 等待浮板右边界接近/越过陷阱右边界或目标岸边；
4. 向右移动上岸。

---

## 8. 用户示例条件的修正建议

用户示例：

```text
objects[3].State == 'Idle' && objects[3].RightPosition.x - objects[4].RightPosition.x < 0.5
```

需要注意两点：

1. 如果 `objects[4]` 是检查点，通常不是范围物体，`RightPosition == Position` 可以成立，但它未必代表岸边边界；
2. 如果目标是“平台到达陷阱对岸”，更自然的比较对象可能是陷阱 `objects[2]` 的 `RightPosition.x`：

```text
objects[3].State == "Idle" && objects[3].RightPosition.x - objects[2].RightPosition.x > 0.3
```

或：

```text
objects[3].State == "Idle" && Math.Abs(objects[3].RightPosition.x - objects[2].RightPosition.x) < 0.5
```

具体用 `<`、`>` 还是 `Abs(...) < tolerance`，取决于关卡坐标方向与希望等待的平台停靠点。

---

## 9. 范围物体渲染文案优化

当前范围物体渲染为：

```text
范围: 从你的 left方向4.62108m 到你的 right方向2.965479m 范围
```

这个表达对人类能理解，但对 AI Player 可能存在三个问题：

1. “从你的 left方向X 到你的 right方向Y”需要模型自行还原左右边界；
2. 没有明确说明这是“左边界”和“右边界”；
3. 没有和 ActionSequence 中即将开放的 `LeftPosition` / `RightPosition` 形成同构概念。

建议优化为更结构化的表达，例如：

```text
范围: 左边界在你的 left方向4.62m，右边界在你的 right方向2.97m
```

或进一步加一行强调用途：

```text
范围: 左边界在你的 left方向4.62m，右边界在你的 right方向2.97m
说明: 判断是否越过、接近或离开该范围时，应关注左边界/右边界。
```

考虑 token 成本与环境信息密度，推荐采用第一种短格式：

```text
范围: 左边界在你的 {RangeLeftDirection}方向 {RangeLeftDistance}m，右边界在你的 {RangeRightDirection}方向 {RangeRightDistance}m
```

它的好处是：

- 与 `LeftPosition` / `RightPosition` 的概念一致；
- 减少“从 A 到 B”这种需要额外解析的句式；
- 明确告诉 Agent 左右两个数分别代表范围边界，而不是物体中心。

---

## 10. 已确认决策

1. `Position` 对范围物体完全禁止。
   - 在 Unity 规划校验阶段，如果 `objects[i]` 是范围物体，condition 中出现 `objects[i].Position` 应直接校验失败，并提示改用 `LeftPosition` / `RightPosition`。
2. 非范围物体的 `LeftPosition/RightPosition` 等同 `Position`。
   - 这样 Agent 可以在不额外判断对象类型的情况下统一使用边界字段。
3. `LeftPosition.y/RightPosition.y` 先取 `RangeCollider.bounds.center.y`。
   - 当前主要解决横版横向位置判断，y 只需保持可用且稳定。
4. 不暴露 `Width` / `RangeWidth`。
   - 如需宽度，可由 `RightPosition.x - LeftPosition.x` 计算。
5. `FollowTarget` 不写“不要用于浮板过陷阱”的硬编码禁令，而是写清楚工具使用后会发生什么、用来干嘛、适合什么场合。
   - 通过准确描述工具语义，让 Agent 自己判断它不适合阶段化过障任务。
6. 暂不补充默认技能。
   - 本版本不预置“浮板过陷阱技能”，以便测试 AI Player 能否通过训练自行掌握所需技能。
7. 需要优化范围物体的环境渲染文案。
   - 当前“范围: 从你的 left方向X 到你的 right方向Y 范围”可能对 AI Player 理解不够直接；PRD/方案中应纳入更清晰的边界表达。

---

## 11. 初步验收思路（非 PRD）

若后续进入 PRD/方案，建议至少验证：

1. 对非范围物体：旧条件 `objects[i].Position.x` 仍能规划和执行；
2. 对范围物体：规划条件使用 `objects[i].Position.x` 会收到清晰错误；
3. 对范围物体：使用 `LeftPosition/RightPosition` 的条件能通过规划校验；
4. 浮板/陷阱日志中，Agent 能生成基于平台右边界与陷阱右边界的等待/上岸判断；
5. AI Player 能把浮板过陷阱规划成完整 ActionSequence，而不是调用 `FollowTarget`；
6. AI Player 能在浮板到达对岸后通过动作序列向右上岸；
7. 若平台/陷阱索引变化，现有规划快照提示仍能帮助 Agent 核对对象编号。

---

## 12. 当前结论

本问题不是单纯的 LLM 决策失误，而是动作系统暴露给 Agent 的“场景对象位置语义”存在结构性不一致：

- 环境观察对范围物体使用边界语义；
- 动作条件对范围物体只提供中心/根节点语义；
- 浮板过陷阱恰好是必须依赖边界关系的场景；
- 由于 ActionSequence 难以自然表达完整过陷阱流程，Agent 改用了不适合该场景的 `FollowTarget`，最终表现为“浮板到岸后 AI Player 不上岸”。

建议 v0.21.4 的 PRD 围绕“范围物体在 ActionSequence condition 中的边界位置表达、误用校验、环境渲染文案优化，以及工具语义说明澄清”展开，而不是把问题归因于 `FollowTarget` 本身缺少结束条件；本版本不预置浮板过陷阱默认技能，以便保留训练验证空间。
