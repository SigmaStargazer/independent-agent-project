# 技术方案 — v0.21.4 范围物体边界条件与工具语义澄清

> **状态**：已实现（验收通过）  
> **依据 PRD**：`PRD.md`  
> **依据分析**：`analysis.md`  
> **最后更新**：2026-06-21

---

## 1. 方案概述

本方案通过三类轻量改造解决范围物体观察语义与动作条件语义不一致的问题：

1. Unity ActionSequence condition 表达式视图新增 `LeftPosition` / `RightPosition`；
2. Unity 规划校验阶段禁止范围物体使用 `objects[i].Position`，引导改用边界字段；
3. Python 工具 schema、condition 描述、环境渲染文案、`follow_target_cmd` 工具说明同步调整。

本期不改协议、不新增 ActionSequence action 类型、不扩展 `FollowTarget` 行为、不预置“浮板过陷阱”默认技能。

---

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| 文档 | `DevDocs/v0.21.4/analysis.md` | 已补充确认决策与渲染优化分析 |
| 文档 | `DevDocs/v0.21.4/PRD.md` | 新增待确认 PRD |
| Unity | `SceneObjInfoRenderer.cs` | 优化范围物体渲染文案 |
| Unity | `ExprViewFactory.cs` | `SceneObjExprView` 新增边界字段并填充 |
| Unity | `ConditionEvaluator.cs` | 新增范围物体误用 `Position` 的规划语义校验 |
| Python | `agent_framwork/tools/action_sequence_model/core/types.py` | condition schema 新增可访问成员与说明 |
| Python | `agent_framwork/tools/base_tools.py` | 澄清 `follow_target_cmd` 工具描述 |
| 协议 | `Tools/message.proto` | 无改动 |
| 数据 | `Src/PythonServer/db/default_skills/default.yaml` | 无改动 |

---

## 3. 详细设计

### 3.1 Unity：表达式视图新增边界字段

修改文件：

```text
Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/Action/ActionSequence/ConditionEvaluator/ExprViewFactory.cs
```

当前：

- `SceneObjExprView` 只有：
  - `Position`
  - `Velocity`
  - `State`

目标：

- 增加：
  - `Vector2 LeftPosition`
  - `Vector2 RightPosition`

填充规则：

```text
Position = sceneObj.transform.position 的 x/y

如果 sceneObj.UseRangeDirection && sceneObj.RangeCollider != null:
    bounds = sceneObj.RangeCollider.bounds
    LeftPosition = new Vector2(bounds.min.x, bounds.center.y)
    RightPosition = new Vector2(bounds.max.x, bounds.center.y)
否则:
    LeftPosition = Position
    RightPosition = Position
```

注意：

- 不改变 `Position` 的原语义；
- 不暴露 `Width`；
- `LeftPosition.y/RightPosition.y` 按 PRD 取 `bounds.center.y`。

### 3.2 Unity：规划阶段禁止范围物体使用 Position

修改文件：

```text
Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/Action/ActionSequence/ConditionEvaluator/ConditionEvaluator.cs
```

新增校验函数建议命名：

```csharp
private ConditionEvalResult ValidateRangeObjectPositionReference(string condition, ConditionContext context)
```

触发时机：

- 在 `ValidateAll()` 中，执行 `mInterpreter.Eval<bool>(step.Condition)` 之前；
- 与现有 `ValidateNearestInteractableIndexReference()` 并列。

检测逻辑：

- 用正则匹配：

```text
objects\[(\d+)\]\.Position\b
```

- 对每个索引：
  - 如果索引越界，返回错误；
  - 如果 `context.ObjectsSrc[index].UseRangeDirection == true && context.ObjectsSrc[index].RangeCollider != null`，返回错误；
  - 否则允许。

错误信息建议：

```text
action_sequence[{stepIndex}].condition校验出错: objects[3](自动移动的平台) 是范围物体，不能使用 Position 判断位置；请改用 objects[3].LeftPosition 或 objects[3].RightPosition。
```

实现注意：

- 该校验只在规划阶段必须执行；
- 执行阶段理论上已通过规划校验，不必重复；
- 若希望防御性更强，也可在 `Evaluate()` 前复用，但要避免每帧错误日志过多；推荐先只做规划校验。

