# v0.21.5 分析文档 — ActionSkill 可解释性、ActionSequence 表达稳定性与上下文裁剪

> **状态**：待讨论  
> **问题来源**：v0.21.4 验收测试日志 `Src/PythonServer/logs/prompts/小明/2026-06-21_09-35-48.log`、导出技能 `Src/PythonServer/db/default_skills/exports/小明_20260621_092241.yaml`、终端错误 `Range of input length should be [1, 30720]`  
> **最后更新**：2026-06-21  
> **阶段说明**：本文只做原因分析与方案候选，不作为 PRD；讨论确认后再进入 PRD。

---

## 1. 背景

v0.21.4 已验收通过，核心收益是：

1. AI Player 不再主要依赖 `FollowTarget` 处理浮板过陷阱；
2. 环境渲染已能清晰展示范围物体的“左边界 / 右边界”；
3. ActionSequence condition 已具备 `LeftPosition` / `RightPosition` 表达能力；
4. 日志中出现至少一次完整 ActionSequence 完成“借助浮板从左到右渡过陷阱并上岸”。

但验收日志也暴露出多个后续问题。v0.21.5 需要集中分析并解决这些问题，避免 Agent 只是在特定日志上下文中偶然成功，而无法稳定迁移、复用和训练。

---

## 2. 日志与导出技能中的关键证据

### 2.1 DynamicExpresso 字符串单引号问题

日志中 Agent 曾尝试使用 `LeftPosition` 条件：

```text
action_sequence: [
  {'action': 'move', 'direction': 'right', 'condition': 'displacement >= 4.3', 'allowed_contact_obj_ids': []},
  {'action': 'wait', 'condition': "objects[3].State == 'Idle' && objects[3].LeftPosition.x < 7"},
  {'action': 'wait', 'condition': "objects[3].State == 'Move' && objects[3].LeftPosition.x < 7"},
  {'action': 'wait', 'condition': 'objects[3].LeftPosition.x > 12'},
  {'action': 'move', 'direction': 'right', 'condition': 'displacement >= 2', 'allowed_contact_obj_ids': []}
]
```

随后 Unity 校验失败：

```text
action_sequence[1].condition校验出错: Character literal must contain exactly one character (at index 20).
action_sequence[2].condition校验出错: Character literal must contain exactly one character (at index 20).
```

根因是 DynamicExpresso 中字符串比较应使用双引号，例如：

```text
objects[3].State == "Idle"
```

而不是：

```text
objects[3].State == 'Idle'
```

Agent 自己随后意识到：

```text
单引号不被支持，让我用双引号试试。
```

但这个经验没有被稳定固化到工具 schema、condition 描述或 ActionSkill 生成规范中，导致后续仍可能复发。

### 2.2 Agent 在条件失败后退回“只用状态判断”

单引号导致边界条件校验失败后，Agent 的反应是：

```text
条件语法有问题。让我简化，只用状态判断。
```

这说明：

1. `LeftPosition` / `RightPosition` 能力已经提供，但 Agent 没有把错误归因为“字符串引号错误”；
2. 它把“边界条件表达失败”误判为“边界条件不好用 / 太复杂”；
3. 于是退回 `State + displacement` 的旧方案。

这会削弱 v0.21.4 的核心价值：范围边界字段虽然存在，但 Agent 不稳定使用。

### 2.3 多次失败说明技能尚未稳定泛化

同一份日志中出现多次中断和失败：

```text
[动作序列执行中断]撞击到物体: 自动移动的平台
[返回检查点]你触碰到: 2. 陷阱。已被传送回最近的检查点。当前动作序列已中断。
[动作序列执行中断]撞击到物体: 墙
```

这说明 Agent 已经能成功一次，但还不能稳定完成“连续成功 5 次”的训练目标。失败类型主要包括：

1. 上浮板时机 / 位移不稳，撞到浮板或踩不到浮板；
2. 下浮板位移不稳，撞墙或未安全落岸；
3. 过早执行动作，触碰陷阱；
4. 从右到左与从左到右的策略不是简单镜像，不能只反转方向。

### 2.4 无效工具调用 JSON

日志尾部出现：

```text
Invalid Tool Calls:
  plan_action_sequence_cmd
Error: Function plan_action_sequence_cmd arguments are not valid JSON.
```

具体表现是 JSON 字符串里嵌套的 condition 双引号未转义：

```json
{"condition": "objects[3].State == "Idle"}
```

这揭示了一个双层转义问题：

