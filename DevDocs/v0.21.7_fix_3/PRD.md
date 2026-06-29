# PRD — v0.21.7_fix_3 LaserTraining 与 ReturnToCheckPoint 参数解耦

> **状态**：已实现（验收通过）
> **对应需求**：用户口述 — `LaserTraining` 设备的开发与 `PlayerBase.ReturnToCheckPoint(SceneObjBase)` 参数耦合问题
> **关联方案**：`solution.md`
> **关联主版本**：`DevDocs/v0.21.7/PRD.md`、`DevDocs/v0.21.7/solution.md`
> **最后更新**：2026-06-29

---

## 1. 背景与目标

### 1.1 需求来源（用户口述）

- `LaserGrid` 子物体 `Laser` 当前 `OnTriggerEnter2D` 中调用 `player.Die()`，会击杀玩家。
- 训练场需要一种新的 `LaserTraining` 设备：玩家触碰不死亡，而是被传送回最近的 CheckPoint，让 AI Player 可以在训练场反复练习"过激光网"。
- 实现时遇到的耦合问题：`PlayerBase.ReturnToCheckPoint(SceneObjBase sceneObjBase)` 需要传一个 `SceneObjBase` 参数，但用户**不希望 `LaserTraining` 是 `SceneObjBase`**——因为 `SceneObjBase` 会被 `SceneObjManager` 自动登记进可观察 / 可交互列表，而设计上希望"被管理"的只是父物体 `LaserGrid`，子物体 `LaserTraining` 不应作为独立 SceneObj 对 AI 暴露。

### 1.2 为什么 `ReturnToCheckPoint` 当初设计成要传 `SceneObjBase`

复盘 `PlayerBase.ReturnToCheckPoint(SceneObjBase)` / `ReturnToCheckPointByHurt(SceneObjBase)` 这两个方法签名（v0.21.0 引入、v0.21.7-fix 拆分）：

1. **方法签名只用到一次 `sceneObj`**：在 `AIPlayer.ReturnToCheckPointByHurt` 里——给 Agent 发反馈消息 `[返回检查点]你触碰到: {index}. {sceneObjName}。已被传送回最近的检查点。`，里面取 `sceneObj.Name` 与 `SceneObjManager.Instance.GetSceneObjsExcluding(...).IndexOf(sceneObj)`。
2. `PlayerBase.ReturnToCheckPoint` / `PlayerBase.ReturnToCheckPointByHurt` 这两个基类版本**完全没有使用 `sceneObj`**，参数纯粹是为子类（AIPlayer）反馈消息留的"信源标识"。
3. 类型选 `SceneObjBase` 是因为：当时所有"会让玩家受伤"的源头（`Abyss` / `Trap` / `Laser`）**都已经是 `SceneObjBase`**，传 `SceneObjBase` 顺手能拿到 `Name` 和"在 SceneObjManager 中的 index"。

### 1.3 现状的限制

- 把信源类型硬编码为 `SceneObjBase`，等于"想让玩家受伤回 CheckPoint，伤害源就必须是 SceneObjBase"。这与"伤害子物体不应独立成 SceneObj"是冲突的。
- 现实业务里"伤害子物体"会越来越多：`LaserGrid` 下挂多个 `Laser` 子物体、未来可能挂 `Spike`、`ElectricField` 等。每个都包成 SceneObjBase 会把 SceneObjManager 撑大，且 AI `observe` 工具会看到一堆冗余条目。

## 2. 候选方案（推荐排序）

