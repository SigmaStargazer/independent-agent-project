# ActionSequence 开发流程

本文档说明两类相关开发：**动作序列工具**与**动作序列内新增 Action 类型**。Cursor Skill 详见 `.cursor/skills/develop-agent-tool/action-sequence-reference.md`。

## 概念区分

| 层次 | 是什么 | 典型改动 | 是否新增 tool |
|------|--------|----------|---------------|
| 动作序列工具 | LLM 调用的 `plan_action_sequence_cmd` 等 | `AgentPlanActionSequenceRequest` + AgentService 链路 | 极少（通常已存在） |
| Action 类型 | `action.py` 里一种具体动作 | `ActionStep` protobuf + 执行器 | **否** |

动作序列的数据载体是 protobuf `ActionStep`（内含 `oneof action`），由 `plan_action_sequence_cmd` 经 `build_pb_action_step()` 序列化后发给 Unity。

---

## 一、动作序列工具（已有）

### 四个工具

| Python 工具 | Proto Request | 行为 |
|-------------|---------------|------|
| `plan_action_sequence_cmd` | `AgentPlanActionSequenceRequest` | 同步 RPC；Unity 校验 condition 后返回规划结果 |
| `start_action_sequence_cmd` | `AgentStartActionSequenceRequest` | 确认执行；完成后 `SendFeedbackToAgent` |
| `continue_action_sequence_cmd` | `AgentContinueActionSequenceRequest` | 从中断处继续 |
| `stop_action_sequence_cmd` | `AgentStopActionSequenceRequest` | 中止序列 |

### 开发新「工具」时

按 [Agent工具开发流程.md](./Agent工具开发流程.md) 通用流程：`AgentXxxRequest` → MessageDispatch → base_tools → AgentService → AgentManager → AIPlayer。

---

## 二、新增 Action 类型（action.py）

假设要在 `action.py` 增加 `JumpAction`（`action: "jump"`）。

### 步骤总览

```
1. message.proto：ActionStep.oneof + JumpAction message
2. 1.genproto.cmd（无需改 MessageDispatch）
3. Python：action.py → action_sequence.py → build_pb_action_step
4. Unity：ActionSequenceRuntime → AIPlayer.ExecuteCurAction → ExecuteJumpAction
5. 按需：ConditionEvaluator / ConditionContext / types.py
6. 更新 plan_action_sequence_cmd docstring 示例
```

### 1. Protobuf

编辑 `Tools/message.proto` 中 **ActionSequence 区域**（不是 NetMessageRequest）：

```protobuf
message ActionStep {
  string condition = 1;
  oneof action {
    WaitAction wait = 2;
    // ...
    JumpAction jump = 7;  // 新字段号
  }
}

message JumpAction {
  float height = 1;
}
```

执行 `Tools/1.genproto.cmd`，Rebuild `CSharpClient.sln`，`Tools/2.copyprotocol.cmd`。

### 2. Python 模型

**`action_sequence_model/model/action.py`**

```python
class JumpAction(StateChangeAction):  # 或 BaseAction
    action: Literal["jump"] = Field(default="jump", description="...")
    height: float = Field(..., description="跳跃高度")
```

- 需要 **结束条件** → 继承 `StateChangeAction`，`condition` 由 Pydantic 校验（见 `base_action.py`）
- **一次完成** → 继承 `BaseAction`

**`action_sequence_model/model/action_sequence.py`**

在 `Union[WaitAction, MoveAction, ...]` 中加入 `JumpAction`。

**`base_tools.py`**

- import 新模型别名
- `build_pb_action_step()` 增加分支
- `plan_action_sequence_cmd` docstring 补充 JSON 示例

### 3. Unity 执行

**`ActionSequenceRuntime.CreateActionRuntimeLog`**

为 `action.Jump != null` 设置 `ActionName = "Jump"`。

**`AIPlayer.ExecuteCurAction`**

```csharp
else if (curAction.Jump != null)
    this.ExecuteJumpAction(mCurActionSequenceRuntime);
```

**`AIPlayer.ExecuteJumpAction`**

按动作形态选择模板：

#### 持续型（有 condition，多帧执行）

