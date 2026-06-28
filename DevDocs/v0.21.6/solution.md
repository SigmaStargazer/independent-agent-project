# 技术方案 — v0.21.6 ActionSkill 内联参数化模板与持续观察可用性优化

> **状态**：已实现  
> **依据 PRD**：`PRD.md`  
> **最后更新**：2026-06-23

---

## 1. 方案概述

本版本聚焦三个真实问题：

1. 将 ActionSkill 的 `action_sequence_template` 调整为**参数化动作序列模板蓝图**，允许内联 `{placeholder}`。
2. 迁移默认技能 YAML，使其完全使用当前 ActionSequence 风格，并通过内联占位符 + 逐步解释表达参数化意图。
3. 优化持续观察工具：统一参数命名（`monitor_target_index`）与对 Agent 暴露的术语（“持续观察目标[N]”/“持续观察目标序号”），并将返回文本改为更角色化的表达，不再向 Agent 暴露原始字段名。

整体原则：**模板保存与动作执行分离**。技能模板是可复用蓝图，可以包含占位符；真正调用 `plan_action_sequence_cmd` 时，必须已经替换为当前场景的真实值，并通过严格 ActionSequence 校验。

---

## 2. 影响范围

### 2.1 Python

- `agent/tools/skill_tools.py`
  - 调整 `_parse_action_sequence` 语义：保存技能模板时使用宽松模板校验。
  - 新增占位符扫描与校验 helper。
  - `create_action_skill`、`add_action_skill_template`、`refine_action_skill` 的工具描述改为支持内联占位符。
  - 不新增 `template_parameters` 入参。

- `agent_framwork/tools/base_tools.py`
  - `plan_action_sequence_cmd` 增加未替换占位符检查，防止 `{placeholder}` 进入可执行动作序列。
  - `monitor_target_cmd` docstring 与返回文本使用“持续观察目标序号”术语，并以角色化语言告知 Agent。
  - `get_monitor_records_cmd` 入参由 `monitor_index` 重命名为 `monitor_target_index`，docstring 全部使用“持续观察目标序号”。

- `memory_system/action_skill_system/skill_model.py`
  - 不新增 `TemplateParameter` 数据结构。
  - 保持 `ActionSequenceTemplate.action_sequence_template: List[dict]`。
  - 文档注释调整：该字段可以是参数化模板蓝图，不保证可直接执行。

- `memory_system/action_skill_system/action_skill_manager.py`
  - 不改 Kuzu schema。
  - 继续按 JSON 字符串存储 `action_sequence_template`。
  - `_format_template_index()` 不展示独立参数表，只展示带占位符的动作序列、逐步解释与使用注意。

- `db/default_skills/default.yaml`
  - 默认技能迁移到当前 ActionSequence 风格。
  - 使用内联占位符。
  - 补齐 `step_explanations`。

### 2.2 Unity

- `AIPlayer.cs`
  - `MonitorTarget` 成功返回时使用角色化文本告知 Agent 当前的“持续观察目标序号”；目标已在观察中时返回已有序号。
  - `GetMonitorRecords` 入参由 `monitorIndex` 改为 `monitorTargetIndex`，错误提示使用“持续观察目标[N]”。

- `RuntimeInfoRenderer.cs`
  - 持续观察摘要以 `持续观察目标[N]` 开头，并提示「若要回想这个目标的详细观察记录，提供「持续观察目标序号 N」即可」。
  - 不再展示原始字段名 `monitor_index` / `monitor_target_index`。

- `AgentManager.cs` / `AgentService.cs`
  - 同步 proto 字段重命名后的事件签名与转发参数。

### 2.3 协议

涉及 `Tools/message.proto`，仅做字段重命名：

```proto
message AgentGetMonitorRecordsRequest {
    string agent = 1;
    string request_id = 2;
    int32 monitor_target_index = 3; // 原 monitor_index 重命名
}
```

- 不新增 `object_index` / `object_name` 字段。
- 字段号 3 沿用，避免历史协议升级踩坑。
- 协议修改必须按既有流程：只改 `Tools/message.proto`，再运行生成脚本并同步 C# / Python 生成文件。

---

## 3. 详细设计

### 3.1 ActionSkill 内联参数化模板

#### 3.1.1 数据模型不新增字段

保持当前核心结构：

```python
@dataclass
class ActionSequenceTemplate:
    action_sequence_template: List[dict] = field(default_factory=list)
    step_explanations: List[ActionSequenceStepExplanation] = field(default_factory=list)
    usage_notes: str = ""
```