1. DynamicExpresso condition 内部需要双引号表达字符串；
2. 但当整个 action_sequence 作为 JSON 工具参数输出时，condition 内部双引号必须被正确转义；
3. 如果模型手写 JSON，容易生成无效 JSON。

这与“单引号问题”相互关联：Agent 为了满足 DynamicExpresso 需要双引号，但又容易破坏 JSON 参数格式。

### 2.5 导出的 ActionSkill 模板“知其然不知其所以然”

导出的技能片段：

```yaml
- name: 借助浮板渡过陷阱
  description: 利用自动移动的平台（浮板）渡过陷阱区域。适用于场景中有往复运动的平台和陷阱的组合。
  content: 核心思路：先在陷阱边缘等待浮板停靠，然后跳上浮板，随浮板移动到对面后下来。关键步骤：1) 移动到陷阱边缘（不进入陷阱）；2) 等待浮板在近侧停稳（State==Idle）；3) 跳上浮板（移动1.5m，允许接触浮板）；4) 等待浮板开始移动（State==Move）；5) 等待浮板到达远侧停稳（State==Idle）；6) 从浮板上下来（移动2m）。
  templates:
  - name: 从左到右渡过
    action_sequence_template:
    - action: wait
      condition: objects[3].State == "Idle"
    - action: move
      direction: right
      condition: displacement >= 4.3
      allowed_contact_obj_ids: []
    ...
    usage_notes: 浮板周期约11.8秒（左停3秒→右移2.9秒→右停3秒→左移2.9秒）。从右侧回左侧时方向改为left。需要确保从安全位置出发先移动到陷阱边缘再上浮板。
```

表面上它记录了动作序列，但它缺少关键解释：

1. 为什么第一步是等待 `Idle`；
2. 为什么从某些位置需要先移动到陷阱边缘，而另一些位置不需要；
3. 为什么上浮板时 `allowed_contact_obj_ids` 必须允许浮板，而上岸时通常不允许；
4. 为什么等待 `Move` 后还要等待 `Idle`；
5. `displacement >= 1.5` 和 `displacement >= 2` 的含义分别是什么，何时需要调整；
6. 什么情况下应该用边界条件 `LeftPosition` / `RightPosition` 替代固定 `displacement`；
7. 从右到左不是机械把 direction 改成 `left`，还需要根据当前岸、浮板停靠侧、陷阱边界、墙体位置重新判断。

这会造成用户指出的“知其然不知其所以然”：Agent 看到模板后能复制动作序列，但不知道每一步的意图、前置条件、失败风险和可变参数依据，因此很难变通和举一反三。

### 2.6 上下文裁剪仍触发输入长度错误

终端错误：

```text
Error code: 400 - {'error': {'message': '<400> InternalError.Algo.InvalidParameter: Range of input length should be [1, 30720]'}}
[MemoryManager][小明]存储记忆失败
```

这说明至少有一条发给某个 LLM 服务的输入超过了服务端限制 30720。

需要注意：这个错误出现在 `MemoryManager` 存储记忆链路，而不一定是 Agent 主 LLM 推理链路。当前 Agent 主链路在 `chatbot` 节点中确实调用了：

```python
system_tokens = await estimate_system_prompt_tokens(prompt_template, system_vars)
tools_tokens = get_tools_token_count(tools)
trimmed_messages = trim_messages_by_token(...)
response = await llm_with_tools.ainvoke(prompt)
```

但 `save_memory` 节点会把 `mem_to_save` 交给 `MemoryManager.save_memory`，Graphiti 再调用记忆 LLM 抽取事实。v0.20.12 调整的上下文裁剪主要作用于 Agent prompt，不一定约束了 Graphiti 写记忆时的输入长度。

此外，当前 prompt 保存逻辑也有一个容易误导排查的问题：

```python
# Prompt 保存：用全量 messages 重新渲染完整 prompt 并写入文件
prompt = await prompt_template.ainvoke({"messages": state['messages'], ...})
```

也就是说，保存下来的 prompt 日志可能是“全量 messages”，不是实际发送给主 LLM 的 `trimmed_messages`。因此仅看 prompt 日志长度，不能直接判断主 LLM 是否真的收到那么长的上下文。

---

## 3. 问题分类

### 3.1 必修问题 A：DynamicExpresso 字符串语法必须显式约束

问题：Agent 会写 `objects[3].State == 'Idle'`，Unity 报 `Character literal must contain exactly one character`。

用户已确认：

1. prompt / schema 描述必须改；
2. 需要进一步评估 Python 层和 Unity 层校验如何实现、是否好做。

影响：