| # | 方案 | 一句话 | 改动量 | 反馈消息可读性 | 灵活度 | 评分 |
|---|---|---|---|---|---|---|
| **A** | **签名换成 `string sourceName` / 重载** | `ReturnToCheckPointByHurt(string sourceName)` 直接接收"伤害源显示名"，调用方各自决定怎么取名（SceneObj 传 `Name`，伤害子物体传父 SceneObj 的 `Name` 或自定义字符串） | 小（改 1 个签名 + 全部调用方传 string） | 高（名字由调用方控制） | 高 | ★★★★★ |
| **B** | **新增 `IDamageSource` 接口** | 抽一个最小接口 `interface IDamageSource { string Name { get; } GameObject ParentSceneObj { get; } }`（后两个字段按需要），`ReturnToCheckPointByHurt(IDamageSource)`；`SceneObjBase` 默认实现它，`LaserTraining` 等 MonoBehaviour 也可实现 | 中（加接口、改 PlayerBase 签名、所有调用方实现接口） | 高 | 高（未来扩展强） | ★★★★ |
| **C** | **新增专门 API `ReturnToCheckPointByDamage(GameObject source)`** | 给"非 SceneObj 的伤害子物体"留一条新入口，内部用 `source.GetComponentInParent<SceneObjBase>()` 兜底反向找父 SceneObj 作为反馈信源 | 中（加一条 API） | 中（依赖父级是 SceneObj 才能拿到 Name；找不到时反馈信息退化） | 中 | ★★★ |
| **D** | **`LaserTraining` 强行继承 SceneObjBase 但隐藏自己** | 通过覆写 `IsObservable` / `IsInteractable` 或在 `SceneObjManager` 注册时过滤掉，让它"是 SceneObj 但对 AI 不可见" | 小（只改 LaserTraining + 可能改 SceneObjManager） | 高（直接复用现签名） | 低（与你的"不应被 Manager 管理"诉求冲突） | ★★ |
| **E** | **去掉参数，让 AIPlayer 改用「最后一次受伤事件」机制** | `PlayerBase.ReturnToCheckPointByHurt()` 无参；调用方先调 `RegisterLastDamageSource(name)` 再调 `ReturnToCheckPointByHurt()`；AIPlayer 反馈消息读上一次注册的源 | 中—大（新增受伤事件总线） | 高 | 高（解耦更彻底） | ★★ |

### 推荐：方案 A（`string sourceName`）

#### 为什么 A 最好

1. **真正用到的只是字符串**：复盘 `AIPlayer.ReturnToCheckPointByHurt` 实际用法，`sceneObj` 只贡献了两个东西——`Name`（显示名）和 `IndexOf(sceneObj)`（编号）。其中**编号那一段本质是为了给 Agent 一个"对照 observe 工具结果"的索引**，但 `LaserTraining` 这类子物体**本来就不在 observe 列表里**，强行算 index 反而会得到 `-1`，不如直接用 `Name`。
2. **签名退化最干净**：从依赖一个具体类（SceneObjBase）退到只依赖一个原语（string），不引入新接口、新 API、新事件总线。
3. **调用方各自负责取名**，符合"信源标识由源头决定"的直觉：
   - `Trap`/`Abyss`：`player.ReturnToCheckPointByHurt(this.Name)`（自身就是 SceneObj）；
   - `LaserTraining`：`player.ReturnToCheckPointByHurt(GetComponentInParent<LaserGrid>()?.Name ?? "激光")` —— 把"被父 SceneObj 的 Name 反馈"做成调用点的显式选择，而不是 PlayerBase 替你猜；
4. **去掉 index 那段反馈逻辑后**，AIPlayer 端反馈消息变成：`[返回检查点]你触碰到: 激光网。已被传送回最近的检查点。当前动作序列已中断。`（去掉 `0. ` 这种实质无意义的 index 前缀），人类与 Agent 都更易读。
5. **回滚最便宜**：单签名变更，可以单 commit 回滚。

#### A 的开发成本

| 文件 | 改动 |
|---|---|
| `PlayerBase.cs` | `ReturnToCheckPoint(SceneObjBase)` → `ReturnToCheckPoint(string sourceName = null)`；`ReturnToCheckPointByHurt(SceneObjBase)` → `ReturnToCheckPointByHurt(string sourceName = null)` |
| `AIPlayer.cs` | `override ReturnToCheckPointByHurt(string sourceName)`；反馈消息改用 `sourceName`，去掉 `SceneObjManager.GetSceneObjsExcluding/IndexOf` 那段 |
| `Trap.cs` | `player.ReturnToCheckPointByHurt(this)` → `player.ReturnToCheckPointByHurt(this.Name)` |
| `Abyss.cs`（若存在同样调用） | 同上 |
| `LaserTraining.cs`（新建） | `player.ReturnToCheckPointByHurt(GetComponentInParent<LaserGrid>()?.Name ?? "激光")` |
| `IInvulnerableState.cs` 注释 / `PlayerBase` 注释 | 同步签名说明 |

### 备选：方案 B（`IDamageSource` 接口）

适合"未来还想给 Agent 反馈更多伤害源元信息（伤害类型、显示颜色、技能 ID 等）"。**当前需求只用到名字，引入接口是为未来 buff、过早抽象**。除非你明确未来还有结构化字段要带，否则不选。

### 备选：方案 C（`GameObject source` + 反向找 SceneObj）

引入"隐式依赖父对象是 SceneObj"的耦合，找不到时反馈信息会退化。比 A 更脆弱。

### 不推荐：方案 D（强行 SceneObj 化）