但调整 `action_sequence_template` 的语义：

- 旧语义：合法 ActionSequence 示例。
- 新语义：参数化动作序列模板蓝图，允许 `{placeholder}`。

不新增：

- `TemplateParameter`；
- `template_parameters`；
- `replace_targets`。

#### 3.1.2 占位符格式

统一格式：

```text
{snake_case_name}
```

推荐正则：

```python
PLACEHOLDER_RE = re.compile(r"\{[a-z][a-z0-9_]*\}")
```

还需要检测疑似非法占位符：

```python
ANY_BRACED_RE = re.compile(r"\{[^{}]+\}")
```

规则：

- `ANY_BRACED_RE` 能匹配但 `PLACEHOLDER_RE` 不能完整匹配时，视为非法占位符。
- 占位符只能作为字符串内容的一部分出现。JSON 里不能出现裸 `{direction}`。
- condition 字符串中允许出现：`objects[{platform_index}]`、`{exit_threshold}`。

#### 3.1.3 技能模板宽松校验

将 `agent/tools/skill_tools.py` 中用于技能保存的 `_parse_action_sequence` 调整为模板解析函数，例如：

```python
def _parse_action_sequence_template(raw: str) -> list[dict]:
    data = json.loads(raw)
    if not isinstance(data, list):
        raise ValueError("action_sequence_template 必须是 JSON 数组")
    known_actions = _get_known_action_names()
    for i, step in enumerate(data):
        _validate_template_step(step, i, known_actions)
    return data
```

合法 action 类型从 `ActionStep` Union 自动推导，避免在技能模板校验里维护硬编码列表：

```python
# 位置建议：agent_framwork/tools/action_sequence_model/model/action_sequence.py
# 或 action_sequence_model 模块的某个 public helper
from typing import get_args, Literal
from .action_sequence import ActionStep

def get_known_action_names() -> set[str]:
    """从 ActionStep Union 中收集所有合法 action 字面量。"""
    names: set[str] = set()
    union_members = get_args(ActionStep)[0]  # Annotated[Union[...], discriminator]
    for cls in get_args(union_members):
        action_field = cls.model_fields["action"]
        for value in get_args(action_field.annotation):
            names.add(value)
    return names
```

实现细节按当前 `ActionStep = Annotated[Union[...], Field(discriminator="action")]` 的结构实测确认（参照 §第三方库参数实测纪律）。

`_validate_template_step` 基础规则：

- step 必须是 dict。
- `action` 必须存在，且必须是纯字符串。
- `action` 不允许包含占位符（动作类型不能参数化）。
- `action` 必须出现在 `get_known_action_names()` 返回的集合中。
- `wait` / `move` 类（即 `StateChangeAction` 子类）通常应有 `condition`，但允许 condition 中含占位符。
- `move.direction` 可以是真实 `left` / `right`，也可以是 `"{direction}"`。
- `move.allowed_contact_obj_ids` 可以是 int 列表，也可以包含占位符字符串，如 `"{trap_index}"`。
- `select.selection` 可以是 int，也可以是占位符字符串。
- `input.input_text` 可以是普通字符串，也可以含占位符。
- 对所有字符串递归检查占位符格式。

注意：这里不调用完整 Pydantic ActionSequence 校验，因为模板中的 `"{direction}"`、`"{trap_index}"` 等本来就不是可执行值。

未来如果 `ActionStep` 增加新的 Action 类（如 `JumpAction`），只要按现有模式把它加入 `action_sequence.py` 的 `ActionStep` Union，技能模板宽松校验会自动识别它，不需要回头维护单独的允许列表。

#### 3.1.4 执行动作序列严格校验

`plan_action_sequence_cmd` 是执行入口，必须继续严格。

在它解析可执行动作序列前或后增加占位符扫描：

```python
def _contains_placeholder(value: Any) -> bool:
    ...
```

如果任何字段中仍包含 `{snake_case_name}`，直接返回错误：

```text
[动作序列规划失败] 你传入的是技能模板，不是可执行动作序列：仍包含未替换占位符 {platform_index}、{direction}。请先根据当前场景替换为真实 objects 序号、方向、数值后再规划。
```

这样可以保证：

- 技能库能保存模板蓝图；
- Unity 只收到可执行动作序列；
- 未替换模板不会进入执行层。

#### 3.1.5 step_explanations 承担参数解释