### 3.3 Unity：范围物体环境渲染优化

修改文件：

```text
Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Base/SceneObjInfo/SceneObjInfoRenderer.cs
```

当前范围格式：

```csharp
$"范围: 从你的 {sceneObjInfo.RangeLeftDirection}方向{sceneObjInfo.RangeLeftDistance}m " +
$"到你的 {sceneObjInfo.RangeRightDirection}方向{sceneObjInfo.RangeRightDistance}m 范围"
```

目标格式：

```text
范围: 左边界在你的 left方向 4.62m，右边界在你的 right方向 2.97m
```

建议实现：

```csharp
$"范围: 左边界在你的 {sceneObjInfo.RangeLeftDirection}方向 {sceneObjInfo.RangeLeftDistance:F2}m，" +
$"右边界在你的 {sceneObjInfo.RangeRightDirection}方向 {sceneObjInfo.RangeRightDistance:F2}m"
```

关于小数位：

- 用户已确认范围渲染距离一律使用 `F2`；
- 本版本不保留原始 float 默认输出。

### 3.4 Python：condition schema 新增字段

修改文件：

```text
Src/PythonServer/agent_framwork/tools/action_sequence_model/core/types.py
```

当前：

```python
members={"Position", "Velocity", "State"}
```

目标：

```python
members={"Position", "LeftPosition", "RightPosition", "Velocity", "State"}
```

同时更新 `CONDITION_DESC` 中 `sceneObj类的属性` 说明：

- `Position`：物体当前位置。非范围物体可用；范围物体禁止使用，应改用左右边界；
- `LeftPosition`：物体左边界位置。非范围物体等同 `Position`；
- `RightPosition`：物体右边界位置。非范围物体等同 `Position`；
- `Velocity`：当前速度；
- `State`：当前状态。

### 3.5 Python：Vector2 二级成员校验注意事项

当前 Python validator 的 `_validate_access()` 对 `objects[i].member` 做一层成员检查，但不会显式校验 `objects[i].LeftPosition.x` 的二级成员。

现状推断：

- `objects[i].LeftPosition.x` 中的 `LeftPosition` 会被 `objects\[(\d+)\]\.(\w+)` 捕获；
- 只要 `LeftPosition` 加入 `members`，该表达式能通过 Python 第一层校验；
- `x` 因为前面有 `.x`，会被根变量校验中的 `if f".{ident}" in expr_no_string: continue` 跳过；
- Unity DynamicExpresso 负责实际解析 `Vector2.x`。

因此本期无需重构 Python condition validator。

### 3.6 Python：FollowTarget 工具描述澄清

修改文件：

```text
Src/PythonServer/agent_framwork/tools/base_tools.py
```

目标：更新 `follow_target_cmd` docstring，不改函数签名、不改协议。

描述应包含：

- 使用后 AI Player 会进入持续跟随状态；
- 会尝试让自己与目标的横向距离保持在 `minDistance` 和 `maxDistance` 之间；
- 适合长期跟随移动角色或移动物体；
- 不是“一次性移动到某点”的工具；
- 不是“由多个阶段组成、需要等待和完成条件的动作序列”。

注意：

- 不写硬编码禁令“不要用于浮板过陷阱”；
- 不提供浮板过陷阱默认步骤示例；
- 只澄清工具真实语义。

### 3.7 不修改默认技能

明确不修改：

```text
Src/PythonServer/db/default_skills/default.yaml
```

也不新增默认技能导出文件。

---

## 4. 实现步骤

1. 修改 Unity `ExprViewFactory.cs`
   - `SceneObjExprView` 新增 `LeftPosition` / `RightPosition`；
   - 按范围/非范围规则填充字段。
2. 修改 Unity `ConditionEvaluator.cs`
   - 新增 `ValidateRangeObjectPositionReference()`；
   - 在 `ValidateAll()` 的 Eval 前调用；
   - 返回清晰错误。
3. 修改 Unity `SceneObjInfoRenderer.cs`
   - 将范围物体渲染改为“左边界/右边界”短格式；
   - 推荐距离使用 `F2`。
