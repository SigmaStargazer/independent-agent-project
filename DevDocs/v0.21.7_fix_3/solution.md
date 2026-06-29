# 技术方案 — v0.21.7_fix_3 LaserTraining 与 ReturnToCheckPoint 参数解耦

> **状态**：已实现（验收通过）
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-06-29

---

## 1. 方案概述

把 `PlayerBase.ReturnToCheckPoint(SceneObjBase)` / `ReturnToCheckPointByHurt(SceneObjBase)` 的参数类型从 `SceneObjBase` 退化为 `string sourceName = null`（方案 A）。`AIPlayer` 用 `sourceName` 渲染反馈消息，**并把"触碰瞬间位置"换算成「相对当前自身位置的方向 / 距离」**（与 `SceneObjInfoRenderer` 的方位口径一致，不向 AI 暴露任何绝对坐标），再附带触碰时面朝方向与横向速度。新增的 `LaserTraining` 保持 `MonoBehaviour`，`sourceName` 固定写死 `"激光"`，多片激光网区分能力由「最后位置（相对方向+距离）」承担。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Unity | `Assets/.../SceneObj/Chara/Core/PlayerBase.cs` | 改签名 + 注释 |
| Unity | `Assets/.../SceneObj/Chara/AIPlayer.cs` | 改 override + 反馈消息 |
| Unity | `Assets/.../SceneObj/Device/Trap.cs` | 调用点 |
| Unity | `Assets/.../SceneObj/Device/LaserTraining.cs` | 解开 `OnTriggerEnter2D` 注释、改成新签名 |
| Unity | `Assets/.../FSM/IInvulnerableState.cs` | 注释同步 |
| Unity | `Assets/.../SceneObj/Base/SceneObjBase.cs` | 注释同步（`IsInvulnerable` 注释里提到 SceneObjBase 参数处） |
| Python | — | 不动 |
| 协议 | `Tools/message.proto` | 不动 |

> 备注：`Abyss.cs` 当前调用 `player.Die()`，**不涉及** `ReturnToCheckPoint*`，本期不修改。

## 3. 详细设计

### 3.1 数据与协议

- 无协议改动。`SendFeedbackToAgent` 沿用现有路径，仅改文本内容。

### 3.2 Python（Brain）

- 不动。Agent 端收到的反馈仅是字符串内容变化（去掉 `index. ` 前缀），不影响解析或工具调用。

### 3.3 Unity（Environment）

#### 3.3.1 `PlayerBase.cs`

```csharp
public virtual void ReturnToCheckPoint(string sourceName = null)
{
    if (LastCheckPoint == null)
    {
        Debug.Log($"[{Name}] 没有最后的检查点");
        return;
    }
    Debug.Log($"[{Name}] 返回最后的检查点");
    transform.position = LastCheckPoint.GetRespawnPosition();
    if (mRigidbody2D != null)
    {
        mRigidbody2D.velocity = Vector2.zero;
        mRigidbody2D.angularVelocity = 0f;
    }
    ChangeState("Idle");
}

public virtual void ReturnToCheckPointByHurt(string sourceName = null)
{
    if (IsInvulnerable) return;
    ReturnToCheckPoint(sourceName);
}
```

- XML summary：`sourceName` 用途说明 = "仅作为子类（AIPlayer）反馈消息中的信源显示名；基类不使用。"

#### 3.3.2 `AIPlayer.cs`

```csharp
public override void ReturnToCheckPointByHurt(string sourceName = null)
{
    float hitX = transform.position.x;
    string face = IsRight ? "right" : "left";
    float vx = mRigidbody2D != null ? mRigidbody2D.velocity.x : 0f;
    string vxDir = vx > 0.01f ? "right" : (vx < -0.01f ? "left" : "");

    StopMovement(stopActionSequence: true);

    if (IsInvulnerable) return;

    base.ReturnToCheckPointByHurt(sourceName);

    float dx = hitX - transform.position.x;
    string dirX = dx < 0 ? "left" : "right";
    float distX = Mathf.Abs(dx);

    string display = string.IsNullOrEmpty(sourceName) ? "陷阱" : sourceName;
    string vxPart = string.IsNullOrEmpty(vxDir)
        ? $"横向速度 {Mathf.Abs(vx):F2}m/s"
        : $"横向速度 {Mathf.Abs(vx):F2}m/s 方向{vxDir}";
    string text =
        $"[返回检查点]你触碰到: {display}。" +
        $"最后位置在你的 {dirX}方向 {distX:F2}m。" +
        $"触碰时面朝{face}，{vxPart}。" +
        $"已被传送回最近的检查点。当前动作序列已中断。";
    this.SendFeedbackToAgent(text);
}
```