1. 边界条件明明合理，却因字符串语法失败；
2. Agent 误以为复杂条件不可用，退回简单状态判断；
3. 影响 ActionSequence 成功率和边界字段使用率。

#### 3.1.1 prompt / schema 描述层

当前 `CONDITION_DESC` 仍存在反例式描述：

```text
State: 物体当前状态，如'Idle'、'Move'等。
# 示例：displacement >= 10 && myself.State == 'Move'
```

这会直接诱导 Agent 使用单引号。应改为明确规则：

```text
State: 物体当前状态，如 "Idle"、"Move" 等。DynamicExpresso 中字符串必须使用双引号，禁止使用单引号。
# 正确示例：displacement >= 10 && myself.State == "Move"
# 错误示例：myself.State == 'Move'
```

这部分很好做，风险低，是必改项。

#### 3.1.2 Python 层校验怎么实现

Python 层目前在 `agent_framwork/tools/action_sequence_model/model/base_action.py` 的 `StateChangeAction.validate_condition()` 中做 condition 静态校验。它会先用 `STRING_LITERAL_RE` 去掉字符串字面量，再检查变量和成员访问。

可以在现有 validator 的开头增加一条更明确的语义检查：

- 扫描 condition 中的单引号字符串字面量；
- 如果发现类似 `'Idle'`、`'Move'` 这种单引号字符串，直接抛出友好错误；
- 错误文案告诉 Agent：DynamicExpresso 字符串必须写成 `"Idle"`。

实现难度：低。

注意事项：

1. 不建议简单禁止所有 `'` 字符，因为自然语言文本或未来字段可能包含撇号；
2. 可以复用 / 拆分当前 `STRING_LITERAL_RE`，只匹配单引号字符串字面量；
3. Python 层只能检查成功进入工具参数解析的 action_sequence。如果模型已经生成了无效 JSON，工具函数进不来，这层校验无法触发；
4. 因此 Python 层校验适合解决“JSON 合法但 condition 语法不符合项目约定”的情况。

#### 3.1.3 Unity 层校验怎么实现

Unity 层目前在 `ConditionEvaluator.ValidateAll()` 中先做语义校验，再调用：

```csharp
bool ok = mInterpreter.Eval<bool>(step.Condition);
```

如果 DynamicExpresso 抛异常，会进入 catch，并返回：

```csharp
action_sequence[index].condition校验出错: {e.Message}
```

可以有两种做法：

1. 在 `ValidateAll()` / `Evaluate()` 调用 `Eval` 前增加 `ValidateSingleQuotedStringLiteral(condition)`；
2. 或者在 catch 中识别 `Character literal must contain exactly one character`，把错误翻译为更明确的中文提示。

更推荐第一种：前置校验。因为它能在规划阶段给出稳定、可控的错误信息，不依赖 DynamicExpresso 的英文异常文案。

实现难度：低到中。

注意事项：

1. Unity 层是运行时最终兜底，即使 Python 层漏掉，Unity 仍应能给 Agent 可理解的反馈；
2. `Evaluate()` 执行阶段也应复用同一校验，避免规划通过但执行时报英文异常；
3. 校验函数不应尝试自动改写 `'Idle'` 为 `"Idle"`，否则会掩盖工具参数生成问题；只提示即可。

#### 3.1.4 结论

这件事总体好做：

- prompt / schema 描述：低风险，必做；
- Python 层校验：低风险，建议做；
- Unity 层校验：低到中风险，建议做兜底；
- 不建议自动修复，只做明确报错和示例引导。

### 3.2 必修问题 B：ActionSequence 工具参数 JSON 与 condition 双引号冲突

问题：condition 需要双引号，但工具 JSON 也使用双引号，模型容易输出无效 JSON。

用户已确认：

1. 这块主要通过工具描述、condition 描述、强化示例来解决；
2. 不做硬编码修复；
3. 暂不引入额外 condition DSL 或复杂校验。

影响：

1. 产生 `Invalid Tool Calls`；
2. 打断训练流程；
3. 增加 Agent 自我修复负担。

本问题和 3.1 不同：

- 3.1 是 JSON 已经合法，工具收到了 condition，但 condition 本身用了 DynamicExpresso 不接受的单引号；
- 3.2 是模型生成的工具调用参数本身已经不是合法 JSON，Python 工具函数还没机会执行。

因此 3.2 不适合靠 Python 工具函数内部校验解决。更合理的处理是：

