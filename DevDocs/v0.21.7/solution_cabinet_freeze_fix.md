# v0.21.7 Cabinet 位置冻结 Fix

> **状态**：已实现
> **最后更新**：2026-06-25
> **关联**：`DevDocs/v0.21.7/solution.md` — 本文件是 v0.21.7 主方案内 Cabinet 相关物理冻结与无敌机制的补充修复方案。v0.21.7 主方案已实现，本 fix 在同一个版本内完成。

## 1. 背景

v0.21.7 主方案新增了 `Cabinet` 柜子装置和 `PlayerBase.HiddenState`。但当前实现在进入 Hidden 后，玩家的 `Rigidbody2D` 仅被归零速度，没有锁定位置。

**问题表现**：
- 瞬移到 `mEnterAnchor` 后，下一帧重力立刻将玩家向下拉；
- 其它刚体碰撞/移动平台仍能推动玩家位置；
- 玩家无法稳定地"停"在柜子里，与 Cabinet 再次交互的体验不可靠。

**本 fix 的目标**：
1. 进入 Hidden 后锁定玩家位置（重力/外力/碰撞推动/平台移动均不能改变坐标）；
2. Hidden 状态下无敌（对所有致死伤害免疫）；
3. `ReturnToCheckPointByHurt` 在 Hidden 下也被免疫；
4. 退出 Hidden 后完全还原物理状态，不留副作用。

## 2. 已决策项总览

以下决策来自设计讨论，均已闭环，直接写入代码：

| # | 主题 | 决策 | 核心理由 |
|---|------|------|---------|
| D1 | 冻结方式 | **RigidbodyConstraints2D.FreezeAll** | 物理引擎中「锁位置」的官方机制；不改变 bodyType（保持 Dynamic），不破坏 Trigger 交互（玩家可再次 Interact 退出柜子）；进入保存原值、退出还原，可逆性最好 |
| D2 | Hidden 是否无敌 | **是** | Hidden 状态下所有致死伤害源无效；语义从「不被检测」扩展到「不可见+不可伤害」 |
| D3 | 无敌的实现方式 | **新增 IInvulnerableState 接口** | 不复用 IUndetectableState，每个接口只标记一种属性，职责纯净 |
| D4 | 无敌的拦截入口 | **在 CharaBase.Die() 入口单点拦截** | 现有三处伤害入口（EnemyBase.OnAttackEnter / Laser / Abyss）全走 Die()；不走非 IInvulnerable 的伤害源未来也自然免疫；无需改动 3 个伤害源文件 |
| D5 | Cabinet.velocity=0 | **删除** | 冻结职责收敛在 PlayerBase.OnHiddenEnter/OnHiddenExit 中，不在 Cabinet 中双重管控 |
| D6 | Hidden→Dead constraints | **无条件还原** | OnHiddenExit 不管下一状态是什么，都还原 constrants 到保存的原值；如果未来 Dead 要加物理约束，DeadState 自己负责 |
| D7 | 免疫传送判定 | **新增 ReturnToCheckPointByHurt** | 与 Die() 设计一致——伤害入口在被攻击方接口里；Trap 等机关调 ByHurt，未来中性重生可调原版 ReturnToCheckPoint |
| D8 | 新接口命名 | **IInvulnerableState** | 与现有 IUndetectableState / IImmovableState 平行 |
| D9 | DeadState 是否实现 IInvulnerableState | **是** | 额外防御：避免已 Dead 状态被重复 Die() 调用触发 OnDeadAgain |
| D10 | Trap 改用 ReturnToCheckPointByHurt | **是** | 体现「受伤型重生」语义；Hidden 状态下免疫传送 |
| D11 | `IsInvulnerable` 虚属性放在哪里？ | **SceneObjBase**（与 IsUndetectable / IsImmovable 一致） | 未来物体（Device、Trap、Cabinet 等）也可能被设计成「可破坏」，统一在 SceneObjBase 暴露；不需要为「破坏」单独再加一套机制 |
| D12 | AIPlayer 是否 override `ReturnToCheckPointByHurt`？ | **是** | 把现有 `ReturnToCheckPoint` override 中的 StopMovement + SendFeedbackToAgent 逻辑挪到 ByHurt 上；中性的 ReturnToCheckPoint 取消 override，沿用 PlayerBase 默认实现 |
| D13 | Hidden 下隐藏玩家渲染的方式 | **关闭 Renderer 组件（方案 B）** | 不能 SetActive(false)（会断 FSM/Collider/Trigger，破坏退出柜子链路）；关 `Renderer` 基类一次覆盖 Sprite/Mesh/Particle/Trail 等所有渲染；进入保存原 enabled 列表、退出按列表还原 |
| D14 | Hidden 下是否额外做柜子门等视觉表现 | **否** | 属于美术 / 关卡设计师职责（柜子门精灵直接放场景）；本 fix 只解决「玩家自身渲染消失」 |

