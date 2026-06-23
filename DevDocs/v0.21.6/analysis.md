# v0.21.6 分析文档 — ActionSkill 模板参数化、训练验收标准与运行联调可观测性

> **状态**：分析中  
> **分析对象**：`Src/PythonServer/logs/prompts/小明/2026-06-22_11-22-36.log`、`Src/PythonServer/logs/prompts/小明/2026-06-22_12-32-04.log`、`Src/PythonServer/db/default_skills/exports/小明_20260622_112329.yaml`  
> **最后更新**：2026-06-22

---

## 1. 背景

`v0.21.5` 已验收通过，解决了 ActionSequence 表达稳定性、`step_explanations` 强类型存储、记忆压缩与 repeat timer 等问题。本次继续分析验收后的新日志与导出的 ActionSkill YAML，用于确定下个小版本的开发方向。

本轮测试任务仍围绕练习关卡 1：让 AI Player 反复借助浮板渡过陷阱，要求：

1. 每次仅使用一个动作序列，一次性渡过陷阱；
2. 不使用其他运动工具；
3. 不断提升熟练度，直到能连续成功 5 次。

---

## 2. 本轮正向结论

### 2.1 v0.21.5 语法类修复继续稳定

在 `2026-06-22_11-22-36.log` 中，没有再发现以下旧问题：

- 单引号 condition：如 `objects[3].State == 'Idle'`；
- Vector2 大写坐标字段：如 `LeftPosition.X` / `RightPosition.X`；
- DynamicExpresso 原始错误：`No property or field 'X' exists in type 'Vector2'`；
- `Invalid Tool Calls`；
- `Range of input length should be [1, 30720]`；
- repeat timer 高频打断。

说明 `v0.21.5` 对表达式语法、上下文压缩、计时器安全边界的修复方向是有效的。

### 2.2 `step_explanations` 已进入技能索引与导出 YAML

导出 YAML 中，`借助移动平台渡越陷阱` 技能已经包含完整 `step_explanations`，并且在 prompt 的动作技能记忆中也被注入：

- 每个 step 都有 `action_reason`；
- 每个 step 都有 `parameter_reason`；
- 需要 condition 的 step 有 `condition_reason`；
- 有 `adjustment_hint` 说明如何变通。

这说明 `v0.21.5` 的“让 AI Player 知其然也知其所以然”的方向已经生效。

### 2.3 Agent 能从旧技能中继续演化出新策略

日志后段显示，Agent 在用户纠正“必须同一个动作序列连续成功 5 次”后，最终提出了“统一双向渡越”的新思路：

> 同一个序列，始终向右走，双向通用。

并在导出的 YAML 中形成了第二个模板：`统一双向渡越`。

这说明 ActionSkill 的索引注入与逐步解释，确实帮助 Agent 在已有技能基础上继续推演，而不是完全从零试错。

### 2.4 新 Agent 能直接利用导入技能完成任务

在 `2026-06-22_12-32-04.log` 中，使用导出 YAML 创建的新 AI Player 一开始没有长期记忆：

```text
<回想>


</回想>
```

但它的 prompt 中已经注入了 `平地接近`、`标准渡越`、`统一双向渡越` 三个动作技能模板。随后新 Agent 能够直接把模板中的占位信息替换为当前场景对象：

- 陷阱：`objects[2]`；
- 自动移动的平台：`objects[3]`；
- 方向、边界条件与 `allowed_contact_obj_ids` 均按当前场景改写。

这说明导出的 ActionSkill YAML 已经具备实际迁移价值：新 Agent 不需要重新从零学习，就能较快规划出可执行动作序列。

### 2.5 复用测试最终结果较好，但不是“一次无失误”

复用验证中，新 Agent 完成了两个关键目标：

1. 从左侧借助平台渡到右侧，动作序列状态为 `Completed`；
2. 根据用户要求返回玩家身边，再次借助平台从右侧回到左侧，动作序列状态同样为 `Completed`。

这说明训练成果总体有效，尤其是 `标准渡越` 模板与 `step_explanations` 对 Agent 复用技能有明显帮助。