1. 在 `CONDITION_DESC` 中明确：condition 中字符串写双引号，但作为工具参数时由结构化工具调用负责转义，不能手写破坏 JSON；
2. 在 `plan_action_sequence_cmd` 的工具描述中加入完整正确示例，覆盖 `objects[3].State == "Idle"` 这类常见条件；
3. 在 ActionSequence 模型字段描述中避免出现单引号示例；
4. 在测试用例中加入“包含 State == \"Idle\" 的 action_sequence 可以通过 Python schema 并正确发送到 Unity”的自测。

结论：v0.21.5 不做 DSL 改造，不做自动修复无效 JSON；重点做描述和示例，降低模型生成错误的概率。

### 3.3 必修问题 C：ActionSkill 模板缺少“步骤意图 / 原因 / 参数依据”

问题：模板只记录动作序列和简短 usage_notes，不足以说明每一步为什么这么做。

用户已确认：方向 C2 是对的，即应考虑为模板增加逐步解释字段；但字段设计需要结合 ActionSequence 真实数据结构继续细化。

当前 ActionSequence Python 结构如下：

- `WaitAction`：需要 `condition`，表示原地等待直到条件成立；
- `MoveAction`：需要 `direction`、`condition`、`allowed_contact_obj_ids`，表示朝某方向移动直到条件成立；
- `InteractAction`：不需要 `condition`；
- `SelectAction`：需要 `selection`，不需要 `condition`；
- `InputAction`：需要 `input_text`，不需要 `condition`。

因此逐步解释不能只写“为什么 condition 这么写”。更完整的解释应覆盖：

1. 为什么这一步要用这个 action；
2. 这个 action 的参数应该怎么填；
3. 如果该 action 有 `condition`，为什么结束条件要这样写；
4. 如果是 `MoveAction`，为什么方向是 left/right，为什么允许或不允许接触某些物体；
5. 如果是 `SelectAction` / `InputAction`，为什么选择该选项或输入该内容；
6. 当前参数哪些是可泛化的，遇到不同场景时应该如何调整。

#### 3.3.1 建议字段：`step_explanations`

初步建议在 `ActionSequenceTemplate` 上新增：

```yaml
step_explanations:
- step_index: 0
  action_reason: 为什么这一步要使用这个 action
  parameter_reason: 这一步各参数为什么这样填
  condition_reason: 如果有 condition，解释为什么结束条件这样写；没有 condition 时为空或省略
  adjustment_hint: 换场景时如何调整这一步
```

字段含义：

- `step_index`：对应 `action_sequence_template` 中的步骤下标，必须一一对应；
- `action_reason`：解释“为什么要做这个动作”，例如为什么此时是 wait 而不是 move；
- `parameter_reason`：解释参数，例如 `direction`、`allowed_contact_obj_ids`、`selection`、`input_text` 为什么这样填；
- `condition_reason`：只对 `WaitAction` / `MoveAction` 必填，解释 condition 为什么能代表这一步完成；
- `adjustment_hint`：解释泛化时该如何变通，例如距离阈值要根据平台/陷阱边界调整。

#### 3.3.2 不同 action 的解释重点

`WaitAction` 的解释重点：

- 等待什么状态或空间关系出现；
- 为什么不能立刻行动；
- `condition` 是如何判断等待目标已经达成的；
- 如果 condition 只写 `State == "Idle"`，是否需要补充边界位置判断，避免左右端点混淆。

`MoveAction` 的解释重点：

- 为什么向 `left` 或 `right` 移动；
- 移动到哪里算完成；
- `condition` 为什么能判断“已经到位”；
- `allowed_contact_obj_ids` 为什么包含或不包含某些对象，例如上浮板时允许接触浮板，上岸时通常不应允许接触陷阱；
- 位移阈值是否是地图特化数值，换场景时如何根据边界距离重新计算。

`InteractAction` 的解释重点：

- 为什么此时需要交互；
- 交互对象应当是谁；
- 前置条件是什么，例如必须已经靠近目标或 `canInteract == true`。

`SelectAction` 的解释重点：

- 为什么选择该 `selection`；
- 选项编号是否依赖当前设备 UI，需要执行前重新观察确认。

`InputAction` 的解释重点：

- 为什么输入该 `input_text`；
- 输入文本是否是固定内容，还是应从当前任务/环境中提取。

#### 3.3.3 示例

