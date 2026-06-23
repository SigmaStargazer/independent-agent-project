# PRD — v0.21.6 ActionSkill 内联参数化模板与持续观察可用性优化

> **状态**：已实现  
> **对应分析**：`analysis.md`  
> **最后更新**：2026-06-23

---

## 1. 背景与目标

`v0.21.5` 已验证：ActionSequence 条件语法修复、`step_explanations`、记忆压缩等改动有效；导出的技能 YAML 可被新 AI Player 复用，新 AI Player 能基于已有技能较快完成浮板渡越任务。

但 `v0.21.6` 分析与讨论后确认，真正需要在本版本解决的问题收敛为三个：

1. **P0：ActionSkill 模板参数化表达能力不足**  
   当前 `action_sequence_template` 必须是合法 JSON / 合法动作序列，导致 Agent 保存模板时只能用 `0`、`right`、`> 0` 等合法示例值假装占位，模板结构本身无法说明哪些值需要替换、如何替换。

2. **P1：默认技能 YAML 仍是旧格式**  
   默认技能 `走到目标旁交互` 仍使用早期 `Move` / `Interact` + `params` 风格，与当前 ActionSequence 的 `move` / `interact` / `wait` 等 schema 不一致，可能污染技能索引。

3. **P2：monitor 工具可用性不足**  
   `monitor_target_cmd` 开始观察后，Agent 仍需猜 `monitor_index`，导致 `monitor[0]不存在` 这类无效调用；持续观察状态也容易累积较多信息。

本版本目标：让技能模板更自描述、更短、更易复用；让默认技能与当前 ActionSequence 统一；让持续观察工具更容易被 Agent 正确使用。

---

## 2. 范围

### 2.1 本期包含

- 将 ActionSkill 的 `action_sequence_template` 语义调整为**参数化动作序列模板蓝图**。
- 允许 `action_sequence_template` 中以内联字符串占位符表达可替换参数，例如 `{platform_index}`、`{direction}`、`{exit_threshold}`。
- 为技能工具增加宽松模板校验：保存技能模板时允许占位符，执行动作序列时仍严格要求合法 ActionSequence。
- 通过 `step_explanations.parameter_reason` 与 `usage_notes` 解释占位符含义和替换方法。
- 清理 `db/default_skills/default.yaml` 中旧格式默认技能。
- 为默认技能补齐当前 ActionSequence 风格的参数化模板与 `step_explanations`。
- 优化 monitor 工具返回与查询方式，降低 `monitor_index` 猜测成本。
- 为上述改动补充可自测脚本。

### 2.2 本期不包含

- 不新增 `TemplateParameter` / `template_parameters` / `replace_targets` 等独立参数表字段。
- 不做自动模板渲染器；Agent 复用模板时仍由它根据当前场景把占位符替换为实际值。
- 不做自动训练任务管理器。
- 不做连续成功次数、动作序列签名、训练验收计数的系统化维护。
- 不单独处理 `统一双向渡越` 的泛化风险；该问题视为模板质量与参数化表达的一种表现。
- 不继续调整 `FollowTarget`。
- 不处理 Unity 失焦导致工具超时的问题。
- 不新增 ActionSequence 动作类型。

---

## 3. 用户与场景

### 3.1 Agent 保存新技能模板

当 AI Player 从实践中总结出一个可复用动作序列时，它应把模板保存为参数化蓝图，而不是把当前场景中的对象序号、方向、阈值伪装成固定值。

期望结果：后续新 AI Player 看到模板时，可以直接从动作序列本体中识别 `{platform_index}`、`{trap_index}`、`{direction}` 等占位符，并结合逐步解释理解如何替换。

### 3.2 Agent 复用技能模板

当 Agent 复用 ActionSkill 模板时，它需要先把占位符替换为当前场景实际值，再调用 `plan_action_sequence_cmd`。

期望结果：`create_action_skill` / `add_action_skill_template` 中可以保存占位符模板；`plan_action_sequence_cmd` 中不能出现未替换占位符。

### 3.3 新 AI Player 导入默认技能

当新 AI Player 创建时，默认技能应与当前 ActionSequence schema 一致，并使用内联占位符表达参数化意图。

期望结果：prompt 中不再混入旧式 `Move` / `Interact` 模板，避免诱导 Agent 生成过期动作结构。

### 3.4 Agent 持续观察动态对象

当 AI Player 对平台等动态对象开启持续观察后，它应能明确知道后续该用哪个 `monitor_index` 查询记录，或者可以按对象编号 / 对象名查询观察记录。

