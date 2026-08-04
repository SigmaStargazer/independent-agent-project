# 技术方案 - v0.22.16 SceneObj 状态动画驱动组件

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-03

---

## 1. 方案概述

新增一个独立 Unity 组件 `SceneObjAnimator`，挂在带 Animator 的节点上，通过订阅 `SceneObjBase.OnStateChanged` 事件，把 FSM 状态切换翻译成 Animator 动画播放。组件只读状态、只驱动 Animator，不参与任何 FSM 逻辑，保持 FSM 单向权威。

核心思路：

- `SceneObjBase.ChangeState` 已经对外抛 `OnStateChanged(this, oldState, newState)`（见 `SceneObjBase.cs:137`），这是统一的事件源，Chara 与 Device 共用。
- 组件用 `[SerializeField]` 持有 `SceneObjBase` 引用 + `Animator` 引用，Animator 可在子节点。
- 状态名默认直接作为动画状态名，需要改名时通过 Inspector 映射表覆盖。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | - | 无 |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Base/SceneObjAnimator.cs` | 新增组件 |
| Unity | 各 SceneObj Prefab（HumanPlayer/EnemyBase/SignalLight/Safebox 等） | 挂载组件 + 拖引用（配置工作，非代码改动） |
| Unity | 对应 Animator Controller 资产 | 新建 State（美术/关卡工作） |
| 协议 | `Tools/message.proto` | 无 |

**明确不改**：`SceneObjBase.cs` / `CharaBase.cs` / `PlayerBase.cs` / `EnemyBase.cs` / `DeviceBase.cs` 及所有子类、所有 FSMStateBase 派生类。

## 3. 详细设计

### 3.1 组件位置

放在与 `SceneObjBase` 同级目录，表明它是 SceneObj 体系的表现层配套组件：

```
Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Base/
├── SceneObjBase.cs
├── SceneObjAnimator.cs        ← 新增
├── SceneObjManager.cs
├── InteractionZone.cs
└── ...
```

### 3.2 组件 API 设计

```csharp
namespace IndependentAgentProject
{
    /// <summary>
    /// 订阅 SceneObjBase.OnStateChanged，把 FSM 状态切换翻译成 Animator 动画播放。
    /// 只读状态、只驱动 Animator，不参与任何 FSM 逻辑。
    /// Chara 与 Device 共用，因为都走 SceneObjBase.ChangeState。
    /// </summary>
    public class SceneObjAnimator : MonoBehaviour
    {
        [SerializeField] private SceneObjBase _target;
        [SerializeField] private Animator _animator;
        [SerializeField] private float _crossFadeDuration = 0.1f;
        [SerializeField] private bool _crossFadeByDefault = false;   // Sprite 项目默认瞬切
        [SerializeField] private string _facingParam = "dirX";
        [SerializeField] private List<StateMapping> _mappings = new();

        // 运行时: 状态名 -> 映射项 查找表
        private Dictionary<string, StateMapping> _map;

        [Serializable]
        public class StateMapping
        {
            public string fsmState;        // FSM 状态名，如 "Idle"
            public string animState;       // Animator 状态名，空则同名
            public bool isOneShot;         // true=一次性(Dead/Stunned/Open), false=循环
            public bool crossFade;         // 是否过渡；以 _crossFadeByDefault 为初值，逐项覆盖
            public bool skipAnimation;     // true=该状态不驱动 Animator（如 Hidden/Follow）
        }
    }
}
```

**字段说明**：

| 字段 | 作用 | 默认 |
|------|------|------|
| `_target` | 订阅哪个 SceneObjBase 的状态。一般就是父节点或自身的 SceneObjBase | - |
| `_animator` | 驱动哪个 Animator。可在子节点（SpriteRenderer 所在节点） | - |
| `_crossFadeDuration` | 状态切换过渡时间（秒），仅在 `crossFade` 生效时使用 | 0.1 |
| `_crossFadeByDefault` | 未在映射表逐项配置时的默认过渡方式。Sprite 项目建议 false（瞬切，避免重影） | false |
| `_facingParam` | 朝向参数名，空则不写 | "dirX" |
| `_mappings` | 状态名映射表，留空则状态名=动画状态名 | 空 |

### 3.3 生命周期与驱动逻辑

```csharp
private void OnEnable()
{
    if (_target != null)
        _target.OnStateChanged += HandleStateChanged;
}

