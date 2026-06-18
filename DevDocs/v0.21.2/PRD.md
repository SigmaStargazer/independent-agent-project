# PRD — v0.21.2 MonitorTarget / FollowTarget 目标名称校验

> **状态**：已确认
> **对应需求**：用户口述需求（2026-06-18）：监视、追踪两个工具增加 `object_name` 参数，与 `object_index` 一起在 Unity 中校验目标一致性。
> **最后更新**：2026-06-18

---

## 1. 背景与目标

当前 `monitor_target_cmd`（监视目标）与 `follow_target_cmd`（追踪目标）只通过 `object_index` 指定 Unity 场景物体。`object_index` 来自 Agent 最近一次观察到的场景对象列表，但在异步世界中，对象列表可能因物体创建、消失、排序变化或 Agent 记忆滞后而发生变化。

这会带来一个风险：Agent 以为自己要监视 / 追踪 A，但 Unity 端按 `object_index` 取到的实际对象已经变成 B。此时工具调用虽然参数类型正确，却会作用到错误目标。

本期目标是在两个长时工具中增加 `object_name` 参数，让 Python 侧把 Agent 认定的目标名称随 `object_index` 一起传给 Unity；Unity 端用 `object_index` 取出物体后，再校验物体名称是否与 `object_name` 对得上。只有索引与名称一致时才开始监视 / 追踪，否则返回失败结果，提示 Agent 重新观察当前环境。

---

## 2. 范围

### 2.1 本期包含

- `monitor_target_cmd` 增加 `object_name: str` 参数。
- `follow_target_cmd` 增加 `object_name: str` 参数。
- `Tools/message.proto` 中 `AgentMonitorTargetRequest` 与 `AgentFollowTargetRequest` 增加 `object_name` 字段。
- Python 侧将 `object_name` 写入 protobuf request。
- Unity `AgentService` / `AgentManager` / `AIPlayer` 链路透传 `object_name`。
- Unity `AIPlayer.MonitorTarget` 与 `AIPlayer.FollowTarget` 在索引范围校验后、动作开始前执行名称一致性校验。
- 工具 docstring / 参数说明调整，让 Agent 明确必须同时填写目标索引与目标名称。

### 2.2 本期不包含

- 不改变观察工具 `observe_cmd` 的输出结构。
- 不改变场景对象列表排序规则。
- 不改变 `object_index` 的含义，仍表示最近观察文本中的对象下标。
- 不为所有依赖 `object_index` 的工具统一加名称校验；本期只处理监视与追踪两个工具。
- 不调整目标消失处理逻辑（v0.20.11 已覆盖的 MonitorTarget / FollowTarget 目标消失行为保持不变）。
- 不修改 ActionSequence 动作模型。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| Agent | 刚观察到 `2. 小球`，随后调用监视工具，传入 `object_index=2, object_name="小球"` | Unity 校验索引 2 当前对象名称为小球，开始监视 |
| Agent | 使用过期观察结果，传入 `object_index=2, object_name="小球"`，但索引 2 当前已是木箱 | Unity 拒绝开始监视，返回名称不匹配，提示重新观察 |
| Agent | 追踪目标时索引有效且名称一致 | Unity 开始追踪，并在结果中继续提示目标索引与名称 |
| Agent | 追踪目标时索引越界 | Unity 按既有逻辑返回对象不存在；名称校验不执行 |
| 开发者 | 排查错误目标问题 | 日志中可看到 request 的 `ObjectIndex` 与 `ObjectName`，便于确认是 Agent 参数过期还是 Unity 侧对象列表变化 |

---

## 4. 功能需求

### 4.1 工具参数调整

**FR-1.1 `monitor_target_cmd` 参数**

- 新签名应包含：`object_index: int, object_name: str`。
- `object_name` 表示 Agent 从最近观察结果中看到的目标名称。
- docstring 需说明：`object_index` 与 `object_name` 必须来自同一条观察结果；如果不确定，应先重新观察。

**FR-1.2 `follow_target_cmd` 参数**

- 新签名应包含：`object_index: int, object_name: str, min_distance: float = 0, max_distance: float = 2`。
- `object_name` 语义同上。
- 工具描述应强调：名称不是给 Agent 自己看的注释，而是 Unity 用于防止索引错位的安全校验参数。

### 4.2 协议调整

**FR-2.1 `AgentMonitorTargetRequest`**

- 在 `Tools/message.proto` 中为 `AgentMonitorTargetRequest` 追加 `string object_name` 字段。
- 不复用已有字段号，不改变现有字段号。

**FR-2.2 `AgentFollowTargetRequest`**

- `AgentFollowTargetRequest` 采用用户最终确认的字段顺序：`object_index = 3`、`object_name = 4`、`min_distance = 5`、`max_distance = 6`。
- 该结构以用户最终确认版本为准；实现阶段不得再调整协议字段顺序与字段号。

