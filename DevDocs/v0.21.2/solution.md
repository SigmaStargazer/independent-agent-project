# 技术方案 — v0.21.2 MonitorTarget / FollowTarget 目标名称校验

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-06-18

---

## 1. 方案概述

为 `monitor_target_cmd` 与 `follow_target_cmd` 增加 `object_name` 参数，并在 `Tools/message.proto` 对应 request 中追加字段；Python 将名称随索引一起发给 Unity，Unity 在 `AIPlayer` 中用 `objectIndex` 取目标后执行“当前名称 == 期望名称”的一致性校验，失败则直接返回工具失败结果且不产生动作副作用。

---

## 2. 影响范围

| 层级 | 模块 / 路径 | 变更类型 |
|------|------------|----------|
| 协议 | `Tools/message.proto` | `AgentMonitorTargetRequest`、`AgentFollowTargetRequest` 追加 `object_name` 字段 |
| 协议生成 | `Tools/1.genproto.cmd`、`Tools/2.copyprotocol.cmd` | 实现阶段执行，生成 Python / C# 协议文件；禁止手改生成文件 |
| Python | `Src/PythonServer/agent_framwork/tools/base_tools.py` | 修改两个工具签名、docstring、protobuf request 赋值 |
| Python | `Src/PythonServer/agent_framwork/agents/agent_interuptible.py` | 通常无需修改；两个工具已注册，schema 随函数签名自动变化 |
| Unity | `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/Services/AgentService.cs` | event 参数、日志、Invoke 参数增加 `objectName` |
| Unity | `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/AgentManager.cs` | 转发方法增加 `objectName` |
| Unity | `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/AIPlayer.cs` | `MonitorTarget` / `FollowTarget` 方法签名与名称校验逻辑 |
| Unity | `Src/Lib/Common/Network/MessageDispatch.cs` | 预计无需新增分发行；message 类型未新增，仅字段变化。实现阶段检查生成后编译结果 |
| ActionSequence | — | 无变更 |

---

## 3. 详细设计

### 3.1 协议字段

只改协议源文件 `Tools/message.proto`。

当前结构：

```proto
message AgentMonitorTargetRequest {
	string agent = 1;
	string request_id = 2;
	int32 object_index = 3;
}

message AgentFollowTargetRequest {
	string agent = 1;
	string request_id = 2;
    int32 object_index = 3;
    float min_distance = 4;
    float max_distance = 5;
}
```

计划追加字段：

```proto
message AgentMonitorTargetRequest {
	string agent = 1;
	string request_id = 2;
	int32 object_index = 3;
    string object_name = 4;
}

message AgentFollowTargetRequest {
	string agent = 1;
	string request_id = 2;
    int32 object_index = 3;
    string object_name = 4;
    float min_distance = 5;
    float max_distance = 6;
}
```

说明：

- `AgentMonitorTargetRequest.object_name` 使用字段号 4。
- `AgentFollowTargetRequest.object_name` 使用字段号 4。
- `AgentFollowTargetRequest` 必须采用用户最终确认的字段顺序与字段号：`object_index = 3`、`object_name = 4`、`min_distance = 5`、`max_distance = 6`。
- `AgentFollowTargetRequest` 字段顺序与字段号以用户最终确认版本为准，后续不得再调整。
- 字段名用 snake_case，C# 生成属性应为 `ObjectName`，Python 访问应为 `request.object_name`。

协议生成流程（实现阶段执行）：

1. 修改 `Tools/message.proto`。
2. 执行 `Tools/1.genproto.cmd`。
3. 检查 `Src/Lib/Common/Network/MessageDispatch.cs`：本期未新增 message 类型，通常无需改分发逻辑。
4. Rebuild `Src/CSharpClient/CSharpClient.sln`。
5. 执行 `Tools/2.copyprotocol.cmd`。

### 3.2 Python 工具调整

#### 3.2.1 `monitor_target_cmd`

计划签名：

```python
@tool
async def monitor_target_cmd(
    agent: Annotated[str, InjectedState("name")],
    tool_call_id: Annotated[str, InjectedToolCallId],
    object_index: int,
    object_name: str,
) -> str:
    ...
```

request 赋值增加：

```python
request.object_index = object_index
request.object_name = object_name
```

工具 docstring 调整要点：

- 说明这是“持续观察目标”。
- `object_index`：目标在最近观察结果中的编号。
- `object_name`：同一条观察结果中该编号对应的对象名称。
- 明确 Agent 不确定时应先重新观察，不要凭旧记忆填写。
- 说明 Unity 会用名称校验索引，校验失败时不会开始观察。

#### 3.2.2 `follow_target_cmd`

计划签名：