4. 修改 Python `types.py`
   - `objects.members` 增加 `LeftPosition` / `RightPosition`；
   - 更新 condition 描述。
5. 修改 Python `base_tools.py`
   - 澄清 `follow_target_cmd` docstring；
   - 不改签名、不改逻辑。
6. 自测 Python schema
   - 验证含 `LeftPosition.x` / `RightPosition.x` 的 ActionStep 能通过 Pydantic 校验；
   - 验证非法字段仍失败。
7. Unity 验证
   - 编译通过；
   - 规划范围物体 `Position` 条件时应失败；
   - 规划范围物体 `LeftPosition/RightPosition` 条件时应通过。

---

## 5. 风险与回退

| 风险 | 影响 | 缓解 / 回退 |
|------|------|-------------|
| Python schema 放开字段但 Unity 未同步实现 | Agent 能生成表达式但 Unity 执行报错 | 必须同时改 Unity `SceneObjExprView`；联调时优先验证边界字段 |
| 范围物体旧技能使用 `Position` 被拒绝 | 旧技能可能需要迁移 | 这是本期确认行为；错误信息应指导改用边界字段 |
| 渲染文案改为 `F2` 导致日志精度降低 | 细微位置调试信息减少 | 用户已确认一律使用 `F2`；不影响 condition 实际字段 |
| 工具描述仍不足以避免误用 `FollowTarget` | Agent 仍可能误选 | 本版本先不加硬禁令；后续可基于日志再加强 prompt/工具描述 |
| 只在规划阶段校验，执行阶段未重复校验 | 理论上被绕过时可能运行错误表达式 | 现有工具流程先规划后确认执行；若后续出现绕过路径，再补 Evaluate 防御校验 |

---

## 6. 测试用例

本节是继续开发前必须确认的测试用例。后续开发不得再用临时命令行片段或只覆盖表层 schema 的脚本替代本节测试。

### 6.1 无效测试记录

#### INVALID-001：`test_v021_4_action_sequence_range_condition.py`

状态：无效测试，不能作为本功能验收依据。

无效原因：

- 该脚本只验证 Python Pydantic 层是否接受 `LeftPosition` / `RightPosition` 等字段名；
- 没有验证 Unity `SceneObjExprView` 是否正确从 `RangeCollider.bounds` 生成边界坐标；
- 没有验证 Unity `ConditionEvaluator` 是否真的禁止范围物体使用 `objects[i].Position`；
- 没有验证 DynamicExpresso 是否能真实求值 `RightPosition.x` / `LeftPosition.x`；
- 没有验证范围物体渲染是否输出正确文本；
- 没有覆盖本次 bug 的核心链路：观察边界语义 → 规划 ActionSequence condition → Unity 校验/执行。

后续处理：

- 该脚本可以删除，或保留但必须在文件头标注“无效测试记录，不作为验收依据”；
- 若保留，应仅用于事故复盘，不纳入本版本测试通过条件。

### 6.2 Python schema 测试

#### PY-001：允许 ActionSequence condition 使用范围边界字段

目的：验证 Python 工具 schema 允许 Agent 生成边界字段表达式，避免在请求发往 Unity 前被 Pydantic 拦截。

前置条件：

- 不启动 Unity；
- 在 `Src/PythonServer` 环境运行；
- 导入 `MoveAction` / `WaitAction`。

输入：

```text
objects[3].RightPosition.x - objects[2].RightPosition.x > 0.3
objects[3].LeftPosition.x < objects[2].RightPosition.x
```

步骤：

1. 构造 `MoveAction(action="move", direction="right", condition=..., allowed_contact_obj_ids=[])`；
2. 构造 `WaitAction(action="wait", condition=...)`。

期望：

- 两个 action 均构造成功；
- condition 字符串保持原样；
- 不触发 `ValidationError`。

覆盖风险：

- Python schema 未同步新增字段，导致 Agent 无法生成合法 ActionSequence。

#### PY-002：未知对象字段仍被拒绝

目的：验证新增字段没有放开任意对象成员访问。

输入：

```text
objects[3].UnknownPosition.x > 0
```

步骤：

1. 构造 `MoveAction(action="move", direction="right", condition=..., allowed_contact_obj_ids=[])`。