工具描述要求 Agent 在 `step_explanations.parameter_reason` 中解释本步骤出现的占位符。例如：

```yaml
- step_index: 1
  action_reason: 走上平台深处，避免站在边缘被甩下
  parameter_reason: >
    {direction} 根据目标岸方向替换；
    {platform_index} 替换为当前平台序号；
    {stand_offset} 一般取 0.5，用于走到平台内部。
  condition_reason: >
    走到平台右边界减去 {stand_offset} 的位置，表示已经站到平台内部。
  adjustment_hint: >
    如果平台较窄，{stand_offset} 可减小；如果容易掉下，可增大。
```

`usage_notes` 说明跨步骤替换规则，例如：

```text
复用前先观察当前场景，把 {platform_index} 替换为移动平台的 objects 序号，
把 {trap_index} 替换为陷阱序号，{direction} 根据目标岸方向填 left 或 right。
```

#### 3.1.6 技能索引注入

`_format_template_index()` 保持当前总体格式，但展示的是带占位符的模板：

```text
动作序列：
  - {"action": "move", "direction": "{direction}", ...}
这一步为什么这样做：
  参数依据：{direction} 根据目标相对方向替换...
```

无需额外“模板参数”章节，以避免 prompt 膨胀。

---

### 3.2 默认技能 YAML 迁移

#### 3.2.1 迁移目标

将 `走到目标旁交互` 的 `平地接近` 从旧格式迁移为当前 ActionSequence 风格的模板蓝图。

建议模板：

```yaml
action_sequence_template:
- action: move
  direction: "{direction}"
  condition: canInteract == true && nearestInteractableIndex == {target_interactable_index}
  allowed_contact_obj_ids: []
- action: interact
```

说明：

- `{direction}` 根据目标在自身左侧 / 右侧替换为 `left` / `right`。
- `{target_interactable_index}` 根据目标进入可交互列表后的编号替换，通常是 `0`。
- `interact` 不需要 condition。

#### 3.2.2 默认技能 step_explanations

两步解释：

1. `move`：走向目标，直到目标进入可交互范围。
2. `interact`：在目标进入可交互范围后触发交互。

`condition_reason` 对 `interact` 为空，符合 v0.21.5 已确认约定：无 condition 的 action 允许 `condition_reason` 为空。

#### 3.2.3 默认技能 usage_notes

`usage_notes` 中说明：

- `{direction}` 的替换方式；
- `{target_interactable_index}` 的替换方式；
- 如果目标不在同一平面，或存在障碍，不应强行使用该模板；
- 如果目标移动，执行前应重新观察确认方向。

---

### 3.3 monitor 工具优化

本期对 monitor 的优化只做命名/术语统一与角色化文本，不新增按对象查询接口。

#### 3.3.1 命名统一

| 旧名/旧表达 | 新统一表达 |
|-------------|-----------|
| `monitor_index`（proto / Python / C# 字段名） | `monitor_target_index` |
| “观察目标[N]” | “持续观察目标[N]” |
| “持续观察目标编号” | “持续观察目标序号” |

涉及位置：

- `Tools/message.proto`：`AgentGetMonitorRecordsRequest.monitor_index` 重命名为 `monitor_target_index`，按既有协议改造流程重新生成 Python / C# 代码并复制。
- `agent_framwork/tools/base_tools.py`：`get_monitor_records_cmd` 入参名改为 `monitor_target_index`，docstring 全部使用“持续观察目标序号”。
- Unity `AgentService` / `AgentManager` / `AIPlayer.GetMonitorRecords`：参数名同步为 `monitorTargetIndex`，对外日志和错误信息使用“持续观察目标序号”。
- `RuntimeInfoRenderer`：观察摘要以 `持续观察目标[N]` 开头。

注意：原 1-based 语义保持不变。

#### 3.3.2 `monitor_target_cmd` 成功返回角色化文本

`AIPlayer.MonitorTarget` 当前返回：

```csharp
return $"[持续观察结果]开始持续观察目标:{objectIndex}. {target.Name}";
```

调整为：

```csharp
int monitorTargetIndex;
if (existingIndex > 0)
{
    monitorTargetIndex = existingIndex;
    return
        $"[持续观察结果]你已经在持续观察:{objectIndex}. {target.Name}\n" +
        $"这是你目前的第 {monitorTargetIndex} 个持续观察目标。\n" +
        $"想回想这个目标的观察记录时,告诉自己「查看第 {monitorTargetIndex} 个持续观察目标的观察记录」即可。";
}

monitorTargetIndex = mObserveRuntimes.Count;
return
    $"[持续观察结果]你已经开始持续观察:{objectIndex}. {target.Name}\n" +
    $"这是你目前的第 {monitorTargetIndex} 个持续观察目标。\n" +
    $"想回想这个目标的观察记录时,告诉自己「查看第 {monitorTargetIndex} 个持续观察目标的观察记录」即可。";
```