```yaml
action_sequence_template:
- action: wait
  condition: objects[3].State == "Idle" && objects[3].LeftPosition.x < objects[2].LeftPosition.x + 1.0
- action: move
  direction: right
  condition: displacement >= 1.5
  allowed_contact_obj_ids:
  - 3

step_explanations:
- step_index: 0
  action_reason: 等待浮板在近侧停稳，避免移动中上板导致撞击或落空。
  parameter_reason: wait 没有方向和接触参数，只依赖结束条件判断等待是否结束。
  condition_reason: State == "Idle" 只能说明浮板停住，LeftPosition 限制用于确认它停在近侧而不是远侧。
  adjustment_hint: 换地图时不要只看 Idle，应根据陷阱边界和浮板边界重新判断“近侧停靠”的位置。
- step_index: 1
  action_reason: 从岸边移动到浮板上，为乘坐浮板过陷阱做准备。
  parameter_reason: direction 为 right，因为目标浮板在右侧；allowed_contact_obj_ids 包含 3，因为本步允许接触浮板，但不能允许接触陷阱。
  condition_reason: displacement >= 1.5 表示从岸边移动到浮板上的经验距离；更稳的写法应结合自身与浮板边界距离。
  adjustment_hint: 如果起点或浮板宽度变化，应调整 displacement，优先用边界位置推导而不是照抄固定数值。
```

#### 3.3.4 与 `usage_notes` 的关系

`usage_notes` 仍建议保留，用于模板级注意事项，例如：

- 适用前提；
- 常见失败原因；
- 整体节奏；
- 与其他模板的区别。

`step_explanations` 则负责逐步解释。两者不是替代关系。

#### 3.3.5 实现影响

需要改动的方向包括：

1. `ActionSequenceTemplate` 数据模型新增字段；
2. Kuzu schema / CRUD / 导入导出兼容新增字段；
3. `create_action_skill`、`add_action_skill_template`、`refine_action_skill` 工具参数新增逐步解释；
4. `load_action_skill` 输出逐步解释；
5. 默认技能 YAML 与已有导出格式兼容：旧模板没有 `step_explanations` 时按空列表处理。

风险：中等。主要风险不是实现难，而是字段设计一旦定下来会影响技能长期记忆格式。

### 3.4 必修问题 D：上下文裁剪为什么看起来“改了但没生效”

用户反馈：这一节之前的表述不好懂，需要重新解释。

先用白话说明：这次报错里的“上下文裁剪”可能不是同一件事。

系统里至少有两条会调用 LLM 的链路：

1. **Agent 主聊天链路**：Agent 思考下一步要做什么、要不要调用工具；
2. **MemoryManager 记忆写入链路**：一轮结束后，把本轮经历交给 Graphiti / 记忆 LLM，让它抽取事实和情景记忆。

v0.20.12 调整的上下文裁剪，主要是在第 1 条链路里裁剪 `state['messages']`，也就是避免 Agent 主聊天 prompt 太长。

但这次终端错误附近出现的是：

```text
[MemoryManager][小明]存储记忆失败
Error in generating LLM response: ... Range of input length should be [1, 30720]
```

所以更像是第 2 条链路爆了：不是 Agent 主聊天时输入太长，而是“写记忆”时给记忆 LLM 的输入太长。

#### 3.4.1 为什么主聊天裁剪不了记忆写入

Agent 一轮中会累积 `mem_to_save`，内容包括：

- 用户/环境输入；
- Agent 心理活动；
- 工具调用记录；
- 动作序列执行结果；
- 可能很长的动作序列日志和环境快照。

主聊天裁剪裁的是 `messages`，但 `mem_to_save` 仍可能很长。之后 `save_memory` 会把 `mem_to_save` 交给 `MemoryManager`，再由 Graphiti 组织自己的 LLM prompt。

也就是说：

```text
主聊天裁剪成功 ≠ 记忆写入一定不会超长
```

这就是之前“上下文裁剪有问题”的真正含义：不是说裁剪函数完全没运行，而是裁剪覆盖的链路不够，至少没有保护记忆写入这条链路。

#### 3.4.2 为什么 8000 字符也可能不够安全

`MemoryManager` 里有单 Episode 上限，但即使把正文截到某个字符数，也不一定安全。因为 Graphiti 写记忆时，不是只把这段正文原样发给模型，它还可能额外拼接：

- 抽取实体/关系的系统提示词；
- schema 说明；
- 当前已有实体信息；
- 其他辅助上下文；
- 本轮 episode 正文。

最终发给记忆 LLM 的总输入可能超过 30720。

所以问题不是简单的“正文 8000 字符是否小于 30720 token”，而是“Graphiti 最终组装出来的完整 prompt 是否超过模型限制”。

#### 3.4.3 为什么日志会让排查更混乱