但本次也出现了一次早期失败：Agent 首先套用了 `统一双向渡越`，在平台停在右侧时仍“始终向右走”，结果触碰陷阱并触发 `Aborted`。这说明导出的技能并非无条件可靠，特别是 `统一双向渡越` 这类从局部训练经验归纳出的模板，需要明确适用前提和验证步骤。

---

## 3. 主要问题分析

### 3.1 P0：ActionSkill 模板参数化表达能力不足

#### 现象

导出 YAML 中 `标准渡越` 模板如下：

```yaml
- action: wait
  condition: objects[0].State == "Idle" && objects[0].LeftPosition.x < 0
- action: move
  direction: right
  condition: myself.Position.x >= objects[0].RightPosition.x - 0
  allowed_contact_obj_ids:
  - 0
  - 0
```

这些 `0` 并不是实际业务语义，而是 Agent 为了让 `action_sequence_template` 成为合法 JSON，不得不用数字占位。

日志中它曾三次尝试使用更自然的模板占位符，例如：

- `{platform_index}`；
- `{direction}`；
- `PLATFORM` / `LEFT_THRESHOLD` / `TRAP`；

但均被 `_parse_action_sequence` 拒绝，报错为：

```text
创建技能失败：action_sequence_template 不是合法 JSON
```

#### 根因

当前 ActionSkill 模型中：

- `action_sequence_template` 被设计为 `List[dict]`，要求必须是合法 JSON / YAML 结构；
- 但“技能模板”的本质又需要表达占位参数；
- 占位参数如果直接出现在 JSON 数值位置或枚举字段位置，就会破坏 JSON 合法性或 Pydantic/ActionSequence 合法性；
- Agent 最终只能用 `0`、`right` 之类的“合法示例值”假装占位。

这导致模板可读性和可复用性下降。

#### 影响

1. `objects[0]` 容易被误认为固定物体序号，而不是“替换为平台序号”；
2. `allowed_contact_obj_ids: [0, 0]` 丢失了“平台 + 陷阱”两个不同对象的语义；
3. `condition: myself.Position.x > 0` 丢失了“安全位置 / 出口阈值”的语义；
4. Agent 复用模板时必须从 `description`、`usage_notes`、`step_explanations` 中重新猜测这些 `0` 的含义；
5. 技能越复杂，越容易因占位语义不明确导致错误复用。

#### 判断

这是下版本最核心问题。`step_explanations` 提供了“为什么”，但 `action_sequence_template` 本身仍缺少“如何参数化”的正式结构。

---

### 3.2 P0：训练目标“连续成功”的判定仍依赖 Agent 自我统计

#### 现象

日志中 Agent 一度宣布：

```text
🎉 完成！连续5次成功借助浮板渡越陷阱！
```

但它的统计中包含：

```text
向左渡越 - 平台成功载我过陷阱（最后撞墙但渡越成功）
```

随后用户纠正：

```text
我是要你通过反复渡过陷阱来提升你自己的熟练度，直到能连续5次仅使用一个动作序列就能成功为止
```

Agent 才重新理解为“同一个动作序列连续成功 5 次”。

#### 根因

当前系统把动作序列结果以自然语言反馈给 Agent，Agent 自己负责判断：

- 什么算一次成功；
- 什么算中断；
- 中间失败是否清零；
- “渡过陷阱”成功但最后撞墙算不算成功；
- “左右两个不同动作序列”是否算同一个序列。

这类训练验收逻辑不适合完全交给 LLM 自我统计。

#### 影响

1. Agent 可能把局部成功误判为完整成功；
2. 可能把带有 `Aborted` / `Failed` 的动作序列算入成功次数；
3. 可能忽略“同一个动作序列”的约束；
4. 用户必须手动纠偏，影响训练效率。

#### 判断

下版本应引入更明确的训练目标/动作序列验收机制。至少 prompt 中要明确，最好由系统返回结构化执行结果。

---

### 3.3 P1：导出的默认技能仍存在旧格式与新格式混用

#### 现象

导出 YAML 中默认技能 `走到目标旁交互` 仍然是旧结构：