private void OnDisable()
{
    if (_target != null)
        _target.OnStateChanged -= HandleStateChanged;
}

private void Start()
{
    BuildMap();
    // 挂载后同步当前状态，避免不动
    if (_target != null)
        PlayState(_target.StateName, force: true);
}

private void Update()
{
    // 朝向参数：每帧写一次（混合树需要连续值）
    if (!string.IsNullOrEmpty(_facingParam) && _animator != null && _target is CharaBase chara)
    {
        _animator.SetFloat(_facingParam, chara.IsRight ? 1f : -1f);
    }
}

private void HandleStateChanged(SceneObjBase obj, string oldState, string newState)
{
    PlayState(newState, force: false);
}

private void PlayState(string fsmState, bool force)
{
    if (_animator == null) return;
    // 查映射
    string animState = fsmState;
    bool crossFade = _crossFadeByDefault;
    if (_map.TryGetValue(fsmState, out var m))
    {
        // 显式跳过：该状态不驱动 Animator（如 Hidden/Follow）
        if (m.skipAnimation) return;
        animState = string.IsNullOrEmpty(m.animState) ? fsmState : m.animState;
        crossFade = m.crossFade;
    }
    if (crossFade)
        _animator.CrossFade(animState, _crossFadeDuration);
    else
        _animator.Play(animState);
}
```

**关键点**：

1. **订阅事件而非轮询**：只在 `OnStateChanged` 触发时调 Animator，零额外开销。
2. **Start 同步**：组件可能在场景加载后挂载，此时对象已处于某状态（如 `Idle`），`Start` 时主动播一次，避免「挂了组件但不动」。
3. **朝向只对 CharaBase 写**：`_target is CharaBase` 判断；Device 不是 CharaBase，自动跳过，无需额外配置。
4. **未映射状态容错**：`_map` 没命中时用 `fsmState` 本身作为动画状态名。若 Animator Controller 里也没这个 State，Unity Animator 会打 Warning（不崩）。组件层不再额外校验，避免与 Animator 自身告警重复。
5. **显式跳过**：映射表里 `skipAnimation=true` 的状态（如 `Hidden`、EnemyBase 已移除的 `Follow`）直接 return，不调 Animator、不告警，与「漏建动画状态」区分开。

### 3.4 映射表设计

映射表用于三种场景：

1. **改名**：FSM 状态名与 Animator 状态名不一致。例如 FSM 叫 `GreenLight`，Animator 里叫 `Green`。
2. **标记一次性**：`isOneShot=true` 的状态播完不循环。本期组件层仅记录该标记，实际是否循环由 Animator Controller 里该 State 的 Clip 设置决定（组件不强控）。该字段为后续「一次性态播完通知」预留。
3. **显式跳过**：`skipAnimation=true` 的状态不驱动 Animator。用于 FSM 有该状态但不需要/不该有动画的场景（如 `Hidden` 进躲藏后 Renderer 被关、EnemyBase 已移除的 `Follow`）。

默认（映射表为空）行为：所有状态名=动画状态名，过渡方式取 `_crossFadeByDefault`（Sprite 项目建议 false 瞬切）。**这是最常用的零配置模式**——只要 Animator Controller 里建了与 FSM 同名的 State，挂上组件拖好引用就能工作。

**Sprite 项目与 crossFade**：逐帧 Sprite 动画用 `CrossFade` 会同时渲染两帧产生重影，因此 Sprite 状态默认应 `crossFade=false`（瞬切 `Play`）。少数单 Sprite 加缩放/旋转或特意设计的淡入淡出可逐项打开。组件级 `_crossFadeByDefault=false` 加映射表逐项 `crossFade=true` 即可满足。

### 3.5 Animator Controller 结构建议（给美术/关卡）

每个需要动画的 SceneObj Prefab 配一个 Animator Controller：

```
Animator Controller (HumanPlayer)
├── Idle      (Loop)
├── Move      (Loop)
├── Dead      (No Loop)
├── Hidden    (Loop, 可选；Hidden 时 Renderer 被关，见 §6)
└── 参数: dirX (Float, 供 Move 混合树左右)
```

```
Animator Controller (EnemyBase)
├── Idle / Move / Dead
├── Chase / Searching / Stunned / Alerted / Investigate / Inspect
└── 参数: dirX
```

```
Animator Controller (SignalLight)
├── RedLight / GreenLight   (均 Loop)
└── 无朝向参数
```

**关于 `Follow` 状态**：`CharaBase` 注册了 `Follow`，但其内部按距离分走/停三段，不触发 `ChangeState`，动画组件无法区分。本期决策：`Follow` 用 `skipAnimation=true` 显式跳过（或建一个粗粒度 Follow 动画），不处理其内部走/停表现。`Follow` 作为 FSM 状态粒度过粗的设计问题已记录到 `DevDocs/需求池/backlog.md` 条目 11，后续版本重构为移动策略后，跟随中的走/停改由 Idle/Move 表现状态驱动，动画组件天然支持，无需改动。`EnemyBase` 已 `mStates.Remove("Follow")`，其 Controller 无需建 Follow State。

状态间用 Transition + Conditions（或直接由组件 CrossFade 跳转）。推荐：**不建 Transition**，完全靠 `CrossFade(状态名)` 跳转，Controller 更干净。

**Animator 连线/参数约定**：

- **不需要连线**（State 间不画 Transition）
- **不需要触发值/切换值**（不建 Trigger/Bool/Int 参数做状态切换）
- 组件用 `Animator.CrossFade(stateName, duration)` / `Animator.Play(stateName)` 按状态名直接跳转，能在当前 Layer 内跳到任意 State，不依赖 Transition
- 唯一的参数是 `dirX`（Float），那是给角色**朝向/混合树**用的，不是状态切换用的；Device 不需要
- **一律平铺**：所有 State 建在 Layer 根层级，**不嵌套 Sub-State Machine**。`CrossFade`/`Play` 默认在当前 Layer 平铺层级找 State，嵌套需写全路径（如 `"Layer.Sub.Move"`），本期不处理

### 3.6 与现有代码的衔接点

| 现有代码 | 关系 |
|----------|------|
| `SceneObjBase.OnStateChanged` 事件（`SceneObjBase.cs:57`） | 组件订阅入口 |
| `SceneObjBase.ChangeState`（`SceneObjBase.cs:121-138`） | 事件触发点，不改 |
| `SceneObjBase.StateName`（`SceneObjBase.cs:56`） | `Start` 时同步初始状态 |
| `CharaBase.IsRight`（`CharaBase.cs:15`） | 朝向参数数据源 |
| `PlayerBase.OnHiddenEnter` 关闭 Renderer（`PlayerBase.cs:43-62`） | 见 §6 已知问题 |

## 4. 实现步骤

1. 新建 `SceneObjAnimator.cs`，实现组件（API 见 §3.2，逻辑见 §3.3）。
2. 自测：在空场景建一个 Cube + Animator + 一个测试用的 `SceneObjBase` 子类（或用现有 SignalLight），挂组件，手动 `ChangeState`，观察动画是否切换。
3. 接入 HumanPlayer：Prefab 挂组件，拖 `SceneObjBase`（自身）+ Animator（子节点 SpriteRenderer 所在节点），Animator Controller 建 Idle/Move/Dead 状态。
4. 接入 EnemyBase：同上，补 Chase/Alerted 等状态。
5. 接入一个 Device（SignalLight）：挂组件，建 Red/Green 状态，验证切换。
6. 朝向参数验证：HumanPlayer 左右移动，观察 `dirX` 与动画朝向。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| Animator 所在节点被 `Hidden` 关闭 Renderer 导致动画不可见 | 见 §6，本期接受无动画；如需可见，后续在 `PlayerBase.OnHiddenEnter` 排除 Animator 节点（改代码，需另起方案） |
| 状态名拼写不一致导致动画不播 | 组件打 Warning；映射表可修正；Animator 自身也会告警 |
| `CrossFade` 到不存在的状态名 | Unity Animator 打 Warning 不崩；组件层不额外拦截 |
| 朝向参数与混合树配置不匹配 | 参数名可配；非 Chara 自动跳过 |
| 组件挂载时机晚于对象首次 `ChangeState` | `Start` 时同步当前 `StateName` 覆盖此场景 |

**回退**：组件是纯新增，删除 `SceneObjAnimator.cs` + 从 Prefab 移除组件即可完全回退，无任何代码残留。

## 6. 已知问题：Hidden 状态动画不可见

`PlayerBase.OnHiddenEnter`（`PlayerBase.cs:43-62`）进入躲藏时会关闭**所有子节点 Renderer**：

```43:62:Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Chara/Core/PlayerBase.cs
        public virtual void OnHiddenEnter()
        {
            if (mRigidbody2D != null)
            {
                mHiddenSavedConstraints = mRigidbody2D.constraints;
                mRigidbody2D.constraints = RigidbodyConstraints2D.FreezeAll;
                mRigidbody2D.velocity = Vector2.zero;
                mRigidbody2D.angularVelocity = 0f;
            }

            mHiddenDisabledRenderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                if (r != null && r.enabled)
                {
                    mHiddenDisabledRenderers.Add(r);
                    r.enabled = false;
                }
            }
        }