期望：

- 抛出 `ValidationError`；
- 错误信息中包含 `UnknownPosition` 或“不允许访问成员”。

覆盖风险：

- condition schema 过度放宽，导致 Agent 能生成 Unity 侧无法解释的字段。

#### PY-003：范围物体 `Position` 不在 Python 层拒绝

目的：确认“范围物体禁止 Position”的判断不错误地下沉到 Python 静态 schema，因为 Python 不知道 `objects[i]` 当前是否为范围物体。

输入：

```text
objects[3].Position.x < 7
```

步骤：

1. 构造 `WaitAction(action="wait", condition=...)`。

期望：

- Python 构造成功；
- 是否拒绝应由 Unity 规划校验根据 `objects[3]` 的真实类型决定。

覆盖风险：

- Python 层误禁所有 `Position`，破坏非范围物体兼容性。

#### PY-004：FollowTarget 描述只澄清语义，不写关卡硬禁令

目的：验证 `follow_target_cmd` 的 docstring 符合用户确认的方向。

前置条件：

- 不启动 Unity；
- 在 `Src/PythonServer` 环境运行；
- 可通过 Python 读取 `follow_target_cmd.description` 描述。

检查项：

- 描述包含“持续跟随状态”；
- 描述包含 `min_distance` 与 `max_distance`；
- 描述说明它不是一次性移动，也不是阶段化动作序列；
- 描述不包含“浮板过陷阱禁止使用”这类具体关卡硬禁令。

期望：

- 以上检查全部满足。

覆盖风险：

- 工具描述过度硬编码，影响 Agent 自主判断与训练。

#### SRC-001：Unity 源码静态检查边界字段与 F2 渲染

目的：在不启动 Unity 的当前环境中，尽可能检查 C# 源码是否包含本期关键实现点。该测试不能替代 Unity 运行时测试。

前置条件：

- 不启动 Unity；
- 直接读取 C# 源码文件文本。

检查项：

- `ExprViewFactory.cs` 包含 `LeftPosition`、`RightPosition`，且不包含表达式视图用的 `IsRange` 字段；
- `ExprViewFactory.cs` 包含 `bounds.min.x`、`bounds.max.x`、`bounds.center.y`；
- `ConditionEvaluator.cs` 包含 `ValidateRangeObjectPositionReference`；
- `ConditionEvaluator.cs` 的错误信息包含“范围物体”“不能使用 Position”“LeftPosition”“RightPosition”；
- `SceneObjInfoRenderer.cs` 包含“左边界”“右边界”和 `F2`；
- 被读取源码可按 UTF-8 解码，且不残留 `\\uXXXX` 形式的 Unicode 转义。

期望：

- 所有检查项均成立。

覆盖风险：

- 避免漏改关键源码文件；但不能证明 Unity 编译和运行时行为正确。

### 6.3 当前环境不可自测项（不在本轮自测中执行）

以下用例依赖 Unity 编辑器、Unity Test Runner、C# 编译环境或 Python-Unity 联调，当前不能由 Cursor 仅在 PythonServer 工作区内独立完成。它们不纳入本轮“自测通过”结论，只作为后续 Unity/人工联调验收项。

### 6.4 Unity 表达式视图字段映射测试

#### U-001：范围物体映射 LeftPosition / RightPosition

目的：验证 `ExprViewFactory.From(sceneObj)` 对范围物体使用 `RangeCollider.bounds` 生成边界字段。

前置条件：

- 在 Unity 编辑器或 Unity Test Runner 中创建一个测试对象；
- 对象继承/挂载 `SceneObjBase` 的可测试实现；
- `UseRangeDirection == true`；
- `RangeCollider.bounds.min.x = 2.0`，`bounds.max.x = 6.0`，`bounds.center.y = 1.5`；
- `transform.position = (4.0, 0.0)`。

步骤：

1. 调用 `ExprViewFactory.From(rangeObj)`；
2. 读取 `view.Position`、`view.LeftPosition`、`view.RightPosition`。

期望：

- `view.Position.x == 4.0`；
- `view.LeftPosition.x == 2.0`；
- `view.RightPosition.x == 6.0`；
- `view.LeftPosition.y == 1.5`；
- `view.RightPosition.y == 1.5`。

