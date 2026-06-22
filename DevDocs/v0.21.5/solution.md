# 技术方案 — v0.21.5 ActionSequence 表达稳定性、ActionSkill 可解释性与记忆压缩

> **状态**：已实现  
> **依据 PRD**：`PRD.md`  
> **最后更新**：2026-06-22

---

## 1. 方案概述

本版本分三条主线实现：

1. **ActionSequence condition 表达稳定性**：修正 prompt/schema 中的字符串示例，在 Python 和 Unity 两侧对单引号字符串给出友好错误，避免 Agent 因 DynamicExpresso 语法坑放弃边界条件。
2. **ActionSkill 可解释性**：为 `ActionSequenceTemplate` 增加 `step_explanations`，让技能模板不仅记录动作序列，还记录每一步的行动理由、参数依据、condition 依据和泛化提示。
3. **MemoryManager 超长压缩**：将当前简单截断 `mem_to_save` 的策略升级为“超长后压缩为情景日记式记忆”，降低 Graphiti / 记忆 LLM 输入超长失败概率，同时保留角色生活记忆的连续性和细节感。
4. **运行态打断记忆保护**：不把 `_save_interrupt_memory()` 简单恢复成每次打断都落库，而是在频繁打断导致 `mem_to_save` 过长时做滚动压缩，尽量保持完整情景。
5. **重复计时器安全约束**：限制 `timer_repeat=True` 时的最小间隔，避免短周期反馈风暴使 Agent 无法行动。

本版本不修改协议 `Tools/message.proto`，不新增 ActionSequence DSL，不改变 ActionSkill 索引完整模板注入策略。

---

## 2. 影响范围

### 2.1 Python

- `agent_framwork/tools/action_sequence_model/core/types.py`
  - 修正 `CONDITION_DESC` 中的字符串示例；
  - 明确 DynamicExpresso 字符串必须使用双引号。

- `agent_framwork/tools/action_sequence_model/core/constants.py`
  - 可新增单引号字符串匹配正则，例如 `SINGLE_QUOTED_STRING_LITERAL_RE`。

- `agent_framwork/tools/action_sequence_model/model/base_action.py`
  - 在 `StateChangeAction.validate_condition()` 中增加单引号字符串友好校验。

- `agent_framwork/tools/base_tools.py`
  - 强化 `plan_action_sequence_cmd` 等 ActionSequence 工具描述和示例；
  - 为 `set_timer_cmd(timer_repeat=True)` 增加最小间隔校验与工具描述。

- `agent/tools/skill_tools.py`
  - `create_action_skill`、`add_action_skill_template`、`refine_action_skill` 增加 `step_explanations` 参数；
  - 参数解析和错误提示更新；
  - `load_action_skill` 输出逐步解释。

- `memory_system/action_skill_system/skill_model.py`
  - `ActionSequenceTemplate` 新增 `step_explanations: List[dict]`。

- `memory_system/action_skill_system/action_skill_manager.py`
  - Kuzu schema 新增 `step_explanations STRING` 字段；
  - CRUD / 导入 / 导出 / 格式化索引支持新字段；
  - 旧数据缺字段兼容。

- `agent_framwork/agents/agent_interuptible.py`
  - 确认 `_save_interrupt_memory()` 常规打断路径未启用；
  - 在打断恢复状态中增加 `mem_to_save` 运行态长度保护 / 滚动压缩；
  - 评估 `_save_interrupt_memory()` 是否仅用于 `afinish()` / SceneStop 等不可恢复场景兜底。

- `memory_system/memory_manager.py`
  - 替换当前简单截断逻辑；
  - 增加 `mem_to_save` 超长检测、压缩、兜底截断、日志。

- 新增或更新 Python 自测脚本
  - 建议新增 `test_v021_5_self_test.py`。

### 2.2 Unity

- `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/Action/ActionSequence/ConditionEvaluator/ConditionEvaluator.cs`
  - 增加单引号字符串前置校验；
  - `ValidateAll()` 与 `Evaluate()` 复用该校验；
  - 保持不自动改写，只返回友好错误。

### 2.3 协议