直接违背"不希望 LaserTraining 被 SceneObjManager 管理"的诉求；即便靠 `IsObservable=false` 这类标志屏蔽，也只是在管理层多一层例外，污染数据结构。

### 不推荐：方案 E（受伤事件总线）

为了避免一个签名参数，引入跨对象状态（"上一次受伤源"），并且要保证调用方一定先 `Register` 再 `ReturnToCheckPointByHurt`，时序耦合更脆。

---

## 3. 决策结果（用户已确认 — 2026-06-29）

- **方案选型**：**A**（`ReturnToCheckPointByHurt(string sourceName = null)`）。
- **中性版本 `ReturnToCheckPoint` 一并改签名**：是。两个签名口径统一为 `string sourceName = null`，调用方不传也兼容现有调试 / 系统重置入口。
- **AIPlayer 反馈消息去掉 index 编号**：是。改为多句结构，并把绝对坐标改成「相对当前自身位置的方向 + 距离」（口径与 `SceneObjInfoRenderer` 中 `方位:在你的 {Direction}方向 {Distance:F2}m 位置` 保持一致；不引入绝对 X）。
  - 模板（用「最后位置」描述被传送前的位置）：
    `[返回检查点]你触碰到: {sourceName}。最后位置在你的 {dirX}方向 {distX:F2}m。触碰时面朝{face}，横向速度 {abs(vx):F2}m/s 方向{vxDir}。已被传送回最近的检查点。当前动作序列已中断。`
  - `dirX` / `distX`：以当前（传送后）自身位置为参照，按 `dx = hitX - currentX` 计算：`dirX = dx < 0 ? "left" : "right"`，`distX = Mathf.Abs(dx)`。与 `SceneObjInfoMapper` 对 SceneObj 的方向计算口径一致——dx 为 0 时按 `right` 处理，不引入特例分支。
  - 当 `|vx| ≤ 0.01` 时省略 `方向{vxDir}`，仅写 `横向速度 0.00m/s`，与 `SceneObjInfoRenderer` 中 `speedXStr` 的 0 处理一致。
  - 例子（含位移）：`[返回检查点]你触碰到: 激光。最后位置在你的 right方向 5.32m。触碰时面朝right，横向速度 1.20m/s 方向right。已被传送回最近的检查点。当前动作序列已中断。`
  - 例子（静止 + 几乎原地）：`[返回检查点]你触碰到: 陷阱。最后位置在你的 right方向 0.00m。触碰时面朝right，横向速度 0.00m/s。已被传送回最近的检查点。当前动作序列已中断。`
- **触碰前位置 / 朝向 / 速度采样适用范围**：所有 `ReturnToCheckPointByHurt` 路径（`Trap` / `LaserTraining` / 未来任意调用方）。**触碰前位置 / 朝向 / 速度**必须在 `base.ReturnToCheckPointByHurt`（传送）之前采样；**相对方向 / 距离**则用「采样到的 hitX」减去「传送后的 transform.position.x」得到，因为目的是给 AI 一个相对**当前位置**（即检查点附近）的描述。
- **`LaserTraining.sourceName` 简化**：固定写死 `string sourceName = "激光"`（不再用 `GetComponentInParent<LaserGrid>()?.Name`）。同名 `"激光"` + 不同 `最后位置` 已足够让 AI 区分多片激光网。
- **`Laser`（杀玩家版）不动**：本期只新增 `LaserTraining` 走新签名；`Laser` 继续走 `player.Die()` 路径。
- **`LaserTraining` 是否随 `LaserGrid.Trigger()` 启停**：业务侧自行处理（把 `LaserTraining` GameObject 放在 `LaserGrid.mLaser` 子物体下，随父 SetActive 一起启停），本期代码与方案**不引入额外的开关字段或 FSM**。

---

## 4. 功能需求

### 4.1 `PlayerBase` 签名变更（核心）

- `PlayerBase.ReturnToCheckPoint(SceneObjBase)` → `ReturnToCheckPoint(string sourceName = null)`。
- `PlayerBase.ReturnToCheckPointByHurt(SceneObjBase)` → `ReturnToCheckPointByHurt(string sourceName = null)`。
- 基类 `ReturnToCheckPointByHurt` 内部只做 `IsInvulnerable` 拦截 + 调 `ReturnToCheckPoint(sourceName)`，不使用 `sourceName`。
- 基类 `ReturnToCheckPoint` 不使用 `sourceName`，行为与原版完全一致（修改位置、清零速度、`ChangeState("Idle")`、`LastCheckPoint` 守卫）。
- 注释同步更新：移除"`SceneObjBase` 参数"的描述，改为"`sourceName` 仅用于子类（AIPlayer）反馈消息"。