覆盖风险：

- 边界字段仍然使用中心点，导致本次修复无效。

#### U-002：非范围物体 LeftPosition / RightPosition 等同 Position

目的：验证非范围物体兼容策略。

前置条件：

- 测试对象 `UseRangeDirection == false`；
- `transform.position = (3.0, 2.0)`。

步骤：

1. 调用 `ExprViewFactory.From(normalObj)`；
2. 读取 `view.Position`、`view.LeftPosition`、`view.RightPosition`。

期望：

- `view.Position == (3.0, 2.0)`；
- `view.LeftPosition == view.Position`；
- `view.RightPosition == view.Position`。

覆盖风险：

- 非范围物体旧条件或统一边界写法出现兼容性问题。

### 6.5 Unity 规划语义校验测试

#### U-003：范围物体使用 objects[i].Position 时规划失败

目的：验证本期核心规则“范围物体完全禁止 Position”。

前置条件：

- `ConditionContext.ObjectsSrc[3]` 是范围物体；
- `ObjectsSrc[3].UseRangeDirection == true`；
- `ObjectsSrc[3].RangeCollider != null`。

输入：

```text
objects[3].Position.x < 7
```

步骤：

1. 构造包含上述 condition 的 `WaitAction` 或 `MoveAction`；
2. 调用 `ConditionEvaluator.ValidateAll(actionSequence, context)`。

期望：

- 对应结果 `Status == Error`；
- `ErrorMessage` 包含：
  - `objects[3]`；
  - 对象名称；
  - `范围物体`；
  - `不能使用 Position`；
  - `LeftPosition` 或 `RightPosition`。

覆盖风险：

- Agent 继续用范围物体中心点写条件，导致浮板/陷阱边界判断失真。

#### U-004：非范围物体使用 objects[i].Position 时规划通过

目的：验证禁止规则只作用于范围物体，不破坏旧行为。

前置条件：

- `ConditionContext.ObjectsSrc[1]` 是非范围物体；
- `ObjectsSrc[1].UseRangeDirection == false`。

输入：

```text
objects[1].Position.x < 7
```

步骤：

1. 构造包含上述 condition 的 `WaitAction`；
2. 调用 `ConditionEvaluator.ValidateAll(actionSequence, context)`。

期望：

- 不因范围物体 Position 校验失败；
- 若表达式本身可求值，应返回 `True` 或 `False`，但不是 `Error`。

覆盖风险：

- 误伤非范围物体现有技能和动作序列。

#### U-005：范围物体使用 LeftPosition / RightPosition 时规划通过并可求值

目的：验证 DynamicExpresso 能真实访问新增字段并求值。

前置条件：

- `objects[2]` 是陷阱范围物体，`RightPosition.x = 10.0`；
- `objects[3]` 是平台范围物体，`RightPosition.x = 10.4`；
- 两者都已通过 `ExprViewFactory.From()` 进入 `ConditionContext.Objects`。

输入：

```text
objects[3].RightPosition.x - objects[2].RightPosition.x > 0.3
```

步骤：

1. 构造包含上述 condition 的 `WaitAction`；
2. 调用 `ConditionEvaluator.ValidateAll(actionSequence, context)`。

期望：

- 不触发语义校验错误；
- DynamicExpresso 成功求值；
- 结果为 `True`。

覆盖风险：

- 字段虽存在于 C# 类，但表达式运行时无法解析或无法求值。

#### U-006：越界 objects[i].Position 引用返回清晰错误

目的：验证新增校验不会产生未处理异常。

前置条件：

- `ConditionContext.ObjectsSrc.Count == 3`。

输入：

```text
objects[99].Position.x < 7
```

步骤：

1. 调用 `ConditionEvaluator.ValidateAll(actionSequence, context)`。

期望：

- 返回 `Status == Error`；
- 错误信息说明 `objects[99]` 不存在。

覆盖风险：

- Agent 写错编号时 Unity 抛异常而不是可修正反馈。

### 6.6 Unity 环境渲染测试

#### U-007：范围物体环境文本使用左/右边界 F2 格式

