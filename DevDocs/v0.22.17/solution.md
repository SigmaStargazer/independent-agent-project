# 技术方案 - v0.22.17 SceneObjAnimator 进阶：单状态内多动画流转

> **状态**：待确认
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-04

---

## 1. 方案概述

在 v0.22.16 的 `SceneObjAnimator` 基础上，扩展两项能力：

1. **转换边动画**：`HandleStateChanged` 用 `(oldState, newState)` 查转换映射表，命中则播过渡动画入口
2. **物理参数透传**：每帧向 Animator 写 `velY` / `grounded` / `speed`，供 Animator 自管跳跃等内部阶段

核心原则不变：FSM 状态间的切换由组件 `CrossFade` 驱动；单个 FSM 状态的动画内部流转由 Animator 自管。本期只加能力，不改 FSM。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | - | 无 |
| Unity | `SceneObjAnimator.cs` | 扩展（新增转换映射 + 物理参数透传） |
| Unity | 各 Animator Controller 资产 | 配置转换边动画 / 跳跃阶段（美术/关卡） |
| 协议 | 无 | 无 |

**明确不改**：任何 FSM 类、v0.22.16 现有配置字段语义。

## 3. 详细设计

### 3.1 新增字段

在 v0.22.16 已有字段基础上新增：

```csharp
// === 转换边动画 ===
[SerializeField] private List<TransitionMapping> _transitions = new();

// === 物理参数透传 ===
[SerializeField] private string _velYParam = "";        // 留空则不写
[SerializeField] private string _groundedParam = "";    // 留空则不写
[SerializeField] private string _speedParam = "";       // 留空则不写
[SerializeField] private Collider2D _groundCollider;    // grounded 判定用
[SerializeField] private LayerMask _groundLayerMask = 0;
[SerializeField] private float _maxSpeed = 5f;          // speed 归一化分母
```

### 3.2 新增数据结构

```csharp
[Serializable]
public class TransitionMapping
{
    public string fromState;      // 旧 FSM 状态名，如 "Idle"
    public string toState;        // 新 FSM 状态名，如 "Move"
    public string animState;      // 过渡动画入口名，如 "Move_Start"
    public bool crossFade = true; // 是否 CrossFade（默认过渡）
}
```

运行时建 `(fromState + "->" + toState)` 为 key 的字典。

### 3.3 HandleStateChanged 增强

```csharp
private void HandleStateChanged(SceneObjBase obj, string oldState, string newState)
{
    // 1. 先查转换映射 (oldState, newState)
    string key = oldState + "->" + newState;
    if (_transitionMap.TryGetValue(key, out var t))
    {
        // 命中：播放过渡动画入口，之后由 Animator 内部 exitTime 转到目标 Loop
        PlayAnim(t.animState, t.crossFade);
        return;
    }
    // 2. 未命中：v0.22.16 行为不变
    PlayState(newState);
}
```

**关键点**：
- 过渡动画（如 `Move_Start`）播完后，由 **Animator 内部 Transition**（`exitTime` 或 `HasExitTime`）自动跳到目标 Loop（如 `Move_Loop`）。组件不再介入。
- 若过渡动画中途 FSM 又切状态，组件会再次 `HandleStateChanged`，直接 `CrossFade` 到新状态，打断过渡动画。这是期望行为（FSM 权威优先）。

### 3.4 物理参数透传

在 `Update` 中（与 v0.22.16 的 `dirX` 写入合并）：

```csharp
private void Update()
{
    if (_animator == null) return;

    // 朝向（v0.22.16 已有）
    if (!string.IsNullOrEmpty(_facingParam) && _target is CharaBase chara)
        _animator.SetFloat(_facingParam, chara.IsRight ? 1f : -1f);

    // 物理参数：只对有 Rigidbody2D 的对象写
    if (_target is CharaBase c && c.Rigidbody2D != null)
    {
        var vel = c.Rigidbody2D.velocity;
        if (!string.IsNullOrEmpty(_velYParam))
            _animator.SetFloat(_velYParam, vel.y);
        if (!string.IsNullOrEmpty(_speedParam))
            _animator.SetFloat(_speedParam, Mathf.Min(Mathf.Abs(vel.x) / _maxSpeed, 1f));
    }

    // grounded
    if (!string.IsNullOrEmpty(_groundedParam) && _groundCollider != null)
        _animator.SetBool(_groundedParam, _groundCollider.IsTouchingLayers(_groundLayerMask));
}
```

**注意**：`CharaBase.Rigidbody2D` 当前是 `protected`，需确认是否暴露 public 访问器。若不便暴露，组件可自行 `GetComponent<Rigidbody2D>()` 缓存。倾向后者（组件自洽，不改 CharaBase）。

### 3.5 Animator 配置规范（给美术/关卡）

#### Idle 随机插播

```
Idle_Base (Loop)
  └─Transition(exitTime=5~10s, 随机)─> Idle_Var1 (No Loop)
  └─Transition(exitTime=5~10s, 随机)─> Idle_Var2
Idle_Var1/2 ─Transition(exitTime=1.0)─> Idle_Base
```
组件映射：FSM `"Idle"` -> `"Idle_Base"`。无需转换映射。