### 4.2 AIPlayer.ReturnToCheckPointByHurt 签名同步与反馈消息升级

- 重写 override：`public override void ReturnToCheckPointByHurt(string sourceName = null)`。
- 反馈消息升级为「触碰源 + 最后位置（相对当前自身位置的方向 / 距离）+ 触碰时面朝 + 触碰时横向速度」的多句结构。**不向 AI 暴露任何绝对坐标**，与 `SceneObjInfoRenderer` 的方位口径保持一致。
- 流程：
  1. **先采样触碰瞬间状态**（必须在 `StopMovement` 与 `base.ReturnToCheckPointByHurt` 之前）：
     - `float hitX = transform.position.x;`
     - `string face = IsRight ? "right" : "left";`
     - `float vx = mRigidbody2D != null ? mRigidbody2D.velocity.x : 0f;`
     - `string vxDir = vx > 0.01f ? "right" : (vx < -0.01f ? "left" : "");`
  2. `StopMovement(stopActionSequence: true);`
  3. 若 `IsInvulnerable` 则 `return`（保持 v0.21.7-fix_1 语义不变，不发反馈）；
  4. 调 `base.ReturnToCheckPointByHurt(sourceName);` 完成实际传送；
  5. **传送后**计算「最后位置」相对当前自身位置的方向 / 距离：
     ```csharp
     float dx = hitX - transform.position.x;
     string dirX = dx < 0 ? "left" : "right";
     float distX = Mathf.Abs(dx);
     ```
  6. 拼反馈：
     ```csharp
     string display = string.IsNullOrEmpty(sourceName) ? "陷阱" : sourceName;
     string vxPart = string.IsNullOrEmpty(vxDir)
         ? $"横向速度 {Mathf.Abs(vx):F2}m/s"
         : $"横向速度 {Mathf.Abs(vx):F2}m/s 方向{vxDir}";
     string text =
         $"[返回检查点]你触碰到: {display}。" +
         $"最后位置在你的 {dirX}方向 {distX:F2}m。" +
         $"触碰时面朝{face}，{vxPart}。" +
         $"已被传送回最近的检查点。当前动作序列已中断。";
     SendFeedbackToAgent(text);
     ```
  7. **彻底去掉** `SceneObjManager.GetSceneObjsExcluding(...)` 与 `IndexOf` 的相关代码。
- `sourceName` 为 null 时使用回退字符串 `"陷阱"`。

> 设计要点：
> - **不暴露绝对坐标**：所有位置都换算成「相对当前自身的方向 + 距离」，口径与 `SceneObjInfoRenderer.RenderSceneObj` 中 `方位:在你的 {Direction}方向 {Distance:F2}m 位置` 一致。
> - **dx = 0 时按 `right` 处理**：沿用 `SceneObjInfoMapper` 中 `direction = xDiff < 0 ? "left" : "right"` 的口径，不引入"原地"特例分支，保持渲染一致性。
> - **速度 0 时省略方向后缀**：沿用 `SceneObjInfoRenderer.speedXStr` 的处理（`SpeedX <= 0.01f` 时只写 `0m/s`，不写方向）。
> - 选 X（横向）即可：游戏是 2D 横版，主要走位维度是 X；Y 信息留作未来扩展，本期不出现在反馈里。
> - 采样顺序：所有「触碰瞬间状态」（hitX/face/vx/vxDir）必须在第 1 步采样；「相对距离」依赖采样到的 hitX 与传送后的 `transform.position.x`，所以在第 5 步算。`StopMovement` 会清零速度 → 必须在采样后；`base.ReturnToCheckPointByHurt` 会改坐标 → 必须在采样 hitX 之后、但在算 dx 之前。

### 4.3 既有调用方签名迁移

- `Trap.cs`：`player.ReturnToCheckPointByHurt(this)` → `player.ReturnToCheckPointByHurt(this.Name)`。
- `Abyss.cs`（若存在 `ReturnToCheckPoint*` 调用）：同上迁移。**本步骤需在实施前 grep 确认所有调用点**，避免漏改导致编译错误。

### 4.4 新增 `LaserTraining`

- 位置：`Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Device/LaserTraining.cs`。
- 类型：`MonoBehaviour`（**不继承 SceneObjBase**），不被 `SceneObjManager` 管理。
- 行为：
  - `OnTriggerEnter2D(Collider2D collision)`：取 `collision.GetComponent<PlayerBase>()`；非空则调 `player.ReturnToCheckPointByHurt("激光")`。
  - **`sourceName` 直接写死 `"激光"`**，不再用 `GetComponentInParent<LaserGrid>()?.Name`。多激光网区分由「触碰前位置（X）」承担。