目的：验证 AI Player 观察到的信息与 condition 边界字段概念一致。

前置条件：

- 构造 `SceneObjInfoModel`：
  - `IsRangeDirection = true`；
  - `RangeLeftDirection = "left"`；
  - `RangeLeftDistance = 4.62108f`；
  - `RangeRightDirection = "right"`；
  - `RangeRightDistance = 2.965479f`；
  - 其他字段填入有效值。

步骤：

1. 调用 `SceneObjInfoRenderer.RenderSceneObj(sceneObjInfo)`。

期望输出包含：

```text
范围: 左边界在你的 left方向 4.62m，右边界在你的 right方向 2.97m
```

覆盖风险：

- 环境文本仍是“从 A 到 B”格式，AI Player 观察语义与 condition 字段不同构。

#### U-008：非范围物体环境文本继续使用方位 F2 格式

目的：验证非范围物体渲染不被范围文案破坏。

前置条件：

- `IsRangeDirection = false`；
- `Direction = "right"`；
- `Distance = 9.5622f`。

步骤：

1. 调用 `SceneObjInfoRenderer.RenderSceneObj(sceneObjInfo)`。

期望输出包含：

```text
方位:在你的 right方向 9.56m 位置
```

覆盖风险：

- 非范围物体观察信息格式异常。

### 6.7 集成/联调验收

#### INT-001：不预置技能时，AI Player 能基于训练形成过陷阱 ActionSequence

目的：验证本期底层语义修复是否支持训练，而不是靠默认技能答案。

前置条件：

- 不新增或修改默认“浮板过陷阱”技能；
- 使用包含陷阱与自动移动平台的练习关卡；
- Python 与 Unity 正常通信。

步骤：

1. 让 AI Player 观察环境；
2. 通过训练/交互引导它尝试借助浮板通过陷阱；
3. 观察它生成的 ActionSequence；
4. 执行动作序列并观察结果。

期望：

- ActionSequence 中能出现基于 `LeftPosition` / `RightPosition` 的等待条件；
- 不依赖预置默认技能；
- 若失败，日志能显示是训练/策略问题，而不是底层字段缺失或范围物体 `Position` 误用。

覆盖风险：

- 低层语义修复仍不足以支持 Agent 学会该任务。

#### INT-002：误用 FollowTarget 时能从日志区分为策略问题

目的：验证若 AI Player 仍选择 `FollowTarget`，能将问题归类为策略/训练问题，而不是底层 condition 表达能力问题。

前置条件：

- 本期字段和校验均已通过单元/编辑器测试；
- `follow_target_cmd` 描述已澄清。

步骤：

1. 让 AI Player 自主尝试过陷阱；
2. 如果它仍调用 `FollowTarget`，记录上下文、工具调用和环境信息。

期望：

- 日志中能确认 ActionSequence 已具备边界表达能力；
- `FollowTarget` 的选择可被归因为 Agent 策略未学会，而不是系统缺字段。

覆盖风险：

- 修复完成后仍无法判断失败原因。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-20 | 已将测试章节拆分为“当前环境可自测项”和“不可自测联调验收项”；新增 `test_v021_4_self_test.py` 覆盖 PY-001、PY-002、PY-003、PY-004、SRC-001，并运行通过。`test_v021_4_action_sequence_range_condition.py` 已标记为 INVALID-001，不作为验收依据。 |
| 2026-06-21 | 经二次讨论确认 `IsRange` 对本期 ActionSequence 边界表达不是必要参数，已从 PRD、方案、Python schema、Unity `SceneObjExprView` 与自测脚本中移除。 |
| 2026-06-21 | 用户确认 v0.21.4 验收通过。`2026-06-21_09-35-48.log` 中已观察到 AI Player 不再误用 `FollowTarget`，并能通过 ActionSequence 完成至少一次借助浮板从左到右渡过陷阱并上岸；日志中暴露的 DynamicExpresso 字符串单引号问题、边界条件使用不稳定、连续 5 次训练稳定性不足、无效工具调用 JSON 等问题转入后续版本处理。 |

---

*本文档由 Cursor Agent 根据 `PRD.md` 生成；已由用户确认，可按本方案修改代码。*