- `Tools/message.proto`：不改。

### 2.4 数据库 / 数据文件

- Kuzu 中 `ActionSequenceTemplate` 增加字段：`step_explanations STRING`。
- `db/default_skills/*.yaml` 与导出 YAML 增加可选字段 `step_explanations`。
- 旧数据兼容：缺少 `step_explanations` 时视为空列表。

---

## 3. 详细设计

## 3.1 ActionSequence condition 字符串规则

### 3.1.1 修改 `CONDITION_DESC`

当前 `types.py` 中存在诱导单引号的描述：

```text
State: 物体当前状态，如'Idle'、'Move'等。
# 示例：displacement >= 10 && myself.State == 'Move'
```

改为：

```text
State: 物体当前状态，如 "Idle"、"Move" 等。
DynamicExpresso 中字符串必须使用双引号，禁止使用单引号。

# 正确示例：displacement >= 10 && myself.State == "Move"
# 错误示例：myself.State == 'Move'
```

同时确保 ActionSequence 工具描述中出现的所有状态字符串示例都使用双引号。

### 3.1.2 Python 层校验

在 `constants.py` 中新增单引号字符串字面量匹配：

```python
SINGLE_QUOTED_STRING_LITERAL_RE = re.compile(
    r"'[^'\\]*(?:\\.[^'\\]*)*'"
)
```

在 `StateChangeAction.validate_condition()` 中，先于 `STRING_LITERAL_RE.sub(...)` 做检查：

```python
single_quote_match = SINGLE_QUOTED_STRING_LITERAL_RE.search(expr)
if single_quote_match:
    literal = single_quote_match.group(0)
    raise ValueError(
        "condition 中的字符串必须使用双引号，不能使用单引号；"
        f"请将 {literal} 改为 \"{literal[1:-1]}\""
    )
```

注意：

- 不简单禁止所有 `'` 字符；
- 只禁止单引号字符串字面量；
- 不自动修复；
- 该校验只能处理工具 JSON 已解析成功的情况。

### 3.1.3 Unity 层校验

在 `ConditionEvaluator.cs` 增加：

```csharp
private ConditionEvalResult ValidateSingleQuotedStringLiteral(string condition)
{
    if (string.IsNullOrWhiteSpace(condition))
        return null;

    var match = Regex.Match(condition, @"'[^'\\]*(?:\\.[^'\\]*)*'");
    if (!match.Success)
        return null;

    return new ConditionEvalResult
    {
        Status = ConditionEvalStatus.Error,
        ErrorMessage = $"condition 中的字符串必须使用双引号，不能使用单引号；请将 {match.Value} 改为双引号字符串。"
    };
}
```

在 `ValidateAll()` 中，放在现有语义校验之前：

```csharp
var semanticCheck = ValidateSingleQuotedStringLiteral(step.Condition);
if (semanticCheck != null)
{
    results.Add(semanticCheck);
    index++;
    continue;
}
```

在 `Evaluate()` 中也复用该校验，避免执行阶段才出现 DynamicExpresso 英文异常。

---

## 3.2 工具描述与 JSON 示例

### 3.2.1 原则

- 只通过描述和示例降低错误率；
- 不在工具内部修复无效 JSON；
- 不新增 condition DSL；
- 不引入额外复杂校验。

### 3.2.2 工具描述建议

在 `plan_action_sequence_cmd` 的 docstring 中补充：

```text
condition 是 DynamicExpresso 表达式。状态字符串必须写双引号，例如 objects[3].State == "Idle"。
不要写 objects[3].State == 'Idle'。
当你通过结构化工具参数传入 action_sequence 时，请保持 condition 是字符串字段，不要手写破坏 JSON 的嵌套引号。
```

示例要覆盖：

```python
[
    {
        "action": "wait",
        "condition": "objects[3].State == \"Idle\""
    },
    {
        "action": "move",
        "direction": "right",
        "condition": "displacement >= 1.5",
        "allowed_contact_obj_ids": [3]
    }
]
```

注意：docstring 中的示例是给模型看的自然语言/代码示例，最终实际工具调用仍由 LangChain 结构化工具机制处理。