### 2.1 D13 实现要点

```csharp
// PlayerBase 新增字段
private RigidbodyConstraints2D mHiddenSavedConstraints;
private List<Renderer> mHiddenDisabledRenderers = new List<Renderer>();

// OnHiddenEnter 中处理渲染（与冻结一起）
mHiddenDisabledRenderers.Clear();
foreach (var r in GetComponentsInChildren<Renderer>(includeInactive: false))
{
    if (r.enabled)
    {
        mHiddenDisabledRenderers.Add(r);
        r.enabled = false;
    }
}

// OnHiddenExit 中还原
foreach (var r in mHiddenDisabledRenderers)
{
    if (r != null) r.enabled = true;
}
mHiddenDisabledRenderers.Clear();
```

注意点：
- 用 `GetComponentsInChildren<Renderer>(includeInactive: false)`：只处理当前 active 的 Renderer，避免把 Inspector 里本来就关掉的意外打开。
- 用基类 `Renderer`：一次覆盖 `SpriteRenderer` / `MeshRenderer` / `SkinnedMeshRenderer` / `ParticleSystemRenderer` / `TrailRenderer` 等子类。
- 退出时检查 `r != null`：防止 Renderer 在 Hidden 期间被销毁。
- 不处理 UI（`Canvas` 下 Image / Text 等），项目角色当前没挂 UI；未来如挂上，用 `CanvasGroup.alpha = 0` 而非关 Renderer。

## 3. 待决策项

（无；P1 / P2 / P3 均已闭环为 D11 / D12 / D13~D14，见上表。）


## 4. ReturnToCheckPointByHurt 设计

### 4.1 接口拆分

```csharp
// CharaBase 基类（中性：无论状态均执行）
public virtual void ReturnToCheckPoint(SceneObjBase sceneObjBase)
{
    // 保持现有逻辑不变（PlayerBase.ReturnToCheckPoint 也保持现有重载）
}

// PlayerBase 新增（受伤型：IInvulnerableState 下免疫）
public virtual void ReturnToCheckPointByHurt(SceneObjBase sceneObjBase)
{
    if (IsInvulnerable) return;  // 用 SceneObjBase.IsInvulnerable 虚属性（D11）
    ReturnToCheckPoint(sceneObjBase);
}
```

### 4.2 语义分离

| 方法 | 调用方 | 语义 | Hidden 下行为 |
|------|--------|------|--------------|
| `ReturnToCheckPoint` | 调试命令 / 系统内部重置 | "中性——我决定回到检查点" | 不检查无敌状态 |
| `ReturnToCheckPointByHurt` | Trap / 未来机关 | "玩家被伤害性机关击中，传送回检查点" | 如果 IInvulnerableState，忽略 |

### 4.3 AIPlayer 覆写（D12 已确认：是）

取消 AIPlayer 当前对 `ReturnToCheckPoint` 的 override（中性版本沿用 PlayerBase 默认），把 StopMovement + 反馈逻辑挪到 `ReturnToCheckPointByHurt` 上：

```csharp
public override void ReturnToCheckPointByHurt(SceneObjBase sceneObj)
{
    StopMovement(stopActionSequence: true);
    base.ReturnToCheckPointByHurt(sceneObj);  // 内部走 IInvulnerableState 判定
    if (!IsInvulnerable)
    {
        var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);
        string sceneObjName = sceneObj.Name;
        int index = sceneObjs.IndexOf(sceneObj);
        this.SendFeedbackToAgent($"[返回检查点]你触碰到: {index}. {sceneObjName}。已被传送回最近的检查点。当前动作序列已中断。");
    }
}
```

注意：判定 `if (!IsInvulnerable)` 用的是 `SceneObjBase.IsInvulnerable` 虚属性（D11 决策），不直接 `mCurState is`。

## 5. 改动文件清单

