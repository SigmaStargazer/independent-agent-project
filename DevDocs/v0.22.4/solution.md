# 技术方案 - v0.22.4 WaitAction 接触白名单 + List[int] 模板占位符

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-07-10

---

## 1. 方案概述

两部分改动：

1. **条目 1**：给 `WaitAction` 补齐 `allowed_contact_obj_ids`，跨 Python 模型 / Proto / Unity 三层落地，使 wait 期间也能声明接触白名单。
2. **条目 2**：放宽 `skill_tools._parse_action_sequence_template` 的占位符扫描，让 `List[int]` 字段的元素可以是字符串占位符；执行入口已有 `_find_unresolved_placeholders` 兜底拒绝残留占位符，无需额外改动。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | `agent_framwork/tools/action_sequence_model/model/action.py` | `WaitAction` 增加字段 |
| Python | `agent_framwork/tools/base_tools.py` | `build_pb_action_step` wait 分支填充新字段；docstring 示例更新 |
| Python | `agent/tools/skill_tools.py` | `_scan_placeholders` 已支持 list 递归，确认无需改；docstring 补充 List[int] 占位符写法说明 |
| 协议 | `Tools/message.proto` | `WaitAction` 消息增加 `repeated int32 allowed_contact_obj_ids` |
| Unity | `AIPlayer.cs` `ExecuteWaitAction` | 读取 `AllowedContactObjIds` 并填入 `AllowedContactObjs`；`ErrorConditionFunc` 增加白名单判断 |

## 3. 详细设计

### 3.1 数据与协议

#### Proto 变更（`Tools/message.proto`）

```proto
message WaitAction {
  repeated int32 allowed_contact_obj_ids = 1;
}
```

- 字段号 1（WaitAction 原来无字段，从 1 开始）。
- `repeated int32`，与 `MoveAction.allowed_contact_obj_ids`（字段号 2）同名同义。
- 向后兼容：旧客户端不填时为空列表。

#### Python 模型变更（`action.py`）

```python
class WaitAction(StateChangeAction):
    action: Literal["wait"] = Field(default="wait", description="原地等待，直至满足条件")
    allowed_contact_obj_ids: List[int] = Field(
        default_factory=list,
        description="等待期间允许接触的物体序号列表，如站在平台上等待时填写平台与陷阱的物体序号。当接触到列表以外的物体时，会中断动作序列。若无则填空列表[]。"
    )
```

- 与 `MoveAction` 的 `allowed_contact_obj_ids` 同名同义。
- **默认空列表**（`default_factory=list`），而非 MoveAction 的必填。原因：绝大多数 wait 场景不需要白名单，必填会增加 Agent 无意义填空负担。

### 3.2 Python（Brain）

#### 3.2.1 `build_pb_action_step` wait 分支（`base_tools.py`）

当前：

```python
if isinstance(step, WaitActionModel):
    pb_step.wait.CopyFrom(message_pb2.WaitAction())
```

改为：

```python
if isinstance(step, WaitActionModel):
    if step.allowed_contact_obj_ids:
        pb_step.wait.allowed_contact_obj_ids.extend(
            step.allowed_contact_obj_ids
        )
```

与 move 分支写法一致。

#### 3.2.2 `plan_action_sequence_cmd` docstring 示例

在现有示例中补充 wait 带 `allowed_contact_obj_ids` 的场景，例如：

```
4) (假设平台序号为3，陷阱序号为2)乘平台渡陷阱：走上平台后等待平台移动至对岸
action_sequence = [
    {
        "action": "move",
        "direction": "right",
        "condition": "canInteract == true && nearestInteractableIndex == 3",
        "allowed_contact_obj_ids": [3]
    },
    {
        "action": "wait",
        "condition": "actionTime >= 5",
        "allowed_contact_obj_ids": [2, 3]
    },
    {
        "action": "move",
        "direction": "right",
        "condition": "displacement >= 2",
        "allowed_contact_obj_ids": [3]
    }
]
```