- **启停**：用户业务侧处理，把 `LaserTraining` 挂在 `LaserGrid.mLaser` 子物体下，`SetActive` 随父级一起切换。本期代码不引入开关字段或 FSM。

### 4.5 注释 / 文档同步

- `PlayerBase.cs` `ReturnToCheckPoint` / `ReturnToCheckPointByHurt` 的 XML summary 同步更新签名描述。
- `IInvulnerableState.cs` 注释里若出现 `SceneObjBase` 参数描述，同步改为"signal sourceName"。
- `AGENTS.md` § 三 / § 四 若有"伤害源必须是 SceneObjBase"类的隐含约定，本版本不动 AGENTS.md，但 `solution.md` 里把这一点列进"实现记录 / 兼容性说明"。

## 5. 非功能需求

- **兼容**：v0.21.7 / fix_1 / fix_2 已落地行为（`IInvulnerableState` 免疫、Cabinet Hidden、EnemyBase Chase 退出等）不得回归。
- **可观测**：`LaserTraining` 不进入 `SceneObjManager`，AI `observe` 工具结果不变（不出现额外条目）。
- **可读性**：AIPlayer 反馈消息去掉 `0. `, `1. ` 这种 index 前缀，更接近自然语言。
- **协议**：无需改 `Tools/message.proto`、Python 工具，无跨语言改动。

## 6. 验收标准

- [ ] T-1.1：单 commit 编译通过；`Trap`、`Abyss`（若有）调用方已更新到新签名。
- [ ] T-1.2：`PlayerBase.ReturnToCheckPoint` / `ReturnToCheckPointByHurt` 签名为 `(string sourceName = null)`；XML summary 已同步。
- [ ] T-2.1：`Trap` 触发后 AIPlayer 收到反馈，**含相对最后位置 / 触碰时面朝 / 触碰时横向速度**，例如 `[返回检查点]你触碰到: 陷阱。最后位置在你的 right方向 4.20m。触碰时面朝right，横向速度 1.20m/s 方向right。已被传送回最近的检查点。当前动作序列已中断。`（无 `0. ` 前缀、无绝对坐标）。
- [ ] T-2.2：玩家处于 `IInvulnerableState`（Hidden）时撞 `Trap`，不被传送、不发反馈（与 fix_1 一致）。
- [ ] T-2.3：玩家静止（vx≈0）撞 `Trap`，反馈中 `横向速度 0.00m/s`（不带 `方向`）。
- [ ] T-2.4：反馈消息中**绝对不能出现 `X=`、`Y=`、`transform.position` 之类的绝对坐标**。
- [ ] T-3.1：新建 `LaserTraining` 子物体挂在 `LaserGrid.mLaser` 下，玩家触碰后被传送回最近 CheckPoint，AIPlayer 收到反馈例如 `[返回检查点]你触碰到: 激光。最后位置在你的 right方向 5.32m。触碰时面朝right，横向速度 1.20m/s 方向right。已被传送回最近的检查点。当前动作序列已中断。`
- [ ] T-3.2：同场景放置**两个 X 不同的 `LaserTraining`**，分别触碰能从反馈消息的「最后位置」距离 / 方向区分出来。
- [ ] T-3.3：`LaserGrid.Trigger()` 切换 Inactive 状态后，`mLaser` 子物体 SetActive(false)，`LaserTraining` 触发器随之失效（业务侧自测）。
- [ ] T-3.4：`LaserTraining` 不出现在 AI `observe` 工具输出列表里（不被 `SceneObjManager` 管理）。
- [ ] T-4.1：`Laser`（杀玩家版）行为保持 `player.Die()`，本期不动。
- [ ] T-5.1：`solution.md` 含改动清单、自测计划、回滚步骤；本 PRD 与 `solution.md` 状态在用户验收通过后从「待确认」改为「已实现（验收通过）」。

## 7. 待确认问题

- [ ] 业务侧：`LaserTraining` 的 Collider2D 几何形状是否完全复用 `Laser` 同款配置？本期不在代码层强约束，但建议在关卡层保持一致以避免训练 / 正式版手感差异。

---

*本文档由 Cursor Agent 根据用户口述需求生成；§3 决策结果已经用户 2026-06-29 确认。下一步：补 `solution.md`，经用户审核后实施。*