| 文件 | 改动类型 | 具体改动 |
|------|---------|---------|
| 新建 `…/Gameplay/FSM/IInvulnerableState.cs` | 新增 | 标记接口（与 IUndetectableState / IImmovableState 同级） |
| 新建 `…/Gameplay/FSM/IInvulnerableState.cs.meta` | 新增 | 自动生成 |
| `…/SceneObj/Base/SceneObjBase.cs` | 修改 | 新增 `public virtual bool IsInvulnerable => mCurState is IInvulnerableState;`（与 IsUndetectable / IsImmovable 风格一致；考虑未来 Device / Trap / Cabinet 等物体也可能被设计成可破坏，统一在 SceneObjBase 暴露） |
| `…/SceneObj/Chara/Core/CharaBase.cs` | 修改 | 1) `DeadState` 加 `: IInvulnerableState`；2) `Die()` 加 `if (IsInvulnerable) return;`（复用 SceneObjBase.IsInvulnerable，不直接 `mCurState is`） |
| `…/SceneObj/Chara/Core/PlayerBase.cs` | 修改 | 1) `HiddenState` 加 `: IInvulnerableState`；2) 新增 `private RigidbodyConstraints2D mHiddenSavedConstraints;`、`private List<Renderer> mHiddenDisabledRenderers = new List<Renderer>();`；3) `OnHiddenEnter` 改为「保存 constraints → FreezeAll → 关闭所有 active 的 Renderer 并记录列表」；4) `OnHiddenExit` 改为「还原 constraints → 按列表还原 Renderer.enabled」；5) 新增 `public virtual void ReturnToCheckPointByHurt(SceneObjBase sceneObj)`，内部判定 `if (IsInvulnerable) return;` 再调 `ReturnToCheckPoint(sceneObj)` |
| `…/SceneObj/Device/Cabinet.cs` | 修改 | 删除 Interact 方法中的 `rb.velocity = Vector2.zero; rb.angularVelocity = 0f;` 两行（进/出柜子两处共四行） |
| `…/SceneObj/Device/Trap.cs` | 修改 | `player.ReturnToCheckPoint(this)` → `player.ReturnToCheckPointByHurt(this)` |
| `…/SceneObj/Chara/AIPlayer.cs` | 修改 | 1) 取消现有 `override ReturnToCheckPoint(SceneObjBase)`（让中性的 ReturnToCheckPoint 走 PlayerBase 默认）；2) 新增 `public override void ReturnToCheckPointByHurt(SceneObjBase sceneObj)`，把原 ReturnToCheckPoint override 里的 StopMovement+SendFeedbackToAgent 逻辑挪到这里 |

**不修改的文件**（伤害源全留在原地）：
- `Laser.cs` — 调用 `player.Die()`，Die() 入口已拦截
- `Abyss.cs` — 同上
- `EnemyBase.cs` — 同上
- `HumanPlayer.cs` — 不动

## 6. 测试用例矩阵

### 6.1 位置冻结

| ID | 用例 | 步骤 | 期望 |
|----|------|------|------|
| F1 | 进入柜子后重力不再生效 | 走到柜子→Interact 进入 Hidden | 玩家停留在 mEnterAnchor，FixedUpdate N 帧后 position.y 不变（0.01 容差） |
| F2 | Hidden 下被刚体撞击不移动 | 在柜子位置放一个移动 Rigidbody2D 主动撞击玩家 | 玩家 position 不变 |
| F3 | Hidden 下被移动平台携带不变位 | 若 mEnterAnchor 在 MovingPlatform 上 | 玩家 position 不随平台移动（FreezeAll 锁 worldspace） |
| F4 | 退出柜子后重力恢复 | 空中放 mExitAnchor→Interact 退出 | 玩家瞬移到 mExitAnchor 后开始下落 |
| F5 | 退出后 constraints 还原成初始值 | 进入前 `constraints == FreezeRotation`→进入→退出 | 退出后 `constraints == FreezeRotation` |
| F6 | 连续进出 N 次 constraints 不被错误累积 | 进→出→进→出 ×3 | 每次退出后 constraints 都是初始值 |

### 6.2 Hidden 无敌

| ID | 用例 | 步骤 | 期望 |
|----|------|------|------|
| F7 | Hidden 下被敌人攻击 Trigger 命中 | 进入 Hidden 后，EnemyBase.mAttackZone 接触玩家 | player.Die() 被 CharaBase.Die 入口拦截，状态不切到 Dead |
| F8 | Hidden 下被 Laser 命中 | Laser.OnTriggerEnter2D 检测到玩家 | 同上，不死亡 |
| F9 | Hidden 下触碰 Abyss | Abyss.OnTriggerEnter2D | 同上，不死亡 |

### 6.3 ReturnToCheckPointByHurt 免疫

| ID | 用例 | 步骤 | 期望 |
|----|------|------|------|
| F10 | Hidden 下触发 Trap | 进入 Hidden 后触碰 Trap | ReturnToCheckPointByHurt 被 IInvulnerableState 拦截，玩家位置不变 |
| F11 | 非 Hidden 下触发 Trap | Idle 状态下触碰 Trap | ReturnToCheckPointByHurt 正常执行，玩家被传送回检查点 |