当前 prompt 保存逻辑有一个现象：实际主聊天发送前用了 `trimmed_messages`，但保存 prompt 日志时又用全量 `state['messages']` 重新渲染。

所以日志里的 prompt 可能比真正发给主聊天 LLM 的 prompt 更长。

这会导致排查时容易误判：

- 看到日志很长，以为主聊天裁剪失败；
- 但真实错误可能发生在 MemoryManager 写记忆；
- 而 MemoryManager 的最终 prompt 当前又没有很好地记录 token 长度。

#### 3.4.4 v0.21.5 应该怎么理解这个问题

这不是一个单点 bug，而是三个相关问题：

1. **Agent 主聊天上下文预算**：要确认实际发送的 prompt 没超过当前模型限制；
2. **MemoryManager 写记忆预算**：要对 `mem_to_save` / Graphiti 输入做独立长度保护；
3. **日志可观测性**：要能看清楚报错的是哪条链路、实际发送了多长。

#### 3.4.5 候选处理方向

更容易理解的处理方向是：

1. 给 MemoryManager 单独加输入长度保护，不依赖 Agent 主聊天裁剪；
2. 当 `mem_to_save` 超长时，优先对它做压缩摘要，而不是简单截断；
3. 压缩后仍要保留角色在这段时间里经历了什么：看到了什么、听到了什么、想了什么、做了什么、外界如何反馈、哪些事还没完成；
4. 写记忆失败时不要整轮丢失，至少降级保存一段情景日记式短记忆；
5. prompt 日志里区分“实际发送给主聊天 LLM 的版本”和“全量调试版本”；
6. 打印或保存 token 估算，标清楚是 Agent 主聊天超长，还是 MemoryManager 写记忆超长。

截断和分段仍可作为兜底手段，但不应作为首选：

- 简单截断会丢失关键因果，尤其是动作序列前半段的决策理由；
- 简单分段可能破坏一次经历的整体语义，让 Graphiti 难以抽取完整事实；
- 压缩不应把经历过度抽象成几条经验结论，而应保留“我在某段时间里经历了什么”的情景日记；
- 原始流水账的方向是对的，因为 Agent 是生活在世界里的角色；需要压缩的是重复快照和冗余日志，而不是生活细节本身。

#### 3.4.6 当前倾向

v0.21.5 应优先解决 MemoryManager 写记忆超长，因为这次报错证据更指向该链路。当前倾向是：当 `mem_to_save` 超过记忆写入安全阈值时，先把它压缩成一段**情景日记式记忆**，再交给 Graphiti 写入长期记忆。

压缩目标不是把经历改写成任务报告，也不是只保留几条经验结论，而是让角色仍然记得自己在某段时间里生活过、观察过、思考过、行动过。例如应保留：

1. 当时的时间与场景；
2. 角色看到/收到/注意到的关键信息；
3. 角色当时的心理活动和判断；
4. 角色做过的动作、用过的工具；
5. 外界反馈、成功、失败、受阻；
6. 尚未完成的事；
7. 从这段经历中自然沉淀出的教训。

Agent 主聊天裁剪也需要检查，但它不应和记忆写入混为一谈。

### 3.5 新增问题 E：`_save_interrupt_memory` 未启用与打断场景下的 `mem_to_save` 膨胀

进一步检查 `agent_interuptible.py` 后确认：`_save_interrupt_memory()` 当前确实没有启用。

在 `ainterrupt()` 中相关调用是注释状态：

```python
# if not self._interrupt_memory_saved:
#     await self._save_interrupt_memory(reason)
#     self._interrupt_memory_saved = True
```

而当前实际逻辑是：

1. 打断时读取 LangGraph checkpoint；
2. 从 checkpoint 中取出 `mem_to_save`；
3. 给 `mem_to_save` 追加一行“当前思考被中断”；
4. 放入 `_resume_state`；
5. 下次 `astart()` 时继续带着这段 `mem_to_save` 恢复。

这意味着：如果 Agent 在一次完整图执行结束、走到 `save_memory` 节点之前反复被反馈打断，`mem_to_save` 会跨多次打断持续累积。结合动作序列反馈、环境快照、工具调用记录，这确实可能成为 MemoryManager 超长的直接原因之一。

#### 3.5.1 直接启用 `_save_interrupt_memory` 的收益与风险

收益：

1. 每次打断时能尽早把当前 `mem_to_save` 入队写入，避免无限增长；
2. 即使之后 Agent 崩溃或再次被打断，也不会完全丢失前面那段经历；
3. 实现上已有函数雏形，改动看似不大。