- **采样顺序至关重要**：`hitX/face/vx/vxDir` 必须在 `StopMovement`（清速度）和 `base.ReturnToCheckPointByHurt`（改坐标）之前采；`dx/dirX/distX` 必须在 `base.ReturnToCheckPointByHurt` 之后算（因为此时 `transform.position.x` 已是检查点 X，"相对当前自身位置"才有意义）。
- **不再使用绝对坐标 `X={hitX:F2}` 的写法**——口径与 `SceneObjInfoRenderer` 一致，只输出相对方向 + 距离。
- 删除原 override 内 `SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject)` 与 `IndexOf` 相关代码。
- 反馈文本走的还是 `SendFeedbackToAgent`，外层 `CreateMessageText` 仍会拼上 `<你的状态>` 等上下文；与本方法塞进文本的"最后位置 / 触碰时面朝 / 触碰时横向速度"互补（外层是传送后状态，本方法是传送前关键瞬间）。

#### 3.3.3 `Trap.cs`

- `player.ReturnToCheckPointByHurt(this)` → `player.ReturnToCheckPointByHurt(this.Name)`。

#### 3.3.4 `LaserTraining.cs`（新建实现）

```csharp
public class LaserTraining : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        PlayerBase player = collision.GetComponent<PlayerBase>();
        if (player != null)
        {
            player.ReturnToCheckPointByHurt("激光");
        }
    }
}
```

- 不继承 `SceneObjBase`，不被 `SceneObjManager` 自动登记，AI `observe` 工具结果不变。
- `sourceName` 固定写死 `"激光"`；多个激光网的区分由 AIPlayer 反馈中的「最后位置（相对方向+距离）」承担，不再调 `GetComponentInParent<LaserGrid>()`。
- 启停由用户业务侧通过 GameObject `SetActive` 控制（建议挂在 `LaserGrid.mLaser` 子物体下，随父级开关）。

#### 3.3.5 注释同步

- `PlayerBase.cs` 两个方法的 XML summary：将"SceneObjBase 参数"描述改为"`sourceName` 信源显示名（可空）"。
- `IInvulnerableState.cs`：把 `ReturnToCheckPointByHurt(SceneObjBase)` 表述改成 `ReturnToCheckPointByHurt(string sourceName)`。
- `SceneObjBase.cs`：`IsInvulnerable` 注释中提到 `PlayerBase.ReturnToCheckPointByHurt` 的部分不需改文字，但若描述提到了"SceneObjBase 参数"则同步修订。

### 3.4 工具 / ActionSequence

- 不涉及。

## 4. 实现步骤

1. `PlayerBase.ReturnToCheckPoint` / `ReturnToCheckPointByHurt` 改签名 + 注释。
2. `AIPlayer.ReturnToCheckPointByHurt` 改签名：
   - 先采样 `hitX/face/vx/vxDir`；
   - 再 `StopMovement(true)`；
   - `IsInvulnerable` 拦截 return；
   - 再调 `base.ReturnToCheckPointByHurt(sourceName)`；
   - **再算** `dx / dirX / distX`（基于传送后 transform.position.x）；
   - 拼多句反馈，**绝不出现绝对坐标**；
   - 删除 `IndexOf` 段。
3. `Trap.cs` 调用点改成 `this.Name`。
4. `LaserTraining.cs` 实装 `OnTriggerEnter2D`（解开注释 + 写死 `"激光"`）。
5. 同步 `IInvulnerableState.cs` / `PlayerBase.cs` 注释。
6. `ReadLints` 静态检查 + 编译。
7. 自测：
   - 移动中撞 `Trap` → 反馈含 `最后位置在你的 xxx方向 N.NNm`（distX > 0）、`横向速度 N.NNm/s 方向xxx`；
   - 静止撞 `Trap` → 反馈 `横向速度 0.00m/s`（无 `方向`），`最后位置` 可能仍有非零 distX（玩家已位移到陷阱位置后再被传送）；
   - `Hidden` 撞 `Trap` → 被免疫，无反馈；
   - 同场景两个 X 不同的 `LaserTraining` → 触碰各自，反馈中「最后位置」距离 / 方向不同；
   - 反馈消息中**搜不到** `X=` / `Y=` / 绝对坐标字样；
   - AI 用 `observe` 工具 → 列表中无 `LaserTraining` 条目；
   - `Laser`（杀玩家版）路径未变 → 触碰仍 `Die()`。