期望结果：避免 `monitor[0]不存在` 这类猜测错误，提高观察动态规律的效率。

---

## 4. 功能需求

### 4.1 ActionSkill 内联参数化模板

#### 4.1.1 `action_sequence_template` 允许占位符

`ActionSequenceTemplate.action_sequence_template` 继续保持 JSON / YAML 结构，但不再要求能被当前 ActionSequence Pydantic schema 直接执行。

它可以包含字符串占位符：

```yaml
action_sequence_template:
- action: wait
  condition: objects[{platform_index}].State == "Idle" && objects[{platform_index}].LeftPosition.x < {start_side_threshold}
- action: move
  direction: "{direction}"
  condition: myself.Position.x >= objects[{platform_index}].RightPosition.x - {stand_offset}
  allowed_contact_obj_ids:
  - "{trap_index}"
  - "{platform_index}"
```

#### 4.1.2 占位符语法要求

占位符统一使用 `{snake_case_name}` 格式。

允许出现在：

- condition 字符串内部；
- 字符串字段中，如 `direction: "{direction}"`；
- 列表的字符串元素中，如 `allowed_contact_obj_ids: ["{trap_index}", "{platform_index}"]`；
- `usage_notes` 与 `step_explanations` 中。

不允许出现：

- 破坏 JSON / YAML 结构的裸占位符；
- 不清晰的单字母占位符；
- 带空格或中文的占位符名。

#### 4.1.3 模板保存与执行分离

技能保存工具采用宽松模板校验：

- 必须是 JSON 数组；
- 每个 step 必须是对象；
- 每个 step 必须有合法 action 类型：`wait` / `move` / `interact` / `select` / `input`；
- 占位符必须符合 `{snake_case_name}`；
- 对非占位字段做基础结构校验。

动作执行工具保持严格校验：

- `plan_action_sequence_cmd` 输入必须是可执行 ActionSequence；
- 不能包含未替换的 `{placeholder}`；
- `direction` 必须是真实 `left` / `right`；
- `allowed_contact_obj_ids` 必须是真实 int 列表。

#### 4.1.4 通过逐步解释说明参数

不新增独立参数表。参数含义由以下字段解释：

- `step_explanations.parameter_reason`：说明本 step 中占位符如何取值；
- `step_explanations.adjustment_hint`：说明参数如何根据场景调整；
- `usage_notes`：说明跨步骤的整体替换规则与注意事项。

#### 4.1.5 技能索引完整注入模板蓝图

ActionSkill 注入 system prompt 时，需要展示：

- 带 `{placeholder}` 的动作序列模板；
- 每步解释；
- 使用注意。

不额外展开参数表，避免 prompt 膨胀。

#### 4.1.6 导入导出保留内联占位符

技能 YAML 导出 / 默认技能加载 / 导入创建时均保留 `action_sequence_template` 中的占位符字符串。

---

### 4.2 默认技能 YAML 迁移

#### 4.2.1 默认技能使用当前 ActionSequence schema

将 `db/default_skills/default.yaml` 中 `走到目标旁交互` 的 `平地接近` 模板迁移为当前动作序列格式。

旧格式示例：

```yaml
- action: Move
  params:
    target: "{target_name}"
- action: Interact
  params:
    target: "{target_name}"
```

迁移后使用内联占位符模板：

```yaml
- action: move
  direction: "{direction}"
  condition: canInteract == true && nearestInteractableIndex == {target_interactable_index}
  allowed_contact_obj_ids: []
- action: interact
```

#### 4.2.2 默认技能补齐逐步解释

默认技能模板必须补齐 `step_explanations`，说明：

- 为什么先移动再交互；
- `{direction}` 如何根据目标位置替换；
- `{target_interactable_index}` 如何根据可交互列表确定；
- 为什么以 `canInteract` / `nearestInteractableIndex` 作为结束条件；
- 何时需要调整距离或改用其他模板。

`condition_reason` 对 `interact` 这类无 condition 的 action 允许为空。

#### 4.2.3 默认技能不引入独立参数表

默认技能不使用 `template_parameters`。参数解释应写在 `step_explanations.parameter_reason` 与 `usage_notes` 中。

---

### 4.3 monitor 工具可用性优化

#### 4.3.1 命名与术语统一

为避免“`monitor_index` / 观察目标[N] / 持续观察目标编号”三种叫法长期错位，统一约定如下：