---

## 3.3 ActionSkill `step_explanations`

### 3.3.1 数据模型

在 `skill_model.py` 中新增：

```python
@dataclass
class ActionSequenceStepExplanation:
    step_index: int = 0
    action_reason: str = ""
    parameter_reason: str = ""
    condition_reason: str = ""
    adjustment_hint: str = ""
```

并在 `ActionSequenceTemplate` 中使用强类型列表：

```python
step_explanations: List[ActionSequenceStepExplanation] = field(default_factory=list)
```

这里明确不采用 `List[dict]` 作为长期数据模型。`step_explanations` 是 ActionSkill 的核心结构化知识，后续会长期维护、导入导出、迁移和演进，因此应使用 `ActionSequenceStepExplanation` dataclass 表达稳定结构。

从 YAML、工具参数、Kuzu 字符串读取出来的原始 dict，只能作为 I/O 边界数据；进入业务模型前必须 normalize 为 `ActionSequenceStepExplanation` 实例。导出或写入数据库时再转换为 dict / JSON。

### 3.3.2 一致性规则

新增校验函数：

```python
def normalize_step_explanations(raw, step_count: int) -> list[ActionSequenceStepExplanation]:
    ...
```

规则：

1. `raw` 为空时返回空列表，用于兼容旧数据；
2. 非空时必须是 list；
3. 每项必须包含有效 `step_index`；
4. `step_index` 必须在 `[0, step_count - 1]`；
5. 非空列表必须覆盖每个 step，长度与 `action_sequence_template` 完全一致；
6. 字段缺失时补空字符串；
7. `condition_reason` 对无 condition 的 action 允许为空。

已确认规则：

- 旧数据兼容：允许空列表；
- Agent 新建/精进模板：要求非空且与 action step 完全一一对应；
- 内部读取：始终容错 normalize，但不能把新数据的缺失解释 silently 当作合格数据。

### 3.3.3 Kuzu schema

当前表：

```cypher
CREATE NODE TABLE IF NOT EXISTS ActionSequenceTemplate (
    uuid STRING,
    skill_uuid STRING,
    name STRING,
    group_id STRING,
    `description` STRING,
    `description_embedding` DOUBLE[],
    action_sequence_template STRING,
    usage_notes STRING,
    created_at STRING,
    updated_at STRING,
    PRIMARY KEY (uuid)
)
```

新增字段：

```cypher
step_explanations STRING
```

数据兼容策略：

当前项目仍处于开发阶段，用户已确认：现有 Kuzu 库和技能 YAML 可以随意删改，不需要按生产数据做强兼容迁移。

因此 v0.21.5 可以采用更直接的开发期策略：

1. 更新 `ActionSequenceTemplate` schema，加入 `step_explanations STRING`；
2. 如现有 Kuzu schema 不匹配，可以直接删除/重建开发库或重建相关表；
3. `db/default_skills/*.yaml` 与导出 YAML 可直接改为新格式；
4. 旧数据读取仍保留基本容错：缺少 `step_explanations` 时视为空列表，但不需要为旧库实现复杂非破坏式迁移。

注意：这是开发阶段策略。后续进入稳定版本或已有用户数据需要保留时，再补非破坏式迁移流程。

### 3.3.4 CRUD 改动

#### 插入模板

`_insert_template()` 写入：

```python
"action_sequence_template: $seq, usage_notes: $notes, step_explanations: $step_explanations, "
```

参数：

```python
"step_explanations": json.dumps(tmpl.step_explanations, ensure_ascii=False)
```

#### 从 dict 导入

`create_skill_from_dict()` 读取：

```python
step_explanations=t.get("step_explanations", []) or []
```

并 normalize。

#### 精进模板

`refine_skill()` 新增 `new_step_explanations` 参数。

当 `new_template` 或 `new_step_explanations` 存在时，应共同校验：

- 如果更新了动作序列，应建议同步更新逐步解释；
- 如果只更新逐步解释，动作序列不变，也允许。

#### 读取与格式化

`_row_to_template()` 读取 JSON 字符串，解析失败时返回空列表。

