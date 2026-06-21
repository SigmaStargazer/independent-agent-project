# PRD — v0.21.4 范围物体边界条件与工具语义澄清

> **状态**：已确认（对应方案已验收通过）  
> **对应需求**：用户口述需求（2026-06-20）：解决 AI Player 借助浮板过陷阱后不上岸的问题；补齐范围物体在 ActionSequence condition 中的边界表达；优化范围物体渲染；澄清 FollowTarget 工具语义；不预置默认技能。  
> **依据分析**：`analysis.md`  
> **最后更新**：2026-06-21

---

## 1. 背景与目标

在日志 `2026-06-20_18-30-19.log` 暴露的问题中，用户让 AI Player 借助浮板通过陷阱。AI Player 能够等待浮板、移动到浮板上，但随后使用了 `FollowTarget` 跟随浮板；浮板到达对岸后，AI Player 没有主动离开浮板上岸。

经分析，这不是 `FollowTarget` 本身缺少结束条件的问题，而是 Agent 在该场景中选错了工具。借助浮板过陷阱本质上应是一个可规划、可确认、可完成的 ActionSequence：

1. 等待浮板到当前岸边；
2. 移动到浮板上；
3. 等待浮板到达对岸；
4. 移动上岸。

当前阻碍 Agent 生成完整 ActionSequence 的关键问题是：

- 环境渲染对范围物体展示的是左右边界相对 AI Player 的方位；
- ActionSequence condition 中只能使用 `objects[i].Position`，实际是 `transform.position` 中心/根节点位置；
- 对浮板、陷阱、墙等范围物体而言，中心点不能表达边界关系；
- 因此 Agent 看到的信息与 condition 能判断的信息不一致，容易误用其他工具。

本期目标：

> 让 AI Player 在观察范围物体、编写动作序列条件、理解工具用途时获得一致语义，从而具备通过训练自行掌握“借助浮板过陷阱”的基础能力。

---

## 2. 范围

### 2.1 本期包含

- 在 ActionSequence condition 的对象表达式视图中新增范围边界位置：
  - `LeftPosition`
  - `RightPosition`
- 对范围物体完全禁止使用 `objects[i].Position`。
- 非范围物体的 `LeftPosition/RightPosition` 等同 `Position`。
- `LeftPosition.y/RightPosition.y` 对范围物体先取 `RangeCollider.bounds.center.y`。
- 不暴露 `Width` / `RangeWidth`。
- 优化范围物体在环境信息中的渲染格式，使其更直观地表达“左边界/右边界”。
- 澄清 `follow_target_cmd` 的工具语义：说明使用后会发生什么、用来干嘛、适合什么场合。
- 同步 Python 工具 schema 与 Unity condition 校验，确保 Agent 能生成并通过边界字段表达式。
- 保持本版本不预置“浮板过陷阱”默认技能。

### 2.2 本期不包含

- 不修改 `Tools/message.proto`。
- 不扩展 `FollowTarget` 协议或给它增加 condition。
- 不新增“跟随直到条件成立”的 ActionSequence action 类型。
- 不提供固定的“浮板过陷阱默认技能”。
- 不暴露 `Width` / `RangeWidth` 字段。
- 不改变 `myself.Position` 的语义。
- 不改变非范围物体 `objects[i].Position` 的可用性。
- 不以硬编码规则禁止 `FollowTarget` 用于某个具体关卡，而是澄清工具真实行为和适用场景。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| AI Player | 观察陷阱、浮板、墙等范围物体 | 能看到清晰的左边界/右边界描述，而不是难以解析的“从 A 到 B 范围” |
| AI Player | 规划涉及范围物体的 ActionSequence | 能使用 `LeftPosition` / `RightPosition` 表达边界关系 |
| AI Player | 对范围物体误用 `objects[i].Position` | 规划阶段收到明确错误，提示改用 `LeftPosition` / `RightPosition` |
| AI Player | 对非范围物体使用旧条件 `objects[i].Position` | 继续可用，不破坏已有简单动作序列 |
| AI Player | 需要持续跟随移动目标 | 能理解 `FollowTarget` 是持续跟随行为，开始后会持续保持与目标距离 |
| AI Player | 借助浮板通过陷阱 | 具备通过训练自行形成 ActionSequence 技能的条件，而不是依赖预置技能 |
| 开发者 | 调试 condition 错误 | 能从工具返回中看到清晰的校验失败原因和修正方向 |

---

## 4. 功能需求

### 4.1 ActionSequence condition 新增边界字段

ActionSequence condition 中，`objects[i]` 应新增以下可访问成员：

- `LeftPosition: Vector2`
- `RightPosition: Vector2`

字段语义：

- 对范围物体：
  - `LeftPosition.x = RangeCollider.bounds.min.x`
  - `RightPosition.x = RangeCollider.bounds.max.x`
  - `LeftPosition.y = RightPosition.y = RangeCollider.bounds.center.y`
