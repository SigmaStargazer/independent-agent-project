# PRD - v0.22.11 observe 工具结果补自身状态

> **状态**：已确认
> **对应需求**：`DevDocs/需求池/backlog.md` 条目 #9
> **最后更新**：2026-07-17

---

## 1. 背景与目标

需求池 #9 指出：`observe` 工具反馈只描述外部环境，不包含 Agent 自身状态。后果是 Agent 在 Hidden / Dead / Stunned / Follow 等状态下观察环境后，容易遗忘自身约束（如「正躲在柜子里不能移动」），下一步规划 `move_cmd` 被 `IsImmovable` 守卫驳回。

经代码梳理（详见 `solution.md` §2），当前现状与需求池原始描述有出入：

- **环境反馈型推送**（受伤传送、定时器到期、动作序列完成/中断、观察目标消失）已走 `SendFeedbackToAgent` -> `CreateMessageText`，**已含** `<你的状态>` 块。
- **工具结果型直接回传**（observe / monitor / move / interact 等 20+ 处）直接调 `SendToolResultMessage`，**不含**自身状态。

本期只解决其中最典型的一处：`observe` 工具结果。

## 2. 范围

### 2.1 本期包含

- 改 `AIPlayer.Observe()`：把手动拼接 `[观察结果]\n<环境>...` 改为调用 `CreateMessageText("[观察结果]")`，使 observe 结果自动带上 `<你的状态>` / `<当前场景>` / `<环境>` 三块。

### 2.2 本期不包含

- 其他工具结果（`MonitorTarget` / `GetMonitorRecords` / `Move` / `Interact` / `Select` / `TextInput` / `SetTimer` / `PlanActionSequence` 等）的反馈格式调整。
- `GetSelfStateInfo` 状态描述的角色化强化（需求池 #9 候选方案中提到的 `is_immovable` / `is_invulnerable` / `is_undetectable` / `following` 等字段标签化）。
- 协议改动、Python 侧改动。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| Agent（小明） | Hidden 状态下调用 `observe` 观察环境 | 反馈文本头部带 `<你的状态>`，明确看到「状态: Hidden」及当前速度、动作序列等，避免随后误调移动工具 |
| Agent（小明） | 正常 Idle 状态下调用 `observe` | 反馈除环境外也带自身状态，信息密度一致，便于一次性决策 |

## 4. 功能需求

### 4.1 observe 工具结果格式统一

- `Observe()` 方法不再手动拼接 `<环境>` 块，改用 `CreateMessageText("[观察结果]")`。
- 改后输出格式与 `SendFeedbackToAgent` 一致：`[观察结果]` + `<你的状态>` + `<当前场景>` + `<环境>` 四块，以空行分隔。
- `includeObserveTagerts` 使用 `CreateMessageText` 默认值 `true`，即 observe 结果也带「持续观察中的目标」与「进行中的定时器」摘要（与 Agent 主动观察时关心自身注意力分配的语义一致）。

## 5. 非功能需求

- **Token 成本**：每次 `observe` 调用比原版多出 `<你的状态>` + `<当前场景>` 两块文本。`observe` 是 Agent 主动调用、频率可控，增量可接受。
- **兼容性**：不改协议、不改 Python、不改工具 schema，仅 Unity 单文件单方法改动。

## 6. 验收标准

- [ ] Agent 调用 `observe` 工具后，收到的反馈文本包含 `<你的状态>` 块，块内可见当前 `状态:` 字段。
- [ ] Hidden 状态下 `observe`，反馈中 `<你的状态>` 块显示 `状态: Hidden`。
- [ ] observe 反馈仍包含 `<环境>` 块，内容与改前一致（来自 `GetEnvSceneObjsInfo()`）。
- [ ] observe 反馈包含 `<当前场景>` 块。
- [ ] 其他工具（monitor / move / interact 等）反馈格式不受影响。

## 7. 待确认问题

- 无。

---

*本文档由 Cursor Agent 根据 `DevDocs/需求池/backlog.md` 条目 #9 生成，确认前请勿直接据此改代码。*