不直接在返回文本里暴露 `monitor_target_index` / `monitor_index` / 工具函数名等技术字段。

#### 3.3.3 观察摘要使用统一术语

`RuntimeInfoRenderer.RenderObserveRuntimeSummary` 由：

```text
观察目标[1]
对象: 3. 自动移动的平台
观察时长:92.0秒
...
```

调整为：

```text
持续观察目标[1]
对象: 3. 自动移动的平台
观察时长:92.0秒
最后状态: Idle
最后变化:0.5秒前
状态变化次数:32次
未读记录: 33条
存储记录:20条
（若要回想这个目标的详细观察记录，提供「持续观察目标序号 1」即可）
```

不再展示原始字段名 `monitor_index` / `monitor_target_index`。

#### 3.3.4 `get_monitor_records_cmd` 入参重命名

```python
async def get_monitor_records_cmd(
    agent: Annotated[str, InjectedState("name")],
    tool_call_id: Annotated[str, InjectedToolCallId],
    monitor_target_index: int,
) -> str:
    """查看持续观察目标的观察记录。

    Args:
        monitor_target_index: 持续观察目标序号。
            数值来源于「持续观察中的目标」摘要里「持续观察目标[N]」中的 N，从 1 开始。

    返回:
        - 该持续观察目标的最近观察记录。
        - 如果序号不存在,会返回当前可用的「持续观察目标[N]」列表提示。
    """
```

Unity 侧 `AIPlayer.GetMonitorRecords` 在序号非法时，错误信息也使用“持续观察目标[N]”，例如：

```text
持续观察目标[3]不存在,你目前的持续观察目标包括:[1] 3. 自动移动的平台、[2] 5. 触发开关。
```

---

## 4. 实现步骤

### 4.1 ActionSkill 内联模板

1. 修改 `agent/tools/skill_tools.py`：
   - 将当前 `_parse_action_sequence` 拆分或改名为模板解析逻辑。
   - 新增占位符递归扫描 helper。
   - 保存技能模板时使用宽松模板校验。
   - 更新 `create_action_skill`、`add_action_skill_template`、`refine_action_skill` 的 docstring 与错误提示。
   - 明确说明模板占位符必须写在字符串中，执行前必须替换。

2. 修改 `agent_framwork/tools/base_tools.py`：
   - 为 `plan_action_sequence_cmd` 增加未替换占位符检测。
   - 如果发现占位符，返回友好错误，不进入 Unity 规划。
   - 更新 `plan_action_sequence_cmd` docstring，区分“技能模板”和“可执行动作序列”。

3. 修改 `skill_model.py`：
   - 不改字段。
   - 更新 `ActionSequenceTemplate` 注释，说明 `action_sequence_template` 可包含占位符，代表模板蓝图。

4. 修改 `action_skill_manager.py`：
   - 不改 schema。
   - 检查 `_format_template_index()` 是否能原样展示占位符。
   - 如有必要，增加一段固定提示：复用模板前必须替换 `{placeholder}`。

### 4.2 默认技能迁移

5. 修改 `db/default_skills/default.yaml`：
   - `走到目标旁交互` 改为当前 ActionSequence 风格。
   - 使用内联 `{direction}`、`{target_interactable_index}`。
   - 补齐 `step_explanations`。
   - 不增加 `template_parameters`。

6. 检查 `db/default_skills/exports/`：
   - 历史导出不批量修改。
   - 如本地测试使用旧导出创建新 Agent，应明确该导出仍可能包含旧格式。

### 4.3 monitor 优化

7. 修改 `Tools/message.proto`：
   - `AgentGetMonitorRecordsRequest.monitor_index` 重命名为 `monitor_target_index`，字段号沿用 3。

8. 按协议流程生成代码：
   - 运行 `1.genproto.cmd`。
   - 检查 `MessageDispatch.cs` 是否需要同步。
   - Rebuild `CSharpClient.sln`。
   - 运行 `2.copyprotocol.cmd`。

