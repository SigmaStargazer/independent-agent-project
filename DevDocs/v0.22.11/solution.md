# 技术方案 - v0.22.11 observe 工具结果补自身状态

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-07-17

---

## 1. 方案概述

将 `AIPlayer.Observe()` 中手动拼接 `[观察结果]\n<环境>...` 的逻辑替换为调用 `CreateMessageText("[观察结果]")`，复用既有的「自身状态 + 场景 + 环境」三块拼接，使 observe 工具结果与 `SendFeedbackToAgent` 的反馈格式一致。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | 无 | 无 |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/AIPlayer.cs` 的 `Observe(string requestId)` 方法 | 修改：替换消息拼接逻辑 |
| 协议 | `Tools/message.proto` | 无 |

## 3. 详细设计

### 3.1 数据与协议

- 无协议改动。`observe` 工具仍经 `SendToolResultMessage` 回传纯文本，仅文本内容变化。

### 3.2 Python（Brain）

- 无改动。

### 3.3 Unity（Environment）

**当前实现**（`AIPlayer.cs:687-700`）：

```csharp
public void Observe(string requestId)
{
    // 获取设备信息
    List<Dictionary<string, object>> sceneObjsInfo = new List<Dictionary<string, object>>();
    string sceneObjsInfoDesc = this.GetEnvSceneObjsInfo();

    // 拼接
    string messageToSend = $"[观察结果]\n<环境>\n{sceneObjsInfoDesc}\n</环境>";

    // 发送给Agent
    // tool_name = "observe"只用于日志打印，不用于判断
    AgentService.Instance.SendToolResultMessage(this.Name, "Observe", requestId, messageToSend);
    Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
}
```

**改后实现**：

```csharp
public void Observe(string requestId)
{
    // 复用 CreateMessageText，自动补 <你的状态> / <当前场景> / <环境> 三块
    string messageToSend = this.CreateMessageText("[观察结果]");

    // 发送给Agent
    // tool_name = "observe"只用于日志打印，不用于判断
    AgentService.Instance.SendToolResultMessage(this.Name, "Observe", requestId, messageToSend);
    Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
}
```

**要点**：

- `CreateMessageText` 内部已调用 `GetEnvSceneObjsInfo()` 生成 `<环境>` 块，故原方法里的 `sceneObjsInfo` / `sceneObjsInfoDesc` 局部变量一并删除，避免冗余。
- `CreateMessageText` 第二参数 `includeObserveTagerts` 使用默认值 `true`，observe 结果会带上「持续观察中的目标」与「进行中的定时器」摘要。这与 Agent 主动观察时关心自身注意力分配的语义一致，也和其他工具结果中可见这些摘要的设计保持统一。
- 改后输出结构（与 `SendFeedbackToAgent` 完全一致）：

```text
[观察结果]

<你的状态>
# 状态:Idle
# 横向速度:0m/s
# 纵向速度:0m/s
# 持续观察中的目标:
...
# 进行中的定时器:
...
# 计划中的动作序列:
...
# 进行中的动作序列:
...
# 进行中的动作:
...
</你的状态>

<当前场景>
场景名称: ...
场景描述: ...
</当前场景>

<环境>
...（原 GetEnvSceneObjsInfo 输出，与改前一致）
</环境>
```

### 3.4 工具 / ActionSequence（如适用）

- 不涉及。

## 4. 实现步骤

1. 编辑 `AIPlayer.cs` 的 `Observe` 方法：删除局部变量 `sceneObjsInfo` / `sceneObjsInfoDesc` 与手动拼接，替换为 `string messageToSend = this.CreateMessageText("[观察结果]");`。
2. 在 Unity 中 Play，NewGame / Continue 进入场景，让 Agent 调用 `observe`，检查反馈文本是否包含 `<你的状态>` / `<当前场景>` / `<环境>` 三块。
3. 让 Agent 进入 Hidden 状态后调 `observe`，确认 `<你的状态>` 中显示 `状态: Hidden`。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| observe 反馈变长，增加 token 消耗 | observe 是 Agent 主动调用、频率可控；增量是自身状态+场景两块，可接受。若后续发现成本过高，可加 `includeObserveTagerts: false` 或只补 `<你的状态>` |
| 回退 | 单方法单行改动，git revert 即可 |

## 6. 测试建议

- 本改动必须联调验证（依赖 Unity 运行时调用 `observe` 工具），无法纯 Python 自测。
- 联调验证点见 PRD §6 验收标准。
- 建议至少覆盖两种状态：Idle（常规）与 Hidden（需求池 #9 指出的痛点状态），确认 `<你的状态>` 块正确反映。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-07-17 | 按 §3.3 完成 `AIPlayer.Observe()` 改动：删除局部变量 `sceneObjsInfo` / `sceneObjsInfoDesc` 与手动拼接，替换为 `this.CreateMessageText("[观察结果]")`。待 Unity 联调验收。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