`to_full_dict()`、`to_export_dict()` 均输出 `step_explanations`。

`load_action_skill` 输出：

```text
逐步解释：
  [0] action_reason: ...
      parameter_reason: ...
      condition_reason: ...
      adjustment_hint: ...
```

### 3.3.5 工具参数

#### create_action_skill

新增参数：

```python
step_explanations: str
```

说明：JSON 数组字符串，每项包含 `step_index/action_reason/parameter_reason/condition_reason/adjustment_hint`。

#### add_action_skill_template

同上新增。

#### refine_action_skill

新增可选参数：

```python
new_step_explanations: str = ""
```

解析逻辑与 `new_template` 类似。

### 3.3.6 技能索引注入解释

已确认：保持完整动作序列模板注入，并且 `step_explanations` 也完整注入技能索引。

实现要求：

- `get_skill_index()` 中展示每个 template 的完整 action sequence；
- 紧随每个 action step 展示对应 `step_explanations`；
- 不改为“只展示摘要，完整解释再调用 `load_action_skill`”的二次检索模式；
- `load_action_skill` 仍展示完整逐步解释，用于主动查看完整技能。

---

## 3.4 MemoryManager 超长压缩

### 3.4.1 当前问题

当前 `_save_memory_impl()` 中有简单截断：

```python
MAX_CHARS_PER_EPISODE = 8000
if len(memory) > MAX_CHARS_PER_EPISODE:
    memory = memory[:MAX_CHARS_PER_EPISODE]
```

但仍出现 30720 输入限制错误，说明：

1. 截断不一定足够；
2. Graphiti 会在 episode_body 外再拼接自己的 prompt；
3. 简单截断会丢失关键因果，不适合作为长期记忆策略。

### 3.4.2 新增压缩流程

建议流程：

```text
_save_memory_impl(name, memory, curtime)
  → estimate memory tokens/chars
  → 未超阈值：直接 add_episode
  → 超阈值：compress_memory_text(name, memory, curtime)
      → 得到情景日记式压缩记忆
      → 若压缩记忆仍超阈值：兜底截断
  → add_episode(summary_or_original)
```

### 3.4.3 压缩触发阈值

建议配置：

```env
MEMORY_COMPRESS_ENABLED=true
MEMORY_COMPRESS_TRIGGER_TOKENS=12000
MEMORY_COMPRESS_TARGET_TOKENS=3000
MEMORY_COMPRESS_FALLBACK_CHARS=6000
```

解释：

- 触发阈值低于 30720，给 Graphiti 系统 prompt 留空间；
- 目标摘要控制在较短范围，便于抽取事实；
- token 估算失败时使用字符数兜底。

### 3.4.4 情景日记式压缩结构

这里不采用“经验总结优先”的摘要结构。AI Player 是生活在游戏世界里的角色，长期记忆应让它记得“某段时间里发生过什么”，而不是只留下几条抽象结论。

压缩 prompt 要求输出中文情景日记式文本，按时间线保留经历细节，例如：

```text
# 这段时间的经历

## 时间与场景
- 时间：...
- 地点/环境：...
- 当时我正在尝试：...

## 经历时间线
1. [时间或顺序] 我看到/收到/注意到：...
   我当时想到：...
   我因此做了：...
   外界反馈/结果是：...

2. [时间或顺序] 接着发生了：...
   我当时想到：...
   我因此做了：...
   外界反馈/结果是：...

## 重要细节
- 关键对象、位置、状态、距离、边界或速度：...
- 重要对话或反馈：...
- 我尝试过但失败/受阻的事情：...

## 当前未完成的事
- 我还没完成：...
- 我接下来可能要：...

## 从这段经历自然得到的经验
- ...
```

压缩目标：

1. 保留角色生活记忆的连续性和细节感；
2. 保留观察、心理活动、行动、工具调用、环境反馈之间的因果顺序；
3. 去掉重复环境快照、重复工具日志、无信息增量的冗余文本；
4. 不把一段经历过度抽象成“目标 / 结果 / 经验”几条结论；
5. 让 Graphiti 既能抽取事实，也能保留可检索的情景片段。

