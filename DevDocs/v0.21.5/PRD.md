# PRD — v0.21.5 ActionSequence 表达稳定性、ActionSkill 可解释性与记忆压缩

> **状态**：已确认  
> **对应需求**：用户口述 + `analysis.md`  
> **最后更新**：2026-06-22

---

## 1. 背景与目标

v0.21.4 已解决范围物体边界表达的基础能力问题：AI Player 能在 ActionSequence condition 中使用 `LeftPosition` / `RightPosition`，并曾成功完成一次借助浮板从左到右渡过陷阱。

但 v0.21.4 验收日志暴露出新的稳定性问题：

1. Agent 会在 DynamicExpresso condition 中使用单引号字符串，例如 `objects[3].State == 'Idle'`，导致 Unity 报 `Character literal must contain exactly one character`；
2. Agent 在遇到 condition 语法错误后，容易放弃边界条件，退回只用 `State + displacement` 的不稳定策略；
3. 当 condition 中必须使用双引号时，工具调用 JSON 又容易因双引号未正确转义而变成 `Invalid Tool Calls`；
4. ActionSkill 模板只有动作序列和简短 `usage_notes`，Agent 能照抄步骤，却不理解每一步为什么这样做，泛化能力不足；
5. MemoryManager 写入记忆时出现 `Range of input length should be [1, 30720]`，说明 `mem_to_save` 或 Graphiti 组装出的记忆 LLM 输入仍可能超长；
6. `_save_interrupt_memory()` 当前未启用，频繁反馈打断会让 `mem_to_save` 在多次恢复中持续累积；
7. `set_timer_cmd(timer_repeat=True)` 缺少最小间隔限制，过短 repeat timer 会制造反馈风暴并持续打断 Agent。

本版本目标：

- 让 ActionSequence condition 的字符串语法更稳定、更清楚；
- 让 ActionSkill 模板记录“每一步为什么这样做”，提升 Agent 复用和变通能力；
- 让 MemoryManager 在 `mem_to_save` 超长时优先压缩为结构化经验摘要，避免记忆写入失败；
- 在频繁打断场景下对 `mem_to_save` 做运行态滚动压缩，避免一直等到最终保存才处理；
- 限制重复计时器的最小间隔，避免 timer feedback 风暴让 Agent 无法行动；
- 保持 v0.21.1 的快速反应设计：ActionSkill 索引继续向 Agent 提供完整动作序列模板，不改成二次检索。

---

## 2. 范围

### 2.1 本期包含

1. **DynamicExpresso 字符串规则强化**
   - 更新 condition 相关 prompt / schema 描述；
   - 明确字符串必须使用双引号；
   - 移除或修正所有单引号字符串示例；
   - Python 层和 Unity 层提供友好校验或错误提示。

2. **ActionSequence 工具调用示例强化**
   - 强化 `plan_action_sequence_cmd` 等相关工具描述；
   - 提供包含 `objects[i].State == "Idle"` 的正确示例；
   - 降低模型生成无效工具 JSON 的概率。

3. **ActionSkill 模板逐步解释能力**
   - 为 ActionSequenceTemplate 增加 `step_explanations`；
   - 每个步骤解释“为什么使用该 action、参数为什么这样填、condition 为什么这样写、换场景如何调整”；
   - `usage_notes` 保留为模板级注意事项；
   - 导入、导出、创建、追加、精进、加载技能时支持该字段。

4. **MemoryManager 超长记忆压缩**
   - 当 `mem_to_save` 超过安全阈值时，优先压缩为结构化经验摘要；
   - 压缩摘要保留本轮目标、关键观察、工具/动作结果、成功/失败原因、可复用经验；
   - 截断/分段只作为兜底，不作为首选。

5. **运行态打断记忆保护**
   - 不直接把 `_save_interrupt_memory()` 恢复为每次打断都落库；
   - 在频繁打断导致 `mem_to_save` 过长时，优先做滚动压缩并继续保留在恢复状态中；
   - 将 `_save_interrupt_memory()` 定位为场景停止、Agent 结束等不可恢复场景的兜底能力。

6. **重复计时器安全约束**
   - `set_timer_cmd(timer_repeat=True)` 时，`delay_seconds` 必须至少 120 秒；
   - 工具描述明确 repeat timer 不能用于几秒级轮询。

7. **可观测性增强**
   - 记录或输出关键长度信息，帮助区分 Agent 主聊天超长和 MemoryManager 写记忆超长；
   - 如调整 prompt 保存逻辑，应区分实际发送版本和全量调试版本。

### 2.2 本期不包含

1. 不新增 ActionSequence condition DSL / 辅助函数；
2. 不自动把单引号 condition 改写成双引号，只做提示和校验；
3. 不改变 `FollowTarget` 的工具语义；
4. 不新增默认“浮板过陷阱”技能；
5. 不把 ActionSkill 索引改为轻量摘要 + 二次 `load_action_skill`；
6. 不修改 `Tools/message.proto`，除非方案评审后发现确有必要。

---

## 3. 用户与场景