**FR-2.3 生成文件**

- 禁止手改 `Src/PythonServer/network/message_pb2.py` 与 `Src/Lib/AgentProtocol/message.cs`。
- 实现阶段必须按项目流程从 `Tools/message.proto` 生成协议代码，并同步 `MessageDispatch.cs` 如有需要。

### 4.3 Unity 校验行为

**FR-3.1 校验顺序**

监视 / 追踪目标时，Unity 端校验顺序应为：

1. 获取 `SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject)`。
2. 校验 `objectIndex` 是否在范围内。
3. 通过 `objectIndex` 取出 `target`。
4. 校验 `target.Name` 与 `objectName` 是否一致。
5. 一致才继续执行既有“是否已观察 / 开始观察 / 开始追踪”等逻辑。

说明：`MonitorTarget` 现有“最多同时持续观察 3 个目标”的业务限制可保持当前优先级；本期重点约束的是“按索引取出目标后，必须先完成名称校验，再创建观察运行时或开始追踪”。

**FR-3.2 名称不匹配时的返回**

- `MonitorTarget` 名称不匹配时，不创建 `ObserveRuntime`，不注册目标事件监听，不占用 3 个监视名额。
- `FollowTarget` 名称不匹配时，不调用 `StopMovement()`，不修改 `TargetFollowing`，不切换到 Follow 状态。
- 返回消息应包含：传入的 index、期望名称、实际名称，并建议 Agent 重新观察当前环境。

建议文案方向：

```text
[持续观察失败] 目标校验失败：索引[2]当前是"木箱"，不是你指定的"小球"。请重新观察当前环境后再选择目标。
```

```text
[跟随结果]失败！目标校验失败：物体[2]当前是"木箱"，不是你指定的"小球"。请重新观察当前环境后再选择目标。
```

**FR-3.3 名称匹配方式**

- 默认采用两侧 `Trim()` 后严格相等：`target.Name.Trim() == objectName.Trim()`。
- 不做模糊匹配、不忽略大小写、不做复杂归一化，避免把相似名称误判为同一对象。
- 若实现阶段确认 Unity 物体名称与 Agent 观察文本绝不会包含首尾空白，也可退化为原始字符串严格相等，但需在方案 / 实现记录中说明。

### 4.4 日志与可观测性

- `AgentService.OnAgentMonitorTarget` 日志增加 `ObjectName`。
- `AgentService.OnAgentFollowTarget` 日志增加 `ObjectName`。
- 成功返回结果应包含目标名称，便于 Agent 与开发者核对。

---

## 5. 非功能需求

- **兼容性**：这是协议字段追加；必须按 proto 生成流程同步 Python 与 C#，避免一端字段缺失。
- **安全性**：校验失败应 fail fast，不产生任何长时动作副作用。
- **可理解性**：工具描述应让 LLM 明确 `object_name` 必填且来自观察结果，减少乱填。
- **性能**：名称校验是一次字符串比较，不应带来可感知性能影响。
- **编码**：所有修改文件保持 UTF-8，含中文文案不得出现乱码。

---

## 6. 验收标准

- [ ] `monitor_target_cmd` 与 `follow_target_cmd` 的 schema 中均能看到 `object_name` 参数。
- [ ] Python 发出的 `AgentMonitorTargetRequest` 与 `AgentFollowTargetRequest` 均包含 `object_name`。
- [ ] Unity `AgentService` 与 `AgentManager` 能把 `object_name` 透传到 `AIPlayer`。
- [ ] 监视工具：索引有效且名称一致时，行为与旧版本一致，能正常开始监视。
- [ ] 监视工具：索引有效但名称不一致时，返回失败，不创建观察运行时，不占用监视名额。
- [ ] 追踪工具：索引有效且名称一致时，行为与旧版本一致，能正常开始追踪。
- [ ] 追踪工具：索引有效但名称不一致时，返回失败，不停止当前移动 / 追踪状态，不切换状态。
- [ ] 索引越界时仍返回既有“对象不存在 / 索引超出范围”类错误。
- [ ] 日志中能看到 `ObjectIndex` 与 `ObjectName`。
- [ ] PRD / solution 状态从「待确认」流转到「已确认」，开发完成并验收后 solution 流转到「已实现」。

---

## 7. 已敲定决策

- [x] D1：名称比较采用两侧 `Trim()` 后严格相等，避免普通空白导致误拒绝，但不做模糊匹配。
- [x] D2：成功返回消息同步增加目标名称，便于 Agent 记忆与日志排查。

---

*本文档由 Cursor Agent 根据用户口述需求生成，确认前请勿直接据此改业务代码。*