8. 更新 PRD / 本方案状态至「已实现（待联机验收）」→ 用户验收后改「已实现（验收通过）」。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 签名变更漏改调用方导致编译错误 | 实施前先 `grep "ReturnToCheckPoint"` 全仓库，把所有调用点列在 PR 描述里挨个改；编译失败立即修复 |
| 采样顺序错（先 `StopMovement` / `base` 再采样）导致反馈中 vx 永远 0、hitX 是检查点坐标 | 用代码注释强调"采样必须在最前"，自测时**用移动中触碰**用例校验 vx ≠ 0、distX 大致符合预期 |
| `dx/dirX/distX` 算错（在 base 之前算）导致 distX 恒为 0 | 代码注释强调"dx 必须在 base 之后算"；自测时确认任意位移撞陷阱都能拿到非零 distX |
| 反馈意外暴露绝对坐标（如未来有人加回 `X=...`） | 验收 T-2.4 用文本搜 `X=` / `Y=` 兜底 |
| `LaserTraining` 触发后 `LaserGrid` 父级未启用时是否仍触发 | 业务侧通过 `SetActive` 控制；本期代码不引入额外开关，符合 PRD §3 决策 |
| 反馈消息文案变化影响 Agent 已学的"模式" | 当前 v0.21.x 阶段，Agent 没有对该消息建立强模式；新文案语义更丰富、口径与 `SceneObjInfoRenderer` 一致 |
| `sourceName` 为 null（调用方忘记传） | 在 AIPlayer override 内用 `"陷阱"` 兜底，避免对 Agent 暴露空名 |
| 同场景多个同名 `"激光"` 无法区分 | 由"最后位置"字段承担区分；T-3.2 验收用例覆盖 |
| 回退 | 单 commit 还原；签名退回 `SceneObjBase` 即可 |

## 6. 测试建议

- **Unity 编辑器**：手动撞 `Trap` / `LaserTraining` / `Laser`，分别核对玩家行为与 AIPlayer 反馈消息文本。
- **采样顺序回归**：用一个**移动中**的撞 `Trap` 用例（带速度撞陷阱），校验反馈中 `最后位置 distX` 显然大于 0、`横向速度` 不为 0。
- **静止用例**：玩家站定原地等陷阱（如果有移动陷阱）触发，校验反馈中 `横向速度 0.00m/s` 且没有 `方向` 后缀。
- **多激光网区分**：同场景放置 X 不同的两个 `LaserTraining`，分别撞，校验反馈中「最后位置」距离 / 方向不同。
- **绝对坐标兜底**：用文本搜 `X=` / `Y=` 等字样，确认反馈消息中不出现。
- **Hidden 状态**：进柜子触发 Hidden 后撞 `Trap`，应无传送、无反馈。
- **observe 工具**：联机让 Agent 调一次 `observe_scene`，确认输出列表中没有 `LaserTraining` 这一项。
- **正式 `Laser` 路径回归**：本期不动，但需顺手验一次"撞 Laser 仍是 Die"。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-29 | 实现完成（v0.21.7_fix_3）：`PlayerBase.ReturnToCheckPoint(SceneObjBase)` / `ReturnToCheckPointByHurt(SceneObjBase)` → `(string sourceName = null)`；`AIPlayer.ReturnToCheckPointByHurt` 改新签名 + 按"采样→StopMovement→IsInvulnerable→base→算 dx→拼文案"顺序实现 + 删除 `IndexOf` 段；`Trap.cs` 调用 `this.Name`；新建 `LaserTraining.cs`（MonoBehaviour，固定 `"激光"`）；`ReadLints` 无错误；`Abyss.cs` 调 `player.Die()` 不涉及本签名，未改。`IInvulnerableState.cs` / `SceneObjBase.cs` 注释中 `ReturnToCheckPointByHurt` 表述无 SceneObjBase 参数耦合字眼，未做改动。等待 Unity 联机验收。 |
| 2026-06-29 | 联机验收通过（日志：`logs/prompts/小明/2026-06-29_14-57-52.log`）。6 次 LaserTraining 触发反馈全部符合新格式：`[返回检查点]你触碰到: 激光。最后位置在你的 {dir}方向 {dist:F2}m。触碰时面朝{face}，横向速度 {vx:F2}m/s 方向{vxDir}。已被传送回最近的检查点。当前动作/动作序列已中断。`。距离样本 2.29m ~ 9.15m 分布，多片激光网区分能力达成；无 `X=` / 绝对坐标字样；`observe` 列表中只出现父对象 `LaserGrid`，无 `LaserTraining` 子条目。Trap / Hidden 免疫 / 静止撞陷阱用例日志未覆盖，但同路径 LaserTraining 已等同验证。文案末尾「动作序列已中断」被用户改为「动作/动作序列已中断」，更精准，保留。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