- 对非范围物体：
  - `LeftPosition = Position`
  - `RightPosition = Position`

### 4.2 范围物体禁止使用 objects[i].Position

当 ActionSequence condition 中出现 `objects[i].Position` 时：

- 如果 `objects[i]` 是范围物体，规划校验必须失败；
- 错误信息必须指出：
  - 哪个对象是范围物体；
  - 不能使用 `Position`；
  - 应改用 `LeftPosition` 或 `RightPosition`；
- 如果 `objects[i]` 不是范围物体，`Position` 保持可用。

示例错误：

```text
objects[3](自动移动的平台) 是范围物体，不能使用 Position 判断位置；请改用 objects[3].LeftPosition 或 objects[3].RightPosition。
```

### 4.3 Python 工具 schema 允许新字段

Python 侧 condition 校验应允许以下成员访问：

```text
objects[i].LeftPosition.x
objects[i].LeftPosition.y
objects[i].RightPosition.x
objects[i].RightPosition.y
```

同时应保持以下旧字段：

```text
objects[i].Position
objects[i].Velocity
objects[i].State
```

但范围物体是否能用 `Position` 的判断由 Unity 规划校验负责，因为 Python schema 不知道当前对象是否是范围物体。

### 4.4 环境渲染优化

当前范围物体渲染格式为：

```text
范围: 从你的 left方向4.62108m 到你的 right方向2.965479m 范围
```

本期应优化为更明确的边界表达：

```text
范围: 左边界在你的 left方向 4.62m，右边界在你的 right方向 2.97m
```

要求：

- 明确使用“左边界”和“右边界”；
- 与 condition 字段 `LeftPosition` / `RightPosition` 的概念保持一致；
- 不额外增加长篇解释，避免环境信息过长；
- 保持非范围物体原有“方位”格式不变，除非实现中顺手做微小格式统一。

### 4.5 FollowTarget 工具语义澄清

`follow_target_cmd` 的描述应说明：

- 这是一个持续行动类工具；
- 使用后 AI Player 会持续跟随指定目标；
- 跟随过程中会尽量保持在 `minDistance` 和 `maxDistance` 之间；
- 适合需要长期跟随某个移动目标的场景；
- 它不是一次性的移动到某点，也不是阶段化动作序列。

要求：

- 不写“禁止用于浮板过陷阱”的硬编码规则；
- 通过准确描述工具行为，让 Agent 自己判断是否适合当前任务；
- 不改 `follow_target_cmd` 的协议和运行逻辑。

### 4.6 不预置默认技能

本版本不应新增或修改默认技能来直接教会 AI Player “浮板过陷阱”。

原因：

- 用户希望测试 AI Player 能否通过训练自行掌握所需技能；
- 本版本只补齐底层表达能力和语义一致性；
- 不用预制答案掩盖 Agent 自主学习/训练能力。

---

## 5. 非功能需求

- **兼容性**：非范围物体旧的 `objects[i].Position` 表达式必须继续可用。
- **可解释性**：范围物体误用 `Position` 时，错误信息必须足够清楚，让 Agent 能修正表达式。
- **低侵入**：不改协议、不改工具 RPC 结构、不新增动作类型。
- **认知一致性**：环境渲染中的“左边界/右边界”应与 condition 字段 `LeftPosition` / `RightPosition` 概念一致。
- **训练友好**：不预置默认过关技能，保留 Agent 通过训练形成技能的空间。

---

## 6. 验收标准

- [ ] 范围物体在环境文本中显示为“左边界/右边界”格式。
- [ ] ActionSequence condition 允许使用 `objects[i].LeftPosition.x/y` 和 `objects[i].RightPosition.x/y`。
- [ ] 非范围物体的 `LeftPosition/RightPosition` 与 `Position` 等同。
- [ ] 范围物体的 `LeftPosition.x/RightPosition.x` 来自 `RangeCollider.bounds.min.x/max.x`。
- [ ] 范围物体的 `LeftPosition.y/RightPosition.y` 来自 `RangeCollider.bounds.center.y`。
- [ ] 范围物体使用 `objects[i].Position` 时，规划校验失败并返回明确修正建议。
- [ ] 非范围物体使用 `objects[i].Position` 时，规划校验不因本功能失败。
- [ ] `follow_target_cmd` 描述清楚其持续跟随语义、距离保持行为和适用场景。
- [ ] 本版本不新增“浮板过陷阱”默认技能。
- [ ] 在无需 Unity 客户端联调的部分，完成 Python schema 校验自测。
- [ ] Unity 侧范围物体字段与规划校验可通过最小运行/编辑器测试或人工联调验证。

---

## 7. 已确认决策补充

- 范围渲染距离一律使用 `F2` 格式化。
- PRD 与方案已由用户确认，可以进入开发。

---

*本文档由 Cursor Agent 根据用户口述需求与 `analysis.md` 生成。*