### 3.3 List[int] 模板占位符（条目 2）

#### 3.3.1 两个阶段的区别

占位符的生命周期分两个阶段，校验机制不同：

| 阶段 | 入口 | 校验方式 | `["{platform_index}"]` 的结果 |
|------|------|---------|---------------------------|
| **保存模板** | `create_action_skill` / `add_template` -> `_parse_action_sequence_template` | 宽松校验：解析 JSON + 扫占位符，不做 Pydantic | **通过**（实测确认） |
| **执行动作序列** | `plan_action_sequence_cmd` 参数 `List[ActionStep]` | Pydantic 强校验：`List[int]` 拒绝字符串 | **报错** `int_parsing`（实测确认） |

**与字符串字段的区别**（关键）：

- 字符串字段（如 `direction: "{direction}"`）：占位符本身是字符串，Pydantic 接受。Agent 若忘记替换，由函数体内的 `_find_unresolved_placeholders` 兜底拦截，给出友好提示「以下占位符尚未替换为真实值」。
- `List[int]` 字段：占位符是字符串，LangChain ToolNode 在调用函数体**之前**做 Pydantic 校验，直接报 `int_parsing` ValidationError。**不会走到** `_find_unresolved_placeholders`，Agent 收到的是 Pydantic 技术报错而非友好提示。

#### 3.3.2 实测验证

```python
# 保存阶段：通过
_parse_action_sequence_template('[{"action":"wait","condition":"actionTime >= 5","allowed_contact_obj_ids":["{platform_index}"]}]')
# -> [{'action': 'wait', 'condition': 'actionTime >= 5', 'allowed_contact_obj_ids': ['{platform_index}']}]

# 执行阶段：Pydantic 拒绝
MoveAction(action='move', direction='right', condition='displacement >= 2', allowed_contact_obj_ids=['{platform_index}'])
# -> ValidationError: Input should be a valid integer, unable to parse string as an integer
```

#### 3.3.3 结论与改动

功能上**无需改代码逻辑**：

- 保存阶段已天然支持 `["{platform_index}"]`（`_scan_placeholders` 已递归 list）。
- 执行阶段 Pydantic 天然拒绝残留占位符，保证不会带着占位符执行。

体验上的小瑕疵：`List[int]` 字段残留占位符时报的是 Pydantic 的 `int_parsing` 技术错误，不如 `_find_unresolved_placeholders` 的「占位符尚未替换」友好。但 LangChain 工具的 Pydantic 校验发生在函数体之前，无法在函数内拦截。这属于可接受的体验折衷--Agent 看到 `int_parsing` 错误后也能理解要替换占位符。

需要的改动（backlog 方案 A，仅文档约束，零代码风险）：在 `skill_tools.py` 工具 docstring 中补充说明：

- `List[int]` 字段在模板中可以写成 `["{platform_index}"]`（字符串占位符包在列表里）。
- 执行 `plan_action_sequence_cmd` 时必须替换为真实 int 值，否则会报类型错误。

### 3.4 Unity（Environment）

#### `ExecuteWaitAction` 改动（`AIPlayer.cs`）

当前 `ExecuteWaitAction` 的 `Todo` 分支缺少 `AllowedContactObjs` 填充，`ErrorConditionFunc` 也缺少白名单判断。需要对齐 `ExecuteMoveAction`。

在 `Todo` 分支的 `StartEnv` 赋值之后、`CompleteConditionFunc` 之前，增加：

```csharp
// 等待时允许接触的物体（与 ExecuteMoveAction 对齐）
var allowedIds = curAction.Wait.AllowedContactObjIds;
if (allowedIds != null)
{
    foreach (var id in allowedIds)
    {
        this.mCurActionRuntime.AllowedContactObjs
            .Add(actionSequenceRuntime.SceneObjSnap[id]);
    }
}
```

`ErrorConditionFunc` 当前：