```python
@tool
async def follow_target_cmd(
    agent: Annotated[str, InjectedState("name")],
    tool_call_id: Annotated[str, InjectedToolCallId],
    object_index: int,
    object_name: str,
    min_distance: float = 0,
    max_distance: float = 2,
) -> str:
    ...
```

request 赋值增加：

```python
request.object_index = object_index
request.object_name = object_name
request.min_distance = min_distance
request.max_distance = max_distance
```

参数顺序建议把 `object_name` 放在 `object_index` 后、距离参数前，便于 LLM 理解“索引 + 名称”是一组目标定位信息。

### 3.3 Unity `AgentService.cs`

当前事件：

```csharp
public event UnityAction<string, string, int> OnMonitorTarget;
public event Action<string, string, int, float, float> OnFollowTarget;
```

计划改为：

```csharp
public event UnityAction<string, string, int, string> OnMonitorTarget;
public event Action<string, string, int, string, float, float> OnFollowTarget;
```

处理函数：

```csharp
void OnAgentMonitorTarget(object sender, AgentMonitorTargetRequest request)
{
    Debug.LogFormat($"OnAgentMonitorTarget::Agent:{request.Agent} RequestId:{request.RequestId} ObjectIndex:{request.ObjectIndex} ObjectName:{request.ObjectName}");
    this.OnMonitorTarget?.Invoke(request.Agent, request.RequestId, request.ObjectIndex, request.ObjectName);
}

void OnAgentFollowTarget(object sender, AgentFollowTargetRequest request)
{
    Debug.LogFormat($"OnAgentFollowTarget::Agent:{request.Agent} RequestId:{request.RequestId} ObjectIndex:{request.ObjectIndex} ObjectName:{request.ObjectName} MinDistance:{request.MinDistance} MaxDistance:{request.MaxDistance}");
    this.OnFollowTarget?.Invoke(request.Agent, request.RequestId, request.ObjectIndex, request.ObjectName, request.MinDistance, request.MaxDistance);
}
```

### 3.4 Unity `AgentManager.cs`

当前转发方法：

```csharp
private void MonitorTarget(string agent, string requestId, int objectIndex)
{
    if (mAgents.TryGetValue(agent, out var agentObj))
    {
        agentObj.MonitorTarget(requestId, objectIndex);
    }
}

private void FollowTarget(string agent, string requestId, int objectIndex, float minDistance, float maxDistance)
{
    if (mAgents.TryGetValue(agent, out var agentObj))
    {
        agentObj.FollowTarget(requestId, objectIndex, minDistance, maxDistance);
    }
}
```

计划改为：

```csharp
private void MonitorTarget(string agent, string requestId, int objectIndex, string objectName)
{
    if (mAgents.TryGetValue(agent, out var agentObj))
    {
        agentObj.MonitorTarget(requestId, objectIndex, objectName);
    }
}

private void FollowTarget(string agent, string requestId, int objectIndex, string objectName, float minDistance, float maxDistance)
{
    if (mAgents.TryGetValue(agent, out var agentObj))
    {
        agentObj.FollowTarget(requestId, objectIndex, objectName, minDistance, maxDistance);
    }
}
```

### 3.5 Unity `AIPlayer.cs` 名称校验

#### 3.5.1 公共校验方式

本期只改两个方法，避免新增过度抽象。可在方法内直接写校验，也可抽一个私有辅助方法。建议抽辅助方法减少重复文案拼接风险：

```csharp
private bool IsSceneObjectNameMatched(SceneObjBase target, string expectedName)
{
    return string.Equals(target?.Name?.Trim(), expectedName?.Trim(), StringComparison.Ordinal);
}
```

说明：

- 采用 `Trim()` 后严格相等，解决普通首尾空白问题。
- 使用 `StringComparison.Ordinal`，不做文化相关比较、不忽略大小写。
- 如果用户确认不需要 `Trim()`，实现时改为 `target?.Name == expectedName`。

#### 3.5.2 `MonitorTarget`

计划签名：

```csharp
public void MonitorTarget(string requestId, int objectIndex, string objectName)
```

校验插入位置：当前方法已先检查监视数量，再检查索引范围，然后取 `target` 并判断是否已观察。名称校验需要放在“取出 target 后、判断是否已观察前”。

建议流程：

```csharp
SceneObjBase target = sceneObjs[objectIndex];
if (!IsSceneObjectNameMatched(target, objectName))
{
    AgentService.Instance.SendToolResultMessage(
        Name,
        "MonitorTarget",
        requestId,
        $"[持续观察失败] 目标校验失败：索引[{objectIndex}]当前是\"{target.Name}\"，不是你指定的\"{objectName}\"。请重新观察当前环境后再选择目标。"
    );
    return;
}
```