9. 修改 Python `base_tools.py`：
   - `get_monitor_records_cmd` 入参重命名为 `monitor_target_index`。
   - docstring 改用“持续观察目标序号”表述。
   - 设置 proto 新字段（请求构造处）。
   - `monitor_target_cmd` docstring 同步改用“持续观察目标序号”术语，并提示返回文本中会以角色化语言告诉 Agent 当前序号。

10. 修改 Unity：
    - `AgentService.cs` 事件签名 / 委托参数由 `monitorIndex` 改为 `monitorTargetIndex`。
    - `AgentManager.cs` 转发新参数名。
    - `AIPlayer.GetMonitorRecords` 入参重命名为 `monitorTargetIndex`，错误提示使用“持续观察目标[N]”表达。
    - `AIPlayer.MonitorTarget` 成功返回文本改为角色化语言，告知 Agent “第 N 个持续观察目标”，不暴露字段名。
    - `RuntimeInfoRenderer.cs` 摘要以 `持续观察目标[N]` 开头，并提示用“持续观察目标序号 N”回想观察记录。

---

## 5. 测试方案

本版本大部分改动可自测；协议和 Unity 行为需要至少静态检查，最好再联调验证。

### 5.1 Python 自测

新增 `DevDocs/v0.21.6/test_v021_6_self_test.py`。

建议覆盖：

#### TEMPLATE-001：技能模板允许合法占位符

输入包含以下内容的模板 JSON：

```json
[
  {
    "action": "move",
    "direction": "{direction}",
    "condition": "myself.Position.x > {exit_threshold}",
    "allowed_contact_obj_ids": ["{trap_index}"]
  }
]
```

断言技能模板解析成功。

#### TEMPLATE-002：非法占位符被拒绝

覆盖：

- `{Direction}`；
- `{方向}`；
- `{bad name}`；
- `{}`；
- action 类型写成 `{action}`。

#### TEMPLATE-003：裸占位符导致 JSON 解析失败时给出友好提示

输入：

```json
[{"action":"move","direction":{direction}}]
```

断言返回错误提示说明“占位符必须写成字符串”。

#### TEMPLATE-004：执行动作序列拒绝未替换占位符

对 `plan_action_sequence_cmd` 的占位符扫描 helper 做最小测试：

- 含 `{direction}` 时返回发现占位符；
- 不含占位符的真实动作序列通过扫描。

#### TEMPLATE-005：技能索引原样展示占位符

构造 `ActionSkill` + `ActionSequenceTemplate`，调用 `_format_template_index()`，确认输出包含：

- `{direction}`；
- `{platform_index}`；
- `parameter_reason` 中的参数说明。

#### YAML-001：默认技能不再包含旧格式

读取 `db/default_skills/default.yaml`，断言：

- 不存在 `action: Move`；
- 不存在 `action: Interact`；
- 不存在旧式 `params.target`；
- 存在 `{direction}`；
- 所有模板均有 `step_explanations`；
- 不存在 `template_parameters`。

#### TOOL-001：skill_tools 工具描述包含模板 / 执行分离规则

静态检查 `skill_tools.py`：

- 提到 `action_sequence_template` 允许 `{placeholder}`；
- 提到执行前必须替换；
- 不再要求 `template_parameters`。

### 5.2 协议 / Unity 静态自测

在同一测试脚本中做源码静态检查：

#### PROTO-001：协议字段重命名

检查 `Tools/message.proto` 中 `AgentGetMonitorRecordsRequest`：

- 字段名为 `monitor_target_index`；
- 字段号沿用 `3`；
- 不存在 `object_index` / `object_name`。

#### UNITY-001：MonitorTarget 返回角色化文本

检查 `AIPlayer.cs` 中：

- 成功返回文本包含「第 」和「个持续观察目标」；
- 不包含字面量 `monitor_index` 或 `monitor_target_index`；
- 目标已在观察中时返回的也是同样格式（基于已有 index）。

#### UNITY-002：GetMonitorRecords 入参与错误提示

检查 `AIPlayer.cs` 中：

- `GetMonitorRecords` 入参名包含 `monitorTargetIndex`；
- 错误提示文本中包含「持续观察目标」字样；
- 不存在已废弃的 `monitor[0]不存在` 文案。

#### UNITY-003：观察摘要术语统一

检查 `RuntimeInfoRenderer.cs` 输出中：

- 包含 `持续观察目标[`；
- 不包含 `观察目标[`（旧字面量）；
- 不包含 `monitor_index:` 字段名展示。

### 5.3 最小联调建议