风险：

1. **情景割裂**：打断发生在思考或工具链中间，直接写入 Graphiti 可能把一个完整情景切成多个 episode；
2. **前因后果不足**：某些被切出来的 `mem_to_save` 只包含一段观察或半截工具调用，Graphiti 可能抽取出不完整甚至误导的事实；
3. **写入过碎**：频繁反馈会导致很多短 episode，情景检索时噪声变多；
4. **重复或断裂**：如果中断保存后恢复状态处理不严谨，可能出现重复保存或丢掉后续上下文。

因此，不建议简单地“每次打断都立刻调用 `_save_interrupt_memory` 并清空”。

#### 3.5.2 只做最终压缩的收益与风险

收益：

1. 尽量保持一次情景的整体性；
2. Graphiti 获得的是较完整的前因后果；
3. 长期记忆中 episode 数量不会因频繁打断暴涨。

风险：

1. 如果反馈风暴持续很久，`mem_to_save` 会在进入最终 `save_memory` 前变得非常长；
2. 越晚压缩，压缩输入本身越容易超出压缩模型限制；
3. 运行中如果 Agent 被关闭，尚未保存的 `mem_to_save` 可能丢失。

因此，也不建议只等最终 `save_memory` 才处理。

#### 3.5.3 当前倾向：运行态“滚动压缩”优先，而不是“打断即落库”

更稳妥的折中策略是：

1. 不把 `_save_interrupt_memory` 简单恢复成“每次打断都写 Graphiti”；
2. 在打断恢复前或恢复后，对 `_resume_state.mem_to_save` 做长度检查；
3. 如果 `mem_to_save` 超过运行态阈值，不落库，而是先压缩成“滚动情景日记”；
4. 压缩后的情景日记继续留在 `_resume_state.mem_to_save` 中，等待完整图执行结束后再统一 `save_memory`；
5. 最终写入前，MemoryManager 仍保留超长压缩兜底。

换句话说：

```text
频繁打断时：长 mem_to_save → 滚动情景日记压缩 → 继续作为同一情景的上下文
最终保存时：压缩后的 mem_to_save → MemoryManager → Graphiti
```

这样可以同时缓解两个担忧：

- 不会因为每次打断都落库而把完整情景切得太碎；
- 也不会让 `mem_to_save` 在反馈风暴中无限增长。

#### 3.5.4 `_save_interrupt_memory` 的定位建议

`_save_interrupt_memory` 不建议作为常规打断路径启用，但可以保留或改造为兜底能力：

1. Agent `afinish()` / SceneStop 前，如果仍有未保存 `mem_to_save`，可考虑保存一份“中断未完成情景摘要”；
2. 进程退出、场景停止、Agent 被移除等不可恢复场景，比普通 feedback interrupt 更适合落库；
3. 普通反馈打断仍以 `_resume_state` + 滚动压缩为主。

#### 3.5.5 与 MemoryManager 压缩的关系

滚动压缩和 MemoryManager 压缩不是互斥关系：

- Agent 运行态滚动压缩：解决“还没到 save_memory 就已经太长”的问题；
- MemoryManager 写入前压缩：解决“即将写入 Graphiti 仍然太长”的问题。

两者已确认使用同一套情景日记式压缩结构，但触发时机不同。

### 3.6 新增问题 F：重复计时器过短导致反馈风暴

用户补充：`set_timer_cmd` 如果 `timer_repeat = true`，必须要求 `delay_seconds` 超过 2 分钟。v0.21.4 测试中曾出现 6 秒 repeat 一次的计时器，不断反馈并打断 Agent，导致 Agent 几乎无法行动。

当前 `set_timer_cmd` 仅校验：

```python
if delay_seconds <= 0:
    return f"[{agent}]延迟秒数必须大于0"
```

工具描述也只说：

```text
当你需要周期性重复提醒时，可将 timer_repeat 设为 True。
```

这不足以阻止 Agent 创建高频重复计时器。

#### 3.6.1 风险

1. repeat timer 到期会通过反馈通道通知 Agent；
2. 反馈消息总是打断当前推理；
3. 过短 repeat 会造成反馈风暴；
4. 反馈风暴会反复触发 `ainterrupt()`，使 Agent 难以完成动作序列或工具链；
5. 还会加剧 `mem_to_save` 膨胀。

#### 3.6.2 当前倾向

v0.21.5 应加入硬性校验：

```text
if timer_repeat is True, delay_seconds must be >= 120
```

同时更新工具描述：