### 3.1 AI Player 规划 ActionSequence

- 角色：AI Player / Agent；
- 场景：需要等待对象状态变化，例如浮板 `Idle` / `Move`；
- 期望结果：Agent 更倾向生成 `objects[3].State == "Idle"`，而不是单引号写法；如果仍写错，应收到明确提示，知道应使用双引号。

### 3.2 AI Player 复用动作技能模板

- 角色：AI Player / Agent；
- 场景：从 ActionSkill 索引中看到完整动作序列模板，需要迁移到当前环境；
- 期望结果：Agent 不只是照抄动作序列，还能理解每一步的目的、参数依据和可调整点。

### 3.3 开发者排查记忆写入失败

- 角色：开发者；
- 场景：终端出现 `Range of input length should be [1, 30720]`；
- 期望结果：可以从日志判断是 Agent 主聊天链路超长，还是 MemoryManager 写记忆链路超长；MemoryManager 应尽量压缩后写入，而不是直接丢失本轮记忆。

### 3.4 Agent 长期记忆沉淀

- 角色：AI Player / Agent；
- 场景：一次动作序列执行产生大量日志；
- 期望结果：长期记忆中保存的是结构化经验，而不是冗长流水账；失败经验也能被保留下来，供下次改进。

---

## 4. 功能需求

### 4.1 condition 字符串规则

1. condition 描述必须明确：DynamicExpresso 字符串必须使用双引号；
2. 正确示例应使用 `"Idle"` / `"Move"`；
3. 错误示例可明确指出 `'Idle'` / `'Move'` 不可用；
4. Python schema 校验应能识别常见单引号字符串字面量，并返回友好错误；
5. Unity condition 校验应能在规划阶段和执行阶段给出友好错误；
6. 校验不应自动修改 Agent 输入。

### 4.2 工具调用 JSON 示例

1. ActionSequence 相关工具描述应提供包含状态字符串的正确示例；
2. 示例应降低 condition 双引号破坏 JSON 的概率；
3. 不在工具函数内部修复无效 JSON，因为无效工具调用通常无法进入工具函数；
4. 不引入新的 condition DSL。

### 4.3 ActionSkill `step_explanations`

1. 每个 ActionSequenceTemplate 可包含 `step_explanations`；
2. `step_explanations` 在长期业务模型中必须使用 `ActionSequenceStepExplanation` dataclass，不使用 `List[dict]` 作为内部模型；
3. `step_explanations` 应按 `step_index` 对应动作序列步骤；
4. 对 `WaitAction` / `MoveAction`，应解释 condition 的含义和调整方式；
5. 对 `MoveAction`，应解释 `direction` 和 `allowed_contact_obj_ids`；
6. 对 `InteractAction`，应解释交互目标和前置条件；
7. 对 `SelectAction`，应解释 `selection` 的选择依据；
8. 对 `InputAction`，应解释 `input_text` 的来源或意义；
9. 旧模板没有 `step_explanations` 时应兼容为空列表；
10. `usage_notes` 继续保留，不被 `step_explanations` 替代。

建议字段：

```yaml
step_explanations:
- step_index: 0
  action_reason: 为什么这一步要使用这个 action
  parameter_reason: 这一步各参数为什么这样填
  condition_reason: 如果有 condition，解释为什么结束条件这样写；没有 condition 时为空或省略
  adjustment_hint: 换场景时如何调整这一步
```

### 4.4 ActionSkill 索引策略

1. ActionSkill 索引继续注入完整动作序列模板；
2. `step_explanations` 也完整注入技能索引；
3. 不改为“只展示摘要，完整内容再调用 `load_action_skill`”；
4. `load_action_skill` 必须展示完整 `step_explanations`。

### 4.5 MemoryManager 超长压缩

1. MemoryManager 应有独立于 Agent 主聊天的记忆写入长度保护；
2. 当 `mem_to_save` 超过安全阈值时，优先压缩为**情景日记式记忆**，而不是抽象经验条目；
3. 压缩后的记忆应尽量保留角色在一段时间里真实经历过的事情：
   - 当时的时间与场景；
   - 角色看到/听到/收到的关键信息；
   - 外界发生了什么变化；
   - 角色做了什么动作、使用了什么工具；
   - 角色当时怎么想、为什么这么做；
   - 行动过程中的反馈、成功、失败、受阻；
   - 尚未完成的意图或后续打算；
   - 从经历中自然沉淀出的教训或经验；
4. 压缩重点是去除重复环境快照、重复工具日志和无信息增量的冗余文本，不应把生活片段过度抽象成几条结论；
5. 压缩失败时允许兜底截断，但应有日志；
6. 写记忆失败不应导致 Agent 主流程崩溃。

### 4.6 运行态打断记忆保护