如果 Unity 可运行，建议做一次手动联调：

1. 启动 PythonServer 与 Unity。
2. 让 Agent 观察平台。
3. 调用 `monitor_target_cmd(object_index=平台编号, object_name="自动移动的平台")`。
4. 确认返回包含「第 1 个持续观察目标」等角色化文案，且 Agent 状态摘要中出现 `持续观察目标[1]`。
5. 调用 `get_monitor_records_cmd(monitor_target_index=1)` 成功。
6. 给一个不存在的序号（如 99），确认错误提示使用「持续观察目标[99]不存在」等角色化措辞。

---

## 6. 风险与回退

### 6.1 技能模板不再可直接执行

风险：Agent 可能把带占位符的模板直接传给 `plan_action_sequence_cmd`。

缓解：执行入口增加占位符拒绝；错误提示明确要求先替换。

回退：保留宽松模板保存，但在技能索引中加强提示；必要时恢复到合法示例模板方案。

### 6.2 占位符过多导致 Agent 替换遗漏

风险：复杂模板中占位符多，Agent 可能漏替换。

缓解：`plan_action_sequence_cmd` 会拒绝未替换占位符；`step_explanations.parameter_reason` 必须解释每步参数。

回退：对复杂技能拆分为多个更小模板。

### 6.3 历史技能仍使用假占位符

风险：已有导出 YAML 中仍存在 `objects[0]`、`0` 这类假占位符。

缓解：历史导出不强迁移；新创建 / refine 的技能使用内联占位符。必要时通过 `refine_action_skill` 手动精进旧模板。

回退：无须回退，仅保留兼容读取。

### 6.4 monitor 协议字段重命名影响 Unity 转发

风险：proto 字段重命名后 C# 事件签名、MessageDispatch、AgentService 转发未同步导致编译失败。

缓解：按协议流程完整生成并 Rebuild `CSharpClient.sln`；自测脚本做静态检查；本期不新增字段，重命名影响面相对可控。

回退：如果重命名造成范围过大，短期可以保留 proto 字段名 `monitor_index`，仅在 Python 工具层将参数名展示为 `monitor_target_index`、转发时映射；但这会让协议与对外名称长期错位，不推荐。

### 6.5 默认技能模板过度简化

风险：`走到目标旁交互` 迁移为单一 `move + interact` 后，无法覆盖复杂场景。

缓解：默认技能只作为最基础模板；复杂接近方式留给后续训练形成新模板。

回退：保留技能内容说明，但不恢复旧式 `Move` / `Interact` 模板。

---

## 7. 实现记录

| 日期 | 说明 |
|------|------|
| 2026-06-23 | v0.21.6 全部实现：①`skill_tools` 宽松模板校验 + 占位符递归扫描 + 工具描述更新；②`base_tools.plan_action_sequence_cmd` 执行前拒绝未替换 `{placeholder}`；③`action_sequence.get_known_action_names()` 自动派生合法 action 集；④`db/default_skills/default.yaml` 迁移为内联占位符 + step_explanations；⑤`message.proto` 字段重命名为 `monitor_target_index`，已运行 `1.genproto.cmd`、MSBuild Rebuild `CSharpClient.sln`、`2.copyprotocol.cmd`；⑥Python `monitor_target_cmd` / `get_monitor_records_cmd` 角色化文案 + 入参重命名；⑦Unity `AIPlayer` / `RuntimeInfoRenderer` / `AgentService` / `AgentManager` 同步术语与字段名；⑧自测脚本 `DevDocs/v0.21.6/test_v021_6_self_test.py` 全部通过 |
| 2026-06-23 | 验收通过。基于 `logs/prompts/小明/2026-06-23_13-41-56.log` 联调验证：①持续观察文案完全角色化（"持续观察目标[1]"/"持续观察目标序号 1"）落地；②Agent 在该日志中自主创建 `乘平台渡陷阱` 技能并添加 `从左到右渡陷阱` / `从右到左渡陷阱` 两个内联占位符模板；③`plan_action_sequence_cmd` 占位符拦截全程未误伤，Agent 调用时均已替换为真实值；④v0.21.4/v0.21.5 修复点（坐标小写、单引号、`Position` 范围禁用、`set_timer` 长间隔）无 regression。剩余的 `allowed_contact_obj_ids` 占位符表达边界、`WaitAction` 缺 `allowed_contact_obj_ids`、monitor 推送降噪等问题归入 `DevDocs/需求池/backlog.md` 候选清单。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