### 6.4 渲染消失（D13）

| ID | 用例 | 步骤 | 期望 |
|----|------|------|------|
| F17 | 进入 Hidden 后玩家及子节点 Renderer 全部不可见 | 进入柜子触发 OnHiddenEnter | 玩家及子节点上所有原本 enabled 的 `Renderer`（SpriteRenderer / 任意子类）都被置为 enabled=false，相机视野里看不见玩家 |
| F18 | 退出 Hidden 后玩家渲染恢复 | 从柜子出来触发 OnHiddenExit | 进入前 enabled 的 Renderer 全部恢复 enabled=true，玩家重新可见 |
| F19 | 进入前已 disable 的 Renderer 退出后保持 disabled | 编辑器里把玩家某个子 Renderer 关掉→进入→退出 | 该 Renderer 仍为 disabled（不被误开启） |
| F20 | 连续进出 N 次渲染状态不累积 | 进→出→进→出 ×3 | 每次退出后玩家可见性、各 Renderer.enabled 与初始一致 |

### 6.5 已有行为保留

| ID | 用例 | 步骤 | 期望 |
|----|------|------|------|
| F12 | Hidden 下 HumanPlayer 移动仍被屏蔽（v0.21.7 §6.4） | 按 Horizontal | 玩家不移动 |
| F13 | Hidden 下 AIPlayer Move 仍返回失败 | 调 move_to | 工具失败提示 |
| F14 | Hidden 下 AIPlayer InteractAction 仍可执行 | 调 do_interact | 动作正常完成，不强切 Idle |
| F15 | Dead 下 Die() 不会重复触发 | 已 Dead 再被机关触发 Die() | Die 入口被 IInvulnerableState 拦截（DeadState 实现了该接口） |
| F16 | Dead 下 ReturnToCheckPointByHurt | 已 Dead 被 Trap 触发 | 不回检查点（保险，逻辑上 Dead 后不会触发 OnTriggerEnter） |

## 7. 风险与回滚

### 7.1 已知风险

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|---------|
| FreezeAll 在特定引擎效果下与动画/IK 冲突（玩家被锁时试图播放位移动画） | 低 | 视觉抖动 | 当前项目无 IK 系统；Hidden 下玩家隐藏渲染（未来可能），移动动画不播 |
| `OnHiddenExit` 还原 constraints 时未能考虑中间被其它系统改过值 | 低 | constraints 异常 | 进入时取当时值并保存；OnHiddenExit 还原时用该保存值（不是硬编码） |
| AIPlayer 的 ReturnToCheckPointByHurt 覆写与 base 逻辑不一致 | 中 | 反馈错乱 | 在实现记录中单独标注此测试 |

### 7.2 回滚方案

如果 fix 引入问题：
1. 还原 `PlayerBase.cs` 的 OnHiddenEnter/OnHiddenExit 到 v0.21.7 初始版本（只 velocity=0）；
2. 还原 `CharaBase.cs` 去掉 Die() 入口判定；
3. 删除 `IInvulnerableState.cs` + `.meta`；
4. 还原 `Trap.cs` 到 `ReturnToCheckPoint`；
5. 还原 `AIPlayer.cs`（如果 P2 选 A）；
6. Cabinet.cs 的 velocity=0 代码还原（如果删除后发现问题）。

## 8. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-25 | 按本方案完成实现：新增 `IInvulnerableState.cs(.meta)`；`SceneObjBase.IsInvulnerable` 虚属性；`CharaBase.DeadState : IInvulnerableState` + `Die()` 入口 `if (IsInvulnerable) return;` 拦截；`PlayerBase.HiddenState : IInvulnerableState` + `OnHiddenEnter`（保存 constraints → FreezeAll → 关闭所有 active Renderer 并记录）+ `OnHiddenExit`（还原 constraints + 按列表还原 Renderer.enabled）+ 新增 `ReturnToCheckPointByHurt`；`Cabinet.Interact` 删除进/出柜子两处共四行 `rb.velocity = Vector2.zero; rb.angularVelocity = 0f;`；`Trap.OnTriggerEnter2D` 改用 `ReturnToCheckPointByHurt`；`AIPlayer` 取消原 `ReturnToCheckPoint` override（沿用 PlayerBase 默认），把 `StopMovement(true) + SendFeedbackToAgent` 逻辑挪到 `ReturnToCheckPointByHurt` override，并在 `IsInvulnerable` 时直接 return 不发反馈。所有改动文件通过 Unity C# lint（无报错）。需 Unity 联调验证 F1~F20 用例。 |

---

*本文档由 Cursor Agent 根据设计讨论生成；**你确认后** Agent 方可按本方案修改代码。*