| 位置 | 统一表达 |
|------|----------|
| 持续观察摘要中的列表项 | `持续观察目标[N]` |
| Agent 可见的中文语义 | `持续观察目标序号` |
| Python / proto / Unity 字段名 | `monitor_target_index` |

工具描述、工具返回文本、持续观察摘要、错误提示统一使用上述措辞，不再出现 `monitor_index`、`观察目标[N]`、`持续观察目标编号` 等旧称。

返回给 Agent 的文本一律使用「持续观察目标序号」这样的角色化中文表达，不直接暴露参数名。

#### 4.3.2 开始观察时明确告知“持续观察目标序号”

`monitor_target_cmd` 成功后，返回文本中必须用角色化语言告诉 Agent 它将被分配到的“持续观察目标序号”，例如：

```text
[持续观察结果]你已经开始持续观察:3. 自动移动的平台
这是你目前的第 1 个持续观察目标。
之后想回想这个目标的观察记录时，告诉自己「查看第 1 个持续观察目标的观察记录」即可。
```

如果目标已在观察中，应返回它当前对应的“持续观察目标序号”而不是新建一个。

#### 4.3.3 观察摘要使用统一术语

持续观察摘要从：

```text
观察目标[1]
对象: 3. 自动移动的平台
...
```

改为：

```text
持续观察目标[1]
对象: 3. 自动移动的平台
观察时长: 92.0秒
最后状态: Idle
最后变化: 0.5秒前
状态变化次数: 32次
未读记录: 33条
存储记录: 20条
（若要回想这个目标的详细观察记录，提供「持续观察目标序号 1」即可）
```

#### 4.3.4 不新增按对象查询接口

本期不再为 `get_monitor_records_cmd` 增加 `object_index` / `object_name`。

当上述命名与提示统一后，Agent 在持续观察摘要中已经能直接看到自己的“持续观察目标序号”，不需要再按对象查询。这样可以避免引入额外的协议字段、Unity 转发参数与歧义匹配逻辑。

`get_monitor_records_cmd` 入参从 `monitor_index` 重命名为 `monitor_target_index`，含义与持续观察摘要中的 `持续观察目标[N]` 完全对应。工具描述应明确：

```text
持续观察目标序号 N 来源于「持续观察中的目标」摘要里的「持续观察目标[N]」中的 N。
```

---

## 5. 非功能需求

- **兼容性**：历史技能模板中没有占位符时仍能正常读取、导出和索引注入。
- **简洁性**：不引入独立参数表，避免每个模板在 prompt 中展示大量重复信息。
- **可自测**：Python 侧模板宽松校验、默认技能加载、技能索引格式化必须可通过脚本自测。
- **执行安全**：可执行动作序列工具必须继续拒绝未替换占位符。
- **协议一致性**：如修改 `Tools/message.proto`，必须按协议生成流程更新生成文件与 Unity 分发代码。
- **编码**：所有文档、Python、C#、YAML 保持 UTF-8。

---

## 6. 验收标准

- [ ] `create_action_skill` / `add_action_skill_template` 可以保存含 `{placeholder}` 的 `action_sequence_template`。
- [ ] 技能模板保存时会拒绝非法占位符格式。
- [ ] `plan_action_sequence_cmd` 会拒绝仍含 `{placeholder}` 的可执行动作序列。
- [ ] 技能索引中展示内联占位符模板、逐步解释和使用注意，不展示独立参数表。
- [ ] 技能 YAML 导出后保留内联占位符。
- [ ] 默认技能 `走到目标旁交互` 不再使用旧式 `Move` / `Interact` + `params` 模板。
- [ ] 默认技能包含当前 ActionSequence 风格的内联占位符模板与 `step_explanations`。
- [ ] `monitor_target_cmd` 成功后，会以角色化语言告知 Agent 当前的“持续观察目标序号”，不暴露原始字段名。
- [ ] 持续观察摘要使用「持续观察目标[N]」表达，并明确说明可通过“持续观察目标序号”查询观察记录。
- [ ] `get_monitor_records_cmd` 入参重命名为 `monitor_target_index`，含义与摘要中的 `持续观察目标[N]` 一致。
- [ ] 自测脚本覆盖以上核心逻辑并通过。

---

## 7. 待确认问题

- [ ] monitor 相关 proto 字段、Python 入参、Unity 转发字段是否一并改名为 `monitor_target_index`？（当前方案默认改名。）

---

*本文档由 Cursor Agent 根据 `analysis.md` 与本轮讨论生成，确认前请勿直接据此改代码。*