1. 系统应确认 `_save_interrupt_memory()` 当前不是常规打断路径；
2. 不应简单改成“每次 feedback interrupt 都把 `mem_to_save` 直接写入 Graphiti 并清空”；
3. 当频繁打断导致 `_resume_state.mem_to_save` 超过运行态阈值时，应优先压缩为滚动情景日记；
4. 滚动情景日记继续保留在 `mem_to_save` 中，等待完整图执行到 `save_memory` 后统一写入；
5. `afinish()`、SceneStop、Agent 移除等不可恢复场景，可考虑用 `_save_interrupt_memory` 或等价机制保存未完成情景日记；
6. 滚动压缩应尽量保留前因后果和生活细节，避免把一个完整情景切碎成多个缺乏上下文的 episode。

### 4.7 重复计时器安全约束

1. `set_timer_cmd` 当 `timer_repeat=True` 时，`delay_seconds` 必须大于等于 120 秒；
2. 若 Agent 传入 `timer_repeat=True` 且 `delay_seconds < 120`，工具应拒绝并返回明确说明；
3. 工具描述必须说明 repeat timer 是低频周期提醒，不能用于几秒级轮询；
4. 工具描述应建议：需要观察环境变化时，应使用观察、动作序列或持续观察类机制，而不是短间隔 repeat timer。

### 4.8 可观测性

1. 记忆写入时应记录原始长度、压缩后长度、是否触发压缩；
2. 滚动压缩触发时应记录原始长度、压缩后长度与触发原因；
3. Graphiti 写入失败时应输出足够信息，便于判断是否仍是输入超长；
4. 如调整 prompt 日志，应区分实际发送 prompt 和全量调试 prompt；
5. 日志不得泄漏额外敏感配置。

---

## 5. 非功能需求

1. **兼容性**：已有 ActionSkill 数据、默认技能 YAML、导出 YAML 应可继续加载；缺少 `step_explanations` 时按空列表处理。
2. **稳定性**：MemoryManager 压缩失败时必须有兜底策略，不能使后台 worker 崩溃。
3. **可测试性**：Python 侧数据模型、工具参数解析、导入导出、记忆压缩应可自测。
4. **可观测性**：关键链路需输出可定位问题的日志。
5. **角色化表达**：ActionSkill 工具描述仍需面向“角色的经验学习”，避免数据库字段式冷冰冰描述。
6. **性能**：超长记忆压缩只在超过阈值时触发，避免每轮都额外调用 LLM。

---

## 6. 验收标准

- [ ] condition schema 描述中不再出现诱导单引号的示例；正确示例统一使用双引号。
- [ ] Python 层对 `objects[3].State == 'Idle'` 给出明确、可理解的错误提示。
- [ ] Unity 层对单引号字符串 condition 给出明确、可理解的错误提示。
- [ ] 包含 `objects[3].State == "Idle"` 的合法 ActionSequence 能通过 Python schema 校验。
- [ ] ActionSequence 工具描述包含正确的状态字符串示例。
- [ ] ActionSkill 模型使用 `ActionSequenceStepExplanation` dataclass 表达 `step_explanations`，不以 `List[dict]` 作为内部长期模型。
- [ ] ActionSkill 模型、创建、追加、精进、加载、导入、导出支持 `step_explanations`。
- [ ] 旧 YAML / 旧数据库记录缺少 `step_explanations` 时不会报错。
- [ ] `load_action_skill` 能展示每个模板的逐步解释。
- [ ] 技能索引仍包含完整动作序列模板，不改为二次检索模式。
- [ ] `mem_to_save` 超长时会触发压缩，压缩结果是保留时间线、场景细节、行动、心理活动与反馈结果的情景日记式记忆，而不是只剩抽象结论。
- [ ] 频繁打断导致 `_resume_state.mem_to_save` 超过阈值时，会触发运行态滚动压缩，而不是每次打断直接落库切碎情景。
- [ ] 记忆压缩失败时有兜底策略和日志，不导致主流程崩溃。
- [ ] `set_timer_cmd(timer_repeat=True, delay_seconds<120)` 会被拒绝，并返回清晰错误提示。
- [ ] `set_timer_cmd` 工具描述明确 repeat timer 最小间隔和适用场景。
- [ ] 能通过不依赖 Unity 的 Python 自测覆盖主要 Python 侧改动。
- [ ] Unity 侧 condition 错误提示需要通过 Unity 或可用的 C# 测试方式验证；如无法自动化，应明确需要联调验证。

---

## 7. 已确认决策

- [x] `step_explanations` 必须与 `action_sequence_template` 长度完全一致；旧数据可为空列表兼容。
- [x] `condition_reason` 对无 condition 的 action 允许为空。
- [x] `step_explanations` 完整注入技能索引。
- [x] 记忆压缩复用 Memory LLM 配置，但设置更小输入和输出预算。
- [x] 记忆压缩触发阈值 token 优先，字符数兜底。
- [x] 运行态滚动压缩和 MemoryManager 写入前压缩使用同一套情景日记式结构。
- [x] `_save_interrupt_memory` 只在 `afinish()` / SceneStop 等不可恢复场景启用；普通 feedback interrupt 不直接落库。

---

*本文档由 Cursor Agent 根据用户口述与 `analysis.md` 生成，确认前请勿直接据此改代码。*