注意：

- 不创建 `ObserveRuntime`。
- 不注册 `StateChangedHandler`。
- 不占用观察名额。
- “最多同时持续观察 3 个目标”的校验目前在索引与名称校验之前。保留现状即可；若已有 3 个观察任务，即使目标名称不匹配，也会先返回“注意力不足”。这是现有业务优先级，不在本期调整。

#### 3.5.3 `FollowTarget`

计划签名：

```csharp
public void FollowTarget(string requestId, int objectIndex, string objectName, float minDistance, float maxDistance)
```

校验插入位置：当前方法在索引范围通过后立即 `StopMovement()`，然后取 target。为避免名称不匹配时产生“停止当前移动 / 追踪”的副作用，需要先取 target 并做名称校验，校验通过后再 `StopMovement()`。

计划流程：

```csharp
var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);
if (objectIndex < 0 || objectIndex >= sceneObjs.Count)
{
    ... // 既有对象不存在返回
    return;
}

SceneObjBase target = sceneObjs[objectIndex];
if (!IsSceneObjectNameMatched(target, objectName))
{
    AgentService.Instance.SendToolResultMessage(
        Name,
        "FollowTarget",
        requestId,
        $"[跟随结果]失败！目标校验失败：物体[{objectIndex}]当前是\"{target.Name}\"，不是你指定的\"{objectName}\"。请重新观察当前环境后再选择目标。"
    );
    return;
}

this.StopMovement();
TargetFollowing = target;
...
```

现有追踪成功文案已经包含索引与名称：

```csharp
$"[跟随结果]开始跟随:{objectIndex}. {target.Name}"
```

该格式可保持不变。监视“已在观察中”文案也已有对象名；若创建观察成功的返回文案当前未包含名称，开发时可补齐。

### 3.6 场景枚举与判定表

| 触发场景 | objectIndex 判定 | objectName 判定 | 期望行为 | 理由 |
|----------|------------------|-----------------|----------|------|
| MonitorTarget，索引越界 | 失败 | 不执行 | 返回索引超出范围 | 目标不存在，沿用旧逻辑 |
| MonitorTarget，索引有效但名称不一致 | 通过 | 失败 | 返回目标校验失败；不创建观察 runtime | 防止观察错对象 |
| MonitorTarget，索引有效且名称一致，目标未观察 | 通过 | 通过 | 创建观察 runtime，返回开始结果 | 正常路径 |
| MonitorTarget，索引有效且名称一致，目标已观察 | 通过 | 通过 | 返回已在观察中 | 正常去重逻辑 |
| MonitorTarget，观察数已达 3 | 不执行或不重要 | 不执行或不重要 | 返回最多观察 3 个目标 | 保持现有注意力限制优先级 |
| FollowTarget，索引越界 | 失败 | 不执行 | 返回物体不存在 | 沿用旧逻辑 |
| FollowTarget，索引有效但名称不一致 | 通过 | 失败 | 返回目标校验失败；不 StopMovement；不改 TargetFollowing | fail fast，无副作用 |
| FollowTarget，索引有效且名称一致 | 通过 | 通过 | StopMovement 后开始跟随 | 正常路径 |
| FollowTarget，目标开始后消失 | 启动时已通过 | 后续不再按名称校验 | 走既有目标消失中断逻辑 | 本期不改长时运行期语义 |

---

## 4. 实现步骤

1. 修改 `Tools/message.proto`：为 `AgentMonitorTargetRequest` 追加 `object_name = 4`；`AgentFollowTargetRequest` 按最终确认顺序改为 `object_index = 3`、`object_name = 4`、`min_distance = 5`、`max_distance = 6`。
2. 执行 `Tools/1.genproto.cmd`，确认无报错，生成 Python / C# 协议代码。
3. 检查 `MessageDispatch.cs`：因为未新增 request 类型，预计无需新增分发行；如生成类型名或命名空间异常，先处理编译问题。
4. Rebuild `Src/CSharpClient/CSharpClient.sln`。
5. 执行 `Tools/2.copyprotocol.cmd`，同步协议到项目使用位置。
6. 修改 `base_tools.py`：两个工具增加 `object_name` 参数、docstring 与 request 赋值。
7. 修改 `AgentService.cs`：事件类型、日志、Invoke 参数增加 `objectName`。
8. 修改 `AgentManager.cs`：事件处理方法签名与转发参数增加 `objectName`。
9. 修改 `AIPlayer.cs`：两个方法签名与名称校验逻辑；尤其确保 `FollowTarget` 名称不匹配时在 `StopMovement()` 之前返回。
10. Python 侧做最小 schema 检查：确认两个 LangChain tool 的 input schema 包含 `object_name`。
11. C# 侧编译 / Unity 打开后检查无编译错误。
12. Unity 联调：分别验证监视 / 追踪的名称一致与不一致路径。
13. 验收后更新文档状态：PRD / solution 由「待确认」改「已确认」；实现完成并验收后 solution 改「已实现」。