### 3.4.5 压缩 LLM 选择

候选方案：

1. 复用 Memory LLM 客户端配置；
2. 使用更便宜/更快的压缩模型；
3. 先使用简单本地启发式压缩，再交给 LLM。

已确认 v0.21.5 采用方案 1：复用 Memory LLM 配置，减少新配置复杂度；但压缩调用必须设置比 Graphiti 写入更小的输入和输出预算，避免压缩链路本身再次超长。第三方库接口与当前 Graphiti 客户端参数必须先实测，不能凭记忆写。

### 3.4.6 失败兜底

压缩失败时：

1. 输出日志：压缩失败原因、原始长度；
2. 使用结构化本地兜底：保留开头、结尾和关键关键词附近片段；
3. 最终仍超长时截断到 `MEMORY_COMPRESS_FALLBACK_CHARS`；
4. 不向 Agent 主流程抛出异常，除非 `wait_result=True`。

### 3.4.7 可观测性

建议日志：

```text
[MemoryManager][小明] memory length tokens=..., chars=..., compress=True
[MemoryManager][小明] compressed memory tokens=..., chars=...
[MemoryManager][小明] graphiti add_episode failed: ...
```

如条件允许，保存最近一次压缩前/后的 debug 文件，但默认关闭，避免日志膨胀。

---

## 3.5 运行态打断记忆保护

### 3.5.1 当前状态

`agent_interuptible.py` 中存在 `_save_interrupt_memory()`，但在 `ainterrupt()` 中调用代码已注释：

```python
# if not self._interrupt_memory_saved:
#     await self._save_interrupt_memory(reason)
#     self._interrupt_memory_saved = True
```

当前打断流程会把 checkpoint 中的 `mem_to_save` 追加中断原因后放入 `_resume_state`，下次启动继续携带。这能保持情景连续性，但在反馈风暴下会导致 `mem_to_save` 持续增长。

### 3.5.2 不采用“每次打断直接落库”

不建议简单启用 `_save_interrupt_memory()` 作为每次 feedback interrupt 的常规路径，原因：

1. 会把一个完整情景切成多个 episode；
2. 中间片段可能缺少前因后果；
3. 高频反馈会制造大量碎片记忆；
4. 可能增加 Graphiti 抽取错误事实的概率。

### 3.5.3 建议方案：滚动压缩 `_resume_state.mem_to_save`

新增运行态阈值，例如：

```env
AGENT_MEM_ROLLING_COMPRESS_ENABLED=true
AGENT_MEM_ROLLING_COMPRESS_TRIGGER_TOKENS=10000
AGENT_MEM_ROLLING_COMPRESS_TARGET_TOKENS=3000
```

在 `_initialize_resume_state()` 或 `ainterrupt()` 构造 `_resume_state` 前后，检查 `mem_to_save` 长度：

```text
old mem_to_save + interrupt_reason
  → 未超阈值：照常放入 _resume_state
  → 超阈值：压缩成滚动情景日记，再放入 _resume_state
```

滚动情景日记仍留在同一个 `mem_to_save` 中，不立即写 Graphiti。这样下一次恢复后，Agent 仍保留“到目前为止发生了什么”的生活化上下文。

### 3.5.4 压缩结构

已确认：运行态滚动压缩和 MemoryManager 写入前压缩使用同一套情景日记式结构。滚动压缩只是在文本中额外强调“这段经历尚未结束”。

滚动压缩的输出不应像任务报告，而应像角色自己的阶段性回忆：

```text
# 这段经历还没有结束

## 时间与场景
- ...

## 到目前为止发生的事
1. ...
2. ...

## 我当时的想法和行动
- ...

## 外界反馈
- ...

## 我还没完成的事
- ...
```

这样既能控制长度，又不会让 Agent 失去“我刚刚经历了什么”的角色连续性。

### 3.5.5 `_save_interrupt_memory` 的兜底定位

`_save_interrupt_memory()` 可保留并改造为不可恢复场景兜底：

- `afinish()`；
- SceneStop / AgentRemove；
- 进程退出前可控清理；
- 用户明确要求中止并保存当前经历。