- repeat timer 是低频周期提醒；
- 不能用于几秒级轮询；
- 如果需要观察环境变化，应使用观察/动作序列/持续观察等更合适机制；
- 当 `timer_repeat=True` 时，`delay_seconds` 必须至少 120 秒。

这条规则适合硬编码校验，因为它不是表达式语法问题，而是运行时安全约束。过短 repeat 已被验证会直接破坏 Agent 行动能力。

### 3.7 非问题 G：ActionSkill 索引保持完整模板注入

用户已确认：这里不需要改。

当前 `ActionSkillManager.get_skill_index()` 的 `_format_template_index()` 会把模板动作序列完整注入 system prompt：

```python
lines.append("   动作序列：")
for step in tmpl.action_sequence_template:
    lines.append(f"     - {json.dumps(step, ensure_ascii=False)}")
if tmpl.usage_notes:
    lines.append(f"   使用注意：{tmpl.usage_notes}")
```

这正是 v0.21.1 的设计目标：给 Agent 完整模板数据，避免二次检索，实现快速反应。

因此 v0.21.5 不应把技能索引轻量化，也不应改成“只展示摘要、再调用 `load_action_skill` 二次检索”的模式。

后续如果新增 `step_explanations`，需要再决定解释字段是否也完整注入索引。但“完整动作序列模板必须注入”这一点保持不变。

---

## 4. 初步优先级建议

### P0：必须解决

1. DynamicExpresso 字符串必须双引号的问题；
2. 工具 JSON 与 condition 双引号冲突导致 Invalid Tool Calls 的问题；
3. MemoryManager 写入输入超长导致记忆失败的问题；
4. 频繁打断导致 `mem_to_save` 在恢复状态中持续膨胀的问题；
5. `timer_repeat=True` 的短间隔计时器造成反馈风暴的问题。

### P1：强烈建议解决

1. ActionSkill 模板增加 `step_explanations`，避免 Agent 机械套用；
2. prompt 日志区分“全量日志”和“实际发送给 LLM 的裁剪版本”；
3. 保持技能索引完整注入动作序列模板，不改为二次检索。

### P2：可讨论是否纳入本版

1. 将动作序列执行日志写入记忆前结构化摘要；
2. 对失败经验生成更规范的技能精进提示；
3. condition DSL / 辅助函数暂不纳入 v0.21.5，避免把 3.2 做复杂。

---

## 5. 待讨论问题

1. **单引号问题的修复层级**：已倾向 prompt/schema 必改，Python 层和 Unity 层都做友好校验；后续 PRD/方案需明确具体校验规则。
2. **condition 字符串与 JSON 转义冲突**：已倾向通过工具描述、condition 描述和示例解决，不做硬编码修复，不做 DSL 改造。
3. **ActionSkill 可解释性字段**：已倾向新增 `step_explanations`；仍需确认字段名和每个字段是否必填。
4. **`step_explanations` 是否注入技能索引**：完整动作序列模板继续注入；逐步解释是否也注入，需要在 PRD/方案阶段继续权衡。
5. **上下文裁剪问题边界**：v0.21.5 至少要解决 MemoryManager 写记忆超长；是否同时增强 Agent 主 prompt 日志可观测性需要确认。
6. **记忆写入超长处理策略**：当前倾向为超长后压缩 `mem_to_save`，截断/分段只作为兜底；后续 PRD/方案需明确压缩触发阈值与摘要结构。
7. **动作序列执行日志是否应该完整进入长期记忆**：当前倾向是不完整保存流水账，而是压缩为关键失败/成功原因与可复用经验。
8. **打断时是否启用 `_save_interrupt_memory`**：当前倾向是普通 feedback interrupt 不直接落库，只做滚动压缩；`afinish()` / SceneStop 等不可恢复场景可作为兜底保存。
9. **重复计时器最小间隔**：当前倾向是 `timer_repeat=True` 时硬性要求 `delay_seconds >= 120`，一次性 timer 不受该限制。

---

## 6. 暂不进入 PRD 的原因

本版本问题横跨：

- ActionSequence condition 表达规则；
- LangChain 工具调用 JSON 格式；
- ActionSkill 数据模型与导出格式；
- system prompt 技能索引注入策略；
- Agent 主 prompt 裁剪；
- MemoryManager / Graphiti 写入长度控制；
- prompt 日志可观测性。

这些问题存在设计取舍，尤其是如何修改 ActionSkill 数据结构、`step_explanations` 是否注入技能索引、以及如何处理记忆写入超长，需要先讨论清楚。讨论确认后再生成 PRD 与技术方案。