```yaml
- action: Move
  params:
    target: '{target_name}'
    condition: object[0].distance < 1.5
- action: Interact
  params:
    target: '{target_name}'
step_explanations: []
```

而新保存的 `借助移动平台渡越陷阱` 是新结构：

```yaml
- action: wait
  condition: ...
- action: move
  direction: ...
  condition: ...
```

#### 根因

默认技能 YAML 仍沿用了早期抽象模板结构，并没有迁移到当前 ActionSequence 模型：`wait` / `move` / `interact` / `select` / `input`。

#### 影响

1. ActionSkill 索引中同时出现两种模板语法；
2. Agent 可能模仿旧模板，生成无法直接用于 `plan_action_sequence_cmd` 的动作；
3. `step_explanations` 为空，与新版本“技能要解释每一步”的目标不一致。

#### 判断

这不是本轮浮板训练的阻塞项，但会影响后续技能库质量。建议下版本顺手清理默认技能格式。

---

### 3.4 P1：`统一双向渡越` 模板已在复用测试中暴露风险

#### 现象

导出 YAML 中 `统一双向渡越` 的描述是：

```text
统一双向渡越：始终向右走。无论从左侧到右侧还是从右侧回左侧，都用同一个序列。平台自动处理方向，无需区分左右。
```

模板也固定：

```yaml
direction: right
condition: myself.Position.x >= objects[0].RightPosition.x - 0.5
...
direction: right
condition: myself.Position.x > objects[0].RightPosition.x + 2.0
```

在 `2026-06-22_12-32-04.log` 的新 Agent 复用测试中，这个风险已经实际出现：

1. 新 Agent 首先套用了 `统一双向渡越`；
2. 当时平台处于右侧，Agent 仍按模板“始终向右走”；
3. Move 阶段触碰陷阱，被传送回检查点；
4. 动作序列状态变为 `Aborted`。

#### 根因

`统一双向渡越` 把一次训练中的局部经验写成了过度绝对的通用规律。它缺少至少三类边界信息：

1. 角色当前位于陷阱哪一侧；
2. 平台当前停在哪一侧；
3. “始终向右”成立的地图坐标前提。

#### 影响

如果未来在不同初始位置或不同地图复用该技能，Agent 可能继续把“始终向右走”当作通用规律，导致误用。

#### 正向补充

这次失败后，Agent 并没有卡死，而是观察世界事件日志，重新分析平台周期，并在平台回到左侧后执行了可行序列。随后用户要求它回去时，它还生成了向左版本的动作序列并成功返回。

因此，问题不是“技能复用无效”，而是：

- `标准渡越` 这类带有方向参数和出发侧判断的模板是有效的；
- `统一双向渡越` 这类强经验结论需要降级为“特定场景经验”，不能作为默认优先模板；
- 技能系统需要保存“适用前提 / 禁用场景 / 验证方式”。

#### 判断

下版本需要让技能模板具备“适用前提 / 禁用场景 / 验证方式”的字段，或者强化 `usage_notes` / `step_explanations` 对适用边界的要求。`统一双向渡越` 不建议继续作为高优先级模板注入，除非它明确声明只适用于已经验证过的同一布局与同一侧向目标。

---

### 3.5 P1：`FollowTarget` 仍被误用一次，但不是主线问题

#### 现象

日志中 Agent 在早期连续失败后仍尝试：

```text
follow_target_cmd
```

这与用户要求“不使用其他运动工具”冲突，也说明 `FollowTarget` 描述虽已改善，但仍不足以完全压制误用。

#### 影响

本次它很快回到 ActionSequence 主线，并未阻塞最终成功，因此不是 P0。

#### 建议

下版本可结合“训练任务约束”处理：当用户明确“仅使用一个动作序列”时，Agent 应将除 ActionSequence 以外的运动工具视为禁用，而不仅仅依赖工具描述。

---

### 3.6 P1：monitor index 与持续观察状态仍有可用性问题

#### 现象

日志早期：

```text
get_monitor_records_cmd monitor_index: 0
[获取观察记录失败] monitor[0]不存在
```