普通 feedback interrupt 不直接落库。

---

## 3.6 重复计时器安全约束

### 3.6.1 工具描述

更新 `set_timer_cmd` docstring：

```text
当 timer_repeat=True 时，delay_seconds 必须至少 120 秒。
重复计时器是低频周期提醒，不能用于几秒级轮询；过短重复提醒会不断打断你的思考和行动。
如果需要观察环境变化，请优先使用观察、动作序列或持续观察类机制。
```

### 3.6.2 Python 硬校验

在现有校验后增加：

```python
if timer_repeat and delay_seconds < 120:
    return f"[{agent}]重复定时器的间隔必须至少为120秒，过短重复提醒会不断打断行动"
```

这条适合硬校验，因为它是运行时安全边界，不是模型表达偏好。

### 3.6.3 测试

新增自测：

- `PY-TIMER-001`：`timer_repeat=False, delay_seconds=6` 不因 repeat 规则被拒绝；
- `PY-TIMER-002`：`timer_repeat=True, delay_seconds=6` 被拒绝；
- `PY-TIMER-003`：`timer_repeat=True, delay_seconds=120` 通过本地校验并尝试发送请求；
- `SRC-TIMER-001`：工具描述包含 `120`、`重复`、`打断` 等关键提示。

---

## 3.7 Prompt 日志可观测性

当前 `_save_prompt_log()` 使用全量 `state['messages']` 重新渲染 prompt，可能与实际发送给 LLM 的 `trimmed_messages` 不一致。

建议：

1. 在 `chatbot` 节点实际裁剪后，保存实际发送 prompt；
2. 如仍需全量日志，单独保存为 debug 版本；
3. 文件名或头部标记：`sent` / `full`；
4. 写入 token 估算：system / tools / messages / total。

注意：该项是可观测性增强，不应阻塞 MemoryManager 压缩主线。

---

## 4. 实现步骤

### 4.1 第一步：condition 描述与校验

1. 修改 `types.py` 的 `CONDITION_DESC`；
2. 修改 ActionSequence 工具 docstring 示例；
3. 在 Python validator 中新增单引号字符串检查；
4. 在 Unity `ConditionEvaluator` 中新增单引号字符串检查；
5. 增加 Python 自测；
6. Unity 侧编译/联调验证。

### 4.2 第二步：ActionSkill `step_explanations`

1. 修改 `skill_model.py`；
2. 修改 `action_skill_manager.py` schema / CRUD / import / export / index / full load；
3. 修改 `skill_tools.py` 的工具参数和输出；
4. 更新默认技能 YAML 或测试 YAML；
5. 编写自测覆盖创建、追加、精进、导出、加载；
6. 确认旧数据兼容策略有效。

### 4.3 第三步：运行态滚动压缩与 MemoryManager 记忆压缩

1. 增加长度估算与配置读取；
2. 抽出共享摘要结构和压缩函数；
3. 在 `agent_interuptible.py` 的打断恢复流程中接入滚动压缩；
4. 实现 MemoryManager 写入前 `compress_memory_text()`；
5. 接入 `_save_memory_impl()`；
6. 增加压缩失败兜底；
7. 增加日志；
8. 编写可自测用例，使用超长 fake memory 验证滚动压缩、写入前压缩与兜底。

### 4.4 第四步：重复计时器安全约束

1. 更新 `set_timer_cmd` 工具描述；
2. 增加 `timer_repeat=True and delay_seconds < 120` 的硬校验；
3. 编写自测覆盖 repeat / non-repeat 两类 timer 参数。

### 4.5 第五步：可观测性与回归

1. 视确认结果调整 prompt 日志保存；
2. 运行全部 v0.21.5 自测；
3. 运行既有相关测试；
4. 整理需要 Unity 联调验证的项目。

---

## 5. 风险与回退

### 5.1 Kuzu schema 迁移风险

风险：`ActionSequenceTemplate` 新增字段涉及 Kuzu schema 变化。

当前判断：项目处于开发阶段，现有 Kuzu 库和技能 YAML 可随意删改，因此本风险不按生产数据迁移处理。