```csharp
foreach (var obj in this.mTouchingObjs)
{
    if (!this.mCurActionRuntime.StartTouchingObjs.Contains(obj))
    {
        // 报错
        return true;
    }
}
```

改为（与 move 一致，增加 `&& !AllowedContactObjs.Contains(obj)`）：

```csharp
foreach (var obj in this.mTouchingObjs)
{
    if (!this.mCurActionRuntime.StartTouchingObjs.Contains(obj)
        && !this.mCurActionRuntime.AllowedContactObjs.Contains(obj))
    {
        // 报错
        return true;
    }
}
```

## 4. 实现步骤

1. **Proto**：`Tools/message.proto` 的 `WaitAction` 加 `repeated int32 allowed_contact_obj_ids = 1;`。
2. **生成协议**：按 `Doc/Agent工具开发流程.md` 跑 `1.genproto.cmd` -> rebuild `CSharpClient.sln` -> `2.copyprotocol.cmd`。
3. **Python 模型**：`action.py` 的 `WaitAction` 加 `allowed_contact_obj_ids` 字段（默认空列表）。
4. **Python 序列化**：`base_tools.py` 的 `build_pb_action_step` wait 分支填充新字段。
5. **Python docstring**：`plan_action_sequence_cmd` 示例补充 wait 带 `allowed_contact_obj_ids`；`skill_tools.py` docstring 补充 `List[int]` 占位符写法说明。
6. **Unity**：`AIPlayer.ExecuteWaitAction` 填充 `AllowedContactObjs` + `ErrorConditionFunc` 加白名单判断。
7. **Python 自测**：模型字段、`build_pb_action_step`、模板解析含 `List[int]` 占位符。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| proto 字段号冲突 | WaitAction 原无字段，从 1 开始，无冲突 |
| 旧模板 wait 无新字段 | 默认空列表，向后兼容 |
| Unity `SceneObjSnap[id]` 越界 | 与 move 共用同一套逻辑，已有兜底 |
| List[int] 占位符在执行入口 Pydantic 报错 | 这是预期行为，Agent 必须替换后才能执行 |

回退：proto 字段为 optional repeated，删除 Python/Unity 侧读取即可回退。

## 6. 测试建议

### 6.1 Python 自测（不依赖 Unity）

- `WaitAction` 实例化：默认 `allowed_contact_obj_ids == []`；传 `[2, 3]` 正确赋值。
- `build_pb_action_step`：wait 动作带 `[2, 3]` 时，proto `pb_step.wait.allowed_contact_obj_ids == [2, 3]`。
- 模板解析：`_parse_action_sequence_template` 接受 `[{"action":"wait","condition":"actionTime >= 5","allowed_contact_obj_ids":["{platform_index}"]}]`，`_scan_placeholders` 收集到 `platform_index`。
- `_find_unresolved_placeholders`：对含 `"{platform_index}"` 的结构报未替换。

> 以上 4 项 + Pydantic 拒绝字符串占位符 + 默认空列表 build_pb + 无残留占位符不报，共 8 项自测全部通过（2026-07-10）。

### 6.2 Unity 联调

- 平台渡陷阱场景：wait 期间接触平台 + 陷阱不中断；接触白名单外物体正常中断。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-07-10 | 完成全部开发：proto 字段、协议生成、Python 模型/序列化/docstring、Unity ExecuteWaitAction 白名单。Python 自测 8 项通过。 |
| 2026-07-10 | Unity 联调验收通过。日志 `logs/prompts/小明/2026-07-10_17-42-34.log` 证实：Agent 成功使用 `wait` + `allowed_contact_obj_ids: [2, 3]`；line 20648 Wait 动作正常完成无碰撞中断；console 日志无「撞击到物体」误报。日志中的碰撞失败均为动作序列未开始执行（动作状态全 Todo）时 Agent 手动 move_cmd 踩陷阱，属策略问题，与本次改动无关。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