随后使用 `monitor_index: 1` 才成功。

持续观察后期状态中也出现大量累计信息，例如观察时长、状态变化次数、未读记录等。

#### 影响

1. Agent 需要猜 monitor index；
2. 持续观察状态容易膨胀；
3. 未读记录长期累积会影响 prompt 质量。

#### 建议

`monitor_target_cmd` 应在返回中明确给出可用于后续查询的 `monitor_index`；持续观察状态应提供摘要化输出，或者提供清理/归档机制。

---

### 3.7 P2：Unity 失焦导致工具超时属于测试环境注意事项

#### 现象

日志中 13479 行后出现连续工具超时：

- `plan_action_sequence_cmd` 超时；
- `observe_cmd` 超时；
- `communicate_to_user` 超时。

用户已确认原因：切到 Cursor 后 Unity 运行场景不推进，因此无法响应 PythonServer 的工具请求。

#### 判断

这不是 Python / Unity 协议逻辑缺陷，不作为下版本 P0/P1 处理。

#### 建议

在测试说明中补充：

- 联调期间保持 Unity Game View / Play 窗口处于可推进状态；
- 或设置 Unity 后台运行（如适用）；
- 工具超时分析时需先排除 Unity 失焦导致的仿真暂停。

---

## 4. 从 YAML 角度的具体问题清单

### 4.1 `标准渡越` 模板中的假占位符问题

当前 YAML：

```yaml
condition: objects[0].State == "Idle" && objects[0].LeftPosition.x < 0
condition: myself.Position.x >= objects[0].RightPosition.x - 0
allowed_contact_obj_ids:
- 0
- 0
```

问题：

- `objects[0]` 同时代表平台和占位符；
- `[0, 0]` 无法表达“平台 + 陷阱”；
- `- 0` 无法表达 offset；
- `> 0` 无法表达 exit threshold；
- 模板复用依赖自然语言解释，结构本身不可自描述。

### 4.2 `统一双向渡越` 模板固定 `right` 的泛化风险

当前 YAML：

```yaml
direction: right
usage_notes: 核心突破：始终向右走！平台自动处理方向，无需区分左右。
```

问题：

- 这是当前训练场景的经验，不一定是一般规律；
- 在新 Agent 复用测试中，平台停在右侧时套用该模板已经造成一次触碰陷阱；
- 缺少适用前提；
- 缺少失败时如何识别“不适用”的说明。

### 4.3 默认技能 `走到目标旁交互` 未迁移

当前 YAML：

```yaml
- action: Move
  params:
    target: '{target_name}'
```

问题：

- 与当前 ActionSequence schema 不一致；
- `step_explanations` 为空；
- 可能诱导 Agent 输出旧格式。

---

## 5. 下版本建议目标

### 5.1 目标 A：设计 ActionSkill 模板参数系统

需要解决：合法结构化存储与可读占位参数之间的矛盾。

可选方向：

#### 方向 A1：新增 `template_parameters`

示例：

```yaml
template_parameters:
- name: platform_index
  type: object_index
  desc: 自动移动平台在 objects 中的序号
  example: 3
- name: trap_index
  type: object_index
  desc: 陷阱在 objects 中的序号
  example: 2
- name: exit_threshold
  type: float
  desc: 离开陷阱后的安全 x 坐标阈值
  example: 15.0
```

`action_sequence_template` 可保留合法示例值，并通过参数映射说明哪些值需要替换。

#### 方向 A2：模板字段允许占位对象

示例：

```yaml
condition_template:
  expr: objects[{platform_index}].State == "Idle" && objects[{platform_index}].LeftPosition.x < {left_threshold}
```

执行前必须由工具或 Agent 渲染成真实 `condition`。

#### 方向 A3：拆分“可执行动作序列”和“可复用模板”

- `action_sequence_template`：可复用模板，不要求直接执行；
- `example_action_sequence`：合法示例，用于参考；
- `render_rules`：如何从当前场景生成可执行序列。

初步建议：优先考虑 **A1 + A3 的组合**。既保持合法示例，又给参数系统明确结构。

---

### 5.2 目标 B：建立训练任务成功判定机制