#### 起跑/刹车

```
Move_Start (No Loop) ─Transition(exitTime)─> Move_Loop (Loop)
Move_Brake (No Loop) ─Transition(exitTime)─> Idle_Base (Loop)
```
组件转换映射：
- `(Idle, Move)` -> `"Move_Start"`
- `(Move, Idle)` -> `"Move_Brake"`

组件状态映射：FSM `"Move"` -> `"Move_Loop"`（直接进 Move 时用，如从 Chase 回 Move）。

Animator 内部 `Move_Start -> Move_Loop` 的 Transition 用 `HasExitTime` + `exitTime=1.0`（播完整段起跑再转）。

#### 跳跃阶段

```
Jump_Base (入口, 可空 State)
  grounded=true & velY>0  ─Transition─> Jump_Launch (No Loop)
  Jump_Launch ─Transition(exitTime)─> Jump_Rise (Loop)
  grounded=false & velY>0 ─Transition─> Jump_Rise
  velY < 0                ─Transition─> Jump_Fall (Loop)
  grounded: false->true   ─Transition─> Jump_Land (No Loop)
  Jump_Land ─Transition(exitTime)─> Idle_Base 或 Jump_Base
```
组件状态映射：FSM `"Jump"` -> `"Jump_Base"`。
组件物理参数：`velYParam="velY"`、`groundedParam="grounded"`。

**Jump_Base 可以只是一个空 State 或 Entry**，进入后立刻由 grounded/velY 条件分流到 Launch/Rise/Fall。也可以直接映射 FSM `"Jump"` -> `"Jump_Launch"`（如果进入 Jump 时一定在地上起跳）。

### 3.6 与 v0.22.16 的兼容性

- 不填 `_transitions`：`HandleStateChanged` 走原逻辑（直接 `PlayState(newState)`）
- 不填 `_velYParam` / `_groundedParam` / `_speedParam`：不写物理参数
- 不填 `_groundCollider`：不写 grounded
- v0.22.16 的 `_mappings` / `_crossFadeByDefault` / `_facingParam` 语义不变

**v0.22.16 已配置的 Prefab 零改动即可继续工作。**

## 4. 实现步骤

1. 扩展 `SceneObjAnimator.cs`：新增 `TransitionMapping` + `_transitions` + 物理参数字段
2. `HandleStateChanged` 增加转换映射查询分支
3. `Update` 增加物理参数写入
4. 自测：依赖 Unity 引擎运行时，需联调
5. 接入验证：Idle 随机插播、起跑/刹车、跳跃阶段

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `CharaBase.Rigidbody2D` 是 protected，组件访问不到 | 组件自行 `GetComponent<Rigidbody2D>()` 缓存，不改 CharaBase |
| 转换映射的过渡动画被打断后 Animator 卡在过渡态 | 组件下次 `CrossFade` 会强制跳走，不会卡；Animator 内部 Transition 不依赖组件 |
| `grounded` 误判（LayerMask 配错） | `groundLayerMask` 留 0 时不写 grounded；配错时 Animator 会表现出错误阶段，易发现 |
| `speed` 归一化分母不对 | `_maxSpeed` 可配；默认 5f 与 `CharaBase.moveSpeed` 默认值一致 |
| v0.22.16 配置不兼容 | 所有新字段留空即不生效，零改动兼容 |

**回退**：删除新增字段 + 还原 `HandleStateChanged` / `Update` 到 v0.22.16 即可。

## 6. 测试用例矩阵

| 测试目标 | 前置条件 | 输入 | 期望输出 | 覆盖风险 |
|----------|----------|------|----------|----------|
| 转换边-起跑 | 转换映射 `(Idle,Move)->Move_Start` | `Idle->Move` | 播 Move_Start，播完转 Move_Loop | 转换映射 |
| 转换边-刹车 | 转换映射 `(Move,Idle)->Move_Brake` | `Move->Idle` | 播 Move_Brake，播完转 Idle_Base | 转换映射 |
| 转换未命中 | 无转换映射 | `Idle->Move` | 直接 CrossFade Move_Loop | 兼容性 |
| 起跑被打断 | 起跑播放中 | FSM 切 Chase | 立即 CrossFade 到 Chase，打断起跑 | FSM 权威 |
| velY 透传 | 配 velYParam | 跳跃 | velY 正负切换 | 物理参数 |
| grounded 透传 | 配 groundedParam + Collider + LayerMask | 跳跃落地 | false->true | 物理参数 |
| speed 透传 | 配 speedParam | 移动/停止 | 0~1 变化 | 物理参数 |
| Device 无物理参数 | Device 无 Rigidbody2D | - | 不写物理参数不报错 | 容错 |
| v0.22.16 兼容 | 旧配置不填新字段 | 切状态 | 行为与 v0.22.16 一致 | 向后兼容 |

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| | |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