---

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 协议生成未同步，导致 Python 有字段但 C# 没字段或反之 | 严格执行 `1.genproto.cmd`、CSharpClient rebuild、`2.copyprotocol.cmd`；禁止手改生成文件 |
| LLM 不填写 `object_name` 或乱填 | 工具 docstring 强调必须来自同一条观察结果；schema 必填，不提供则工具调用校验失败 |
| Unity 名称存在首尾空白导致误拒绝 | 建议两侧 `Trim()` 后 `Ordinal` 严格相等；待用户确认 Q1 |
| 场景中多个对象同名 | 本期校验目标是“防止 index 错位到不同名称对象”，同名对象无法区分；仍保留 `object_index` 作为主定位参数。后续如需完全唯一，可引入稳定对象 id |
| `FollowTarget` 名称不匹配却先停止了当前动作 | 方案明确把名称校验放在 `StopMovement()` 前，并列入验收标准 |
| 观察数量已满时无法看到名称不匹配错误 | 保留现有“最多 3 个观察目标”业务优先级；如需要先做名称校验再检查名额，应另行确认 |

回退方式：

- 若实现后发现协议追加导致联调异常，可回滚本版本改动：移除 `object_name` 字段、还原 Python / Unity 签名并重新生成协议。
- 因为本期不迁移数据、不修改存档结构，回退不涉及数据库或 Unity 存档处理。

---

## 6. 测试建议

### 6.1 Python 静态 / schema 检查

实现后运行最小检查，确认工具 schema 中包含 `object_name`。示例方向：

```bash
uv run python - <<'PY'
from agent_framwork.tools import base_tools
for tool in [base_tools.monitor_target_cmd, base_tools.follow_target_cmd]:
    schema = tool.get_input_schema().model_json_schema()
    print(tool.name, schema.get('properties', {}).keys())
    assert 'object_name' in schema.get('properties', {})
print('ok')
PY
```

如当前 shell 不方便 heredoc，可改成临时脚本或 `python -c`。

### 6.2 协议生成检查

- `Tools/1.genproto.cmd` 成功。
- C# 侧 `AgentMonitorTargetRequest.ObjectName`、`AgentFollowTargetRequest.ObjectName` 可编译访问。
- Python 侧 `message_pb2.AgentMonitorTargetRequest().object_name`、`message_pb2.AgentFollowTargetRequest().object_name` 可赋值。

### 6.3 Unity 联调用例

| 用例 | 步骤 | 预期 |
|------|------|------|
| TC-1 监视名称一致 | 观察场景，选择某对象的 index/name 调 `monitor_target_cmd` | 返回开始持续观察；状态变化记录正常 |
| TC-2 监视名称不一致 | 故意传正确 index 但错误 name | 返回目标校验失败；观察列表不新增 runtime |
| TC-3 监视索引越界 | 传超出范围 index | 返回索引超出范围，行为同旧版本 |
| TC-4 追踪名称一致 | 观察场景，选择某对象的 index/name 调 `follow_target_cmd` | 返回开始跟随；角色进入 Follow 状态 |
| TC-5 追踪名称不一致 | 在角色已有移动或跟随时，传正确 index 但错误 name | 返回目标校验失败；旧动作不被 `StopMovement()` 提前打断 |
| TC-6 追踪索引越界 | 传超出范围 index | 返回物体不存在，行为同旧版本 |

### 6.4 日志检查

- Python 日志：两个工具调用能看到 request 发起。
- Unity 日志：`OnAgentMonitorTarget` / `OnAgentFollowTarget` 包含 `ObjectIndex` 与 `ObjectName`。
- 失败返回文案包含期望名称与实际名称。

---

## 7. 实现记录

| 日期 | 说明 |
|------|------|
| 2026-06-18 | 已完成 `object_name` 参数接入：更新 `Tools/message.proto` 与生成协议，Python `monitor_target_cmd` / `follow_target_cmd` 写入 `object_name`，Unity `AgentService` / `AgentManager` 透传 `ObjectName`，`AIPlayer` 在 Monitor / Follow 开始前按 `Trim()` 后严格相等校验目标名称；Python schema 检查通过，C# 方案由用户重新生成并执行协议部署脚本。 |
| 2026-06-18 | 用户完成 Unity 联调验收，确认 v0.21.2 功能验收通过，方案状态更新为「已实现」。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