需要从自然语言自评升级为结构化判定。

建议至少定义：

- `ActionSequenceExecutionResult.status`: `Completed` / `Aborted` / `Failed`；
- `is_task_success`: 是否达成本次训练目标；
- `failure_reason`: 失败原因；
- `interrupted_by`: 撞击对象 / 触发陷阱 / 超时 / 条件错误；
- `sequence_signature`: 动作序列签名，用于判断是否“同一个动作序列”；
- `success_streak`: 连续成功次数（可由训练任务管理器维护）。

如果短期不做系统级任务管理，至少在 prompt / ActionSequence 回顾中明确：

- 只有 `动作序列状态: Completed` 才可算一次完整成功；
- 任何 `Aborted` / `Failed` / `返回检查点` / `动作序列执行中断` 都必须清零连续成功；
- 如果用户要求“同一个动作序列”，必须检查动作序列结构和参数是否一致。

---

### 5.3 目标 C：默认技能 YAML 迁移到当前模型

建议把默认技能 `走到目标旁交互` 改成当前可执行 ActionSequence 风格，并补齐 `step_explanations`。

同时需要避免默认技能污染新格式技能索引。

---

### 5.4 目标 D：优化 monitor 工具可用性

建议：

1. `monitor_target_cmd` 返回明确 `monitor_index`；
2. `get_monitor_records_cmd` 支持按目标对象名 / object_index 查询，减少 index 猜测；
3. 状态描述中的持续观察记录改成摘要，避免长期膨胀；
4. 提供清空已读记录或停止观察机制。

---

### 5.5 目标 E：测试环境说明与后台运行提示

把 Unity 失焦导致工具超时加入测试注意事项：

- 不是协议 bug；
- 但容易误导日志分析；
- 后续如需稳定自动化测试，应让 Unity 后台继续运行或避免切走 Game View。

---

## 6. 建议优先级

### P0

1. **ActionSkill 模板参数系统**：解决占位符与合法 JSON 的根本矛盾；
2. **训练成功判定机制**：解决“连续 5 次 / 同一个动作序列”不能靠 Agent 自我统计的问题。

### P1

1. 默认技能 YAML 格式迁移与 `step_explanations` 补齐；
2. monitor index / 持续观察摘要优化；
3. 明确训练任务约束下的禁用工具边界，减少 `FollowTarget` 误用；
4. 为技能模板补充适用前提、禁用场景、验证方式，并降低“统一双向渡越”这类局部经验模板的默认优先级。

### P2

1. Unity 失焦导致工具超时的测试注意事项；
2. 技能模板适用边界、禁用场景、验证方式的表达增强。

---

## 7. 初步版本边界建议

建议 `v0.21.6` 聚焦两个主题：

1. **ActionSkill 模板参数化重构**；
2. **动作序列训练结果结构化判定**。

这两个问题是本轮日志和 YAML 中最影响后续训练效率与技能复用质量的核心问题。

如果版本范围需要收敛，可以先做：

- `template_parameters` 字段；
- `action_sequence_template` 合法示例值 + 参数映射；
- 工具描述要求 Agent 保存技能时必须填写参数表；
- ActionSequence 回顾中明确“Completed 才算成功，Aborted/Failed 清零”。

monitor 与默认技能迁移可作为同版本 P1，或拆到后续小版本。

---

## 8. 待讨论问题

1. ActionSkill 模板参数系统采用哪种设计？
   - A1：新增 `template_parameters`；
   - A2：允许 `condition_template` / 字符串模板；
   - A3：拆分可复用模板与可执行示例；
   - 或组合方案。
2. “同一个动作序列”的判定是否需要系统计算签名？
3. 训练成功计数由谁维护？
   - Agent 自己；
   - Unity ActionSequence；
   - Python 训练任务管理器；
   - 或仅在 prompt 中强化规则。
4. 默认技能 YAML 是否在 v0.21.6 中一并迁移？
5. monitor 工具优化是否纳入 v0.21.6，还是单独拆分？
6. Unity 失焦暂停是否需要在测试文档或运行时提示中显式说明？