```

若 Animator 挂在带 SpriteRenderer 的子节点上，该 Renderer 会被关掉，画面不可见，Hidden 动画播了也看不到。

**本期决策（已确认）**：接受 Hidden 时无可见动画，用 `skipAnimation` 显式跳过 Hidden 状态。理由：Hidden 语义就是「看不见」，玩家躲进柜子本就不该有可见动画。如未来需要可见，另起方案改 `PlayerBase`。

**后续若要改**：在 `PlayerBase.OnHiddenEnter` 的 Renderer 关闭循环里，跳过挂有 `SceneObjAnimator` 的节点（或加一个「不关闭」标记）。这属于改 `PlayerBase` 代码，需另起方案确认，不在本期范围。

## 7. 测试建议

### 7.1 自测（不依赖完整场景）

- 新建测试场景，放一个 GameObject 挂 `SignalLight`（已注册 GreenLight/RedLight 状态）+ 子节点挂 Animator + SpriteRenderer
- 挂 `SceneObjAnimator`，拖引用
- Animator Controller 建 `RedLight` / `GreenLight` 两个 State（可用任意 Clip 或空 State 加 Motion）
- 写测试脚本：按键调用 `ChangeState("GreenLight")` / `ChangeState("RedLight")`
- 验证：Animator 状态切换；映射表改名后切换到改名后的 State

### 7.2 联调验证

- HumanPlayer：Play 后左右移动，观察 Idle<->Move 切换 + 朝向
- EnemyBase：触发 Chase（进视野）、Stunned（背刺），观察动画
- SignalLight / Safebox：交互后观察状态切换动画

## 8. 测试用例矩阵

| 测试目标 | 前置条件 | 输入 | 期望输出 | 覆盖风险 |
|----------|----------|------|----------|----------|
| 状态切换驱动动画 | SignalLight + Animator + 组件，Controller 有 RedLight/GreenLight | `ChangeState("GreenLight")` | Animator 播 GreenLight | 核心链路 |
| 初始状态同步 | 对象已处于 Idle，后挂组件 | `Start` | 立即播 Idle | 挂载时机 |
| 映射表改名 | FSM=GreenLight，映射 animState=Green | `ChangeState("GreenLight")` | 播 Green | 改名配置 |
| 未映射状态容错 | Controller 无某状态 | `ChangeState("Unknown")` | Warning 不崩 | 容错 |
| 显式跳过 | 映射表 Hidden 的 `skipAnimation=true` | `ChangeState("Hidden")` | 不调 Animator、不告警 | skipAnimation |
| Sprite 瞬切无重影 | `_crossFadeByDefault=false` + Sprite 动画 | `ChangeState("Move")` | 瞬切无两帧重影 | crossFade 默认 |
| 逐项过渡覆盖 | 某状态映射 `crossFade=true` | 切入该状态 | CrossFade 过渡生效 | 逐项覆盖 |
| 朝向参数 | HumanPlayer + dirX | 左右移动 | dirX 正负切换 | 朝向 |
| Device 无朝向 | SignalLight + dirX | 切状态 | 不写 dirX，不报错 | 非角色跳过 |
| Hidden 不可见 | PlayerBase 进 Hidden（未配 skipAnimation） | 进柜子 | Renderer 关，动画不可见（接受） | 已知问题 |

---

## 9. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-04 | 完成 `SceneObjAnimator.cs` 组件实现（API 见 §3.2，逻辑见 §3.3）。无 linter 错误。组件依赖 Unity 引擎运行时（`Animator`/`MonoBehaviour`/`SceneObjBase`），无法纯命令行自测，需 Unity 编辑器联调验证。PRD §7 全部待确认问题已闭环。Follow 设计问题记录到 `DevDocs/需求池/backlog.md` 条目 11。 |
| 2026-08-04 | 用户 Unity 联调测试通过，验收通过。PRD/solution 状态置为「已实现」。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