参考 `ExecuteMoveAction` / `ExecuteWaitAction`：

1. `Todo` 时记录 `StartEnv`、构建 `ConditionContext`
2. `CompleteConditionFunc` 每帧更新 `ActionTime`/`Displacement`，调用 `mConditionEvaluator.Evaluate`
3. 可选 `ErrorConditionFunc`（碰撞等）
4. `ChangeState(...)` 进入对应 FSM

#### 瞬发型（无 condition，一次完成）

参考 `ExecuteInteractAction`：

1. 调用场景逻辑
2. 设置 `Result.Message`、`Done/Failed`
3. `OnActionFinished` 推进下一动作

### 4. 条件判断器（ConditionEvaluator）

**大多数新 Action 不需要改 ConditionEvaluator**，只要 LLM 写的 `condition` 仍用现有变量。

#### 需要改的情况

| 需求 | Python | Unity |
|------|--------|-------|
| 新 condition 根变量（如 `energy`） | `core/types.py` → `CONDITION_VARIABLES` | `ConditionContext` 新属性 + `ConditionEvaluator.SetVariables` |
| 新 `objects[i].Xxx` 属性 | `CONDITION_VARIABLES["objects"].members` | `SceneObjExprView` + `ExprViewFactory.From` |
| 新 condition 语义规则 | — | `ConditionEvaluator` 新方法 + `ValidateAll` 调用 |
| 新内置函数 | — | `ConditionEvaluator` 构造函数 `SetFunction` |

#### 当前已支持

**变量**（Python `types.py` 与 Unity `SetVariables` 须保持一致）：

- `myself`, `objects`, `displacement`, `actionTime`, `canInteract`, `nearestInteractableIndex`

**objects / myself 成员**：

- `Position`, `Velocity`, `State`

**特殊语义校验**（仅规划阶段）：

- `nearestInteractableIndex == N` 会校验 N 是否为可交互物体（`ValidateNearestInteractableIndexReference`）

#### 两端 condition 校验分工

| 阶段 | Python | Unity |
|------|--------|-------|
| LLM 填参 | Pydantic `StateChangeAction.validate_condition`（语法 + 变量白名单） | — |
| 规划 | — | `ConditionEvaluator.ValidateAll`（运行时语义） |
| 执行 | — | `ConditionEvaluator.Evaluate`（每帧判断结束） |

---

## 三、现有 Action 对照

| action | Python 类 | 基类 | Proto | Unity 执行 |
|--------|-----------|------|-------|------------|
| wait | WaitAction | StateChangeAction | wait | ExecuteWaitAction |
| move | MoveAction | StateChangeAction | move | ExecuteMoveAction |
| interact | InteractAction | BaseAction | interact | ExecuteInteractAction |
| select | SelectAction | BaseAction | select | ExecuteSelectAction |
| input | InputAction | BaseAction | input | ExecuteInputAction |

---

## 四、验证清单

- [ ] `1.genproto.cmd` 后 C#/Python 均含新 `XxxAction` 类型
- [ ] LLM 输出合法 JSON 时 Pydantic 能解析
- [ ] `plan_action_sequence_cmd` 能发出含新 action 的 `ActionStep`
- [ ] Unity `PlanActionSequence` 校验通过（或给出明确错误）
- [ ] `StartActionSequence` 后能进入 `ExecuteXxxAction`
- [ ] 持续型：condition 成立时正确进入下一动作；失败时序列中止并反馈
- [ ] 瞬发型：一次执行后 `OnActionFinished` 推进序列
- [ ] 若改了 condition 变量：Python 校验与 Unity Evaluate 结果一致

---

## 五、常见误区

1. **把 Action 类型当成新 tool** — 只需扩展 `ActionStep`，不必加 `AgentXxxRequest`。
2. **手改 message_pb2.py / message.cs** — 必须走 `1.genproto.cmd`。
3. **只改 Python 不改 Unity** — 规划可能过但执行时 `ExecuteCurAction` 落入「未定义的 ActionStep」。
4. **只改 Unity 不改 proto** — Python 序列化无法填充新字段。
5. **新增 condition 变量只改一端** — LLM 在 Python 侧通过校验，Unity 执行时报未定义变量。