缓解：

- 开发期允许直接删除/重建 Kuzu 开发库或相关表；
- 允许直接改写默认技能 YAML / 导出 YAML 为新格式；
- 代码读取仍对缺少 `step_explanations` 的旧 dict 做空列表兼容，便于临时调试。

回退：

- 如后续需要保留真实用户技能数据，再补非破坏式迁移流程。

### 5.2 工具参数变多导致模型负担增加

风险：`create_action_skill` 等工具新增 `step_explanations` 参数后，模型生成工具参数更复杂。

缓解：

- 工具描述给出短而清晰的 JSON 示例；
- 参数解析错误返回友好提示；
- 允许先创建空解释吗需用户确认，建议新建模板时要求完整解释。

回退：

- 保留 `usage_notes`，即使 `step_explanations` 缺失，也不影响旧技能基本使用。

### 5.3 记忆压缩再次触发超长

风险：压缩 LLM 本身输入也可能超长。

缓解：

- 压缩前先按 token / 字符对原始 `memory` 做安全裁剪，优先保留开头、结尾和关键段；
- 压缩目标 token 设置较低；
- 失败后本地兜底摘要。

回退：

- 关闭 `MEMORY_COMPRESS_ENABLED`，退回当前截断策略；
- 或将阈值调低。

### 5.4 `step_explanations` 完整注入索引增加 prompt 长度

风险：完整解释进入 system prompt 后增加上下文压力。

缓解：

- 这是已确认的产品取舍：优先保证 Agent 快速反应和理解模板；
- 后续如出现 prompt 压力，应通过技能数量控制、单条解释写作规范、主聊天裁剪和记忆压缩处理，而不是改成二次检索；
- `step_explanations` 写作应清晰但避免冗长废话。

回退：

- 本版本不默认回退到“索引不注入解释”；如确需回退，应重新讨论 v0.21.1 快速反应目标。

### 5.5 滚动压缩丢失未完成意图

风险：运行态滚动压缩如果写得像最终总结，可能让 Agent 丢失“接下来还要做什么”。

缓解：

- 滚动摘要使用“当前未完成情景摘要”结构；
- 明确保留“尚未完成的意图”；
- 不在普通 feedback interrupt 时直接落库清空。

回退：

- 关闭 `AGENT_MEM_ROLLING_COMPRESS_ENABLED`，仅保留 MemoryManager 写入前压缩。

### 5.6 repeat timer 限制影响正常短倒计时

风险：有些短倒计时是合理的，例如 6 秒后提醒一次。

缓解：

- 只限制 `timer_repeat=True`；
- `timer_repeat=False` 的一次性短计时器仍允许；
- 错误提示说明如果需要短时间单次提醒，应关闭 repeat。

回退：

- 如 120 秒过长，可通过配置调整最小 repeat 间隔，但默认值保持 120 秒。

---

## 6. 测试用例

### 6.1 可自测：Python condition schema

- `PY-COND-001`：`objects[3].State == "Idle"` 通过校验；
- `PY-COND-002`：`objects[3].State == 'Idle'` 被拒绝，错误提示包含“双引号”；
- `PY-COND-003`：包含 `LeftPosition` / `RightPosition` 的合法 condition 通过；
- `PY-COND-004`：未知字段仍被拒绝，确保旧校验未失效；
- `PY-COND-005`：`objects[3].LeftPosition.X` 被拒绝，错误提示说明 `Vector2` 坐标字段必须使用小写 `.x / .y`；
- `PY-COND-006`：`objects[3].LeftPosition.x` 通过校验。

### 6.2 可自测：ActionSkill `step_explanations`

- `PY-SKILL-001`：创建技能时传入完整 `step_explanations`，可正常保存；
- `PY-SKILL-002`：追加模板时传入 `step_explanations`，可正常保存；
- `PY-SKILL-003`：精进模板时更新 `step_explanations`，版本号增加；
- `PY-SKILL-004`：导出 YAML 包含 `step_explanations`；
- `PY-SKILL-005`：导入缺少 `step_explanations` 的旧 YAML 不报错；
- `PY-SKILL-006`：`load_action_skill` 输出逐步解释。

### 6.3 可自测：MemoryManager 压缩与运行态滚动压缩

- `PY-MEM-001`：短 memory 不触发压缩；
- `PY-MEM-002`：超长 memory 触发 MemoryManager 写入前压缩；
- `PY-MEM-003`：压缩结果包含时间与场景、经历时间线、观察、心理活动、行动、外界反馈、未完成事项；
- `PY-MEM-004`：压缩结果不是只有抽象经验结论，仍保留角色在某段时间里的生活片段；
- `PY-MEM-005`：压缩 LLM 抛错时触发本地兜底，不抛崩主流程；
- `PY-MEM-006`：压缩后仍超长时触发最终截断，并记录日志；
- `PY-MEM-007`：打断恢复状态中的 `mem_to_save` 超过阈值时触发滚动压缩；
- `PY-MEM-008`：滚动压缩结果包含“尚未完成的意图”，不被写成最终完成总结；
- `PY-MEM-009`：普通 feedback interrupt 不直接调用 Graphiti 写入并清空 `mem_to_save`。

### 6.4 可自测：Timer 安全约束

- `PY-TIMER-001`：`timer_repeat=False, delay_seconds=6` 不因 repeat 规则被拒绝；
- `PY-TIMER-002`：`timer_repeat=True, delay_seconds=6` 被拒绝；
- `PY-TIMER-003`：`timer_repeat=True, delay_seconds=120` 通过本地校验；
- `SRC-TIMER-001`：`set_timer_cmd` 工具描述包含 repeat 最小 120 秒和避免频繁打断的说明。

### 6.5 可自测：文档 / 静态源码

- `SRC-001`：`CONDITION_DESC` 不再包含 `myself.State == 'Move'` 这类错误示例；
- `SRC-002`：ActionSequence 工具描述包含双引号状态示例；
- `SRC-003`：`ConditionEvaluator.cs` 包含单引号字符串友好校验函数；
- `SRC-004`：`ConditionEvaluator.cs` 包含 Vector2 大写坐标字段 `.X / .Y` 友好校验函数。

### 6.6 需要 Unity 联调

- `UNITY-COND-001`：规划阶段提交 `objects[3].State == 'Idle'`，Unity 返回中文友好错误；
- `UNITY-COND-002`：执行阶段如果遇到单引号 condition，也返回中文友好错误；
- `UNITY-COND-003`：合法双引号 condition 正常执行；
- `UNITY-COND-004`：规划阶段提交 `objects[3].LeftPosition.X < 8`，Unity 返回中文友好错误；
- `UNITY-COND-005`：规划阶段提交 `objects[3].LeftPosition.x < 8`，不因坐标字段大小写被拒绝；
- `UNITY-ACT-001`：浮板过陷阱训练中，Agent 能稳定使用双引号状态条件和小写 `.x / .y` 坐标字段，不再因表达式语法失败退回简单条件。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-22 | 已完成 v0.21.5 开发实现：ActionSequence condition 双引号说明与 Python/Unity 友好校验、ActionSkill `ActionSequenceStepExplanation` 强类型模型与存储/导入导出/索引注入、MemoryManager 情景日记式压缩与 Agent 打断滚动压缩、`set_timer_cmd` repeat 最小 120 秒约束。已新增并通过 `DevDocs/v0.21.5/test_v021_5_self_test.py` 自测；Unity 联调项仍需在客户端运行时验收。 |
| 2026-06-22 | 根据运行日志补充 Vector2 坐标字段大小写小修：`LeftPosition` / `RightPosition` / `Position` / `Velocity` 等 Vector2 坐标只能使用小写 `.x / .y`，Python schema 与 Unity `ConditionEvaluator` 均会拒绝 `.X / .Y` 并返回中文友好提示。已补充自测并通过。 |
| 2026-06-22 | 用户确认 v0.21.5 验收通过。遗留问题（ActionSkill 模板参数化、连续成功判定、monitor 记录累积、FollowTarget 误用压制、左右镜像模板泛化）转入后续版本处理。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
