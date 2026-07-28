# PRD - v0.22.14 BrokenGlass 半径手柄可视化配置

> **状态**：已确认
> **对应需求**：`requirements/BrokenGlass半径手柄可视化配置.md`
> **最后更新**：2026-07-28

---

## 1. 背景与目标

`BrokenGlass.cs`（碎玻璃装置）定义了被踩踏时的声音传播半径 `mAttractRadius`（`[SerializeField] private float`，默认 5f），并通过 `Gizmos.DrawWireSphere` 在 Scene 视图绘制了圆形线框。

现状问题：半径只能在 Inspector 里改数值，不够直观；Scene 视图虽有圆形线框，但不能直接拖动调整大小，设计师需要反复在 Inspector 数值与 Scene 视图效果间切换试错。

本次目标：让设计师在 Scene 视图中**直接拖动手柄改变圆形响声范围的半径**，所见即所得，与 v0.22.13 相机范围矩形的体验一致。

## 2. 范围

### 2.1 本期包含

- 为 `BrokenGlass` 新增 Scene 视图可拖拽半径手柄，拖动实时改变 `mAttractRadius`。
- 手柄实现遵循 v0.22.13 验证过的模式（`[CustomEditor] + OnSceneGUI`），避免"拖动导致取消选中"的坑。
- 拖动后正确序列化保存（场景保存后生效），支持 Undo/Redo。
- 圆心以 `BrokenGlass.transform.position` 为准。

### 2.2 本期不包含

- 不改造响声广播逻辑本身（`OnTriggerEnter2D`、冷却 `mCooldownSeconds`、`EnemyAnomalyEvent` 上送等保持不变）。
- 不涉及其他 Device 子类、不涉及协议、不涉及 Python/Agent。
- 不做运行时（Game 视图/打包版）的手柄显示--手柄仅 Scene 视图编辑态可见（与 v0.22.13 一致）。
- 不改变 `OnDrawGizmos` 现有圆形线框的绘制（保留作为非选中时也可见的参考线框）。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 关卡设计师 | 在 Scene 视图中调整某块碎玻璃的响声范围 | 选中该碎玻璃，拖动圆周手柄即可直观改变半径 |
| 关卡设计师 | 不同碎玻璃需要不同响声范围（如大房间 vs 走廊） | 每块碎玻璃可独立配置各自半径 |
| 关卡设计师 | 拖错想撤销 | 支持 Ctrl+Z 撤销半径调整 |

## 4. 功能需求

### 4.1 半径手柄

- 选中挂有 `BrokenGlass` 的对象时，Scene 视图中出现可拖拽的半径手柄。
- 手柄以 `transform.position` 为圆心，以 `mAttractRadius` 为当前半径。
- 拖动手柄时半径实时更新，圆周线框随之变化。
- 手柄交互在 `OnSceneGUI` 事件管线内实现，拖动不会导致对象被取消选中。

### 4.2 序列化与撤销

- 拖动产生的半径变更需正确序列化（场景保存后生效）。
- 支持 Undo/Redo（Ctrl+Z / Ctrl+Y）。

### 4.3 既有线框保留

- `BrokenGlass.OnDrawGizmos` 现有的 `DrawWireSphere` 线框保留不动（非选中时也可见，作为参考）。
- 手柄与线框颜色可区分，避免视觉混淆。

## 5. 非功能需求

- **性能**：手柄仅在编辑态、对象被选中时绘制，无运行时开销。
- **兼容性**：仅依赖 Unity 标准 Editor API（`Handles.RadiusHandle` 等），不引入新依赖。
- **编码**：C# 文件 UTF-8 无 BOM（遵循项目编码基线）。

## 6. 验收标准

- [ ] 选中 `BrokenGlass` 对象后，Scene 视图出现可拖拽的半径手柄。
- [ ] 拖动手柄可实时改变 `mAttractRadius`，圆周线框同步变化。
- [ ] 拖动过程中对象不会被取消选中。
- [ ] 拖动后保存场景，重开场景后半径为拖动后的值。
- [ ] 支持 Ctrl+Z 撤销半径调整。
- [ ] 未选中时仍能看到既有圆形线框（`OnDrawGizmos` 行为不变）。
- [ ] 响声广播逻辑（`OnTriggerEnter2D`、冷却、事件上送）未受影响。

## 7. 待确认问题

均已确认（2026-07-28）：

1. **Gizmo 颜色**：颜色改成和 `CameraController` 一样可配置，新增 `mGizmoColor` 字段，默认绿色；并修正 Tooltip 颜色文案不一致问题。
2. **手柄颜色**：固定白色，不暴露为可配置字段，与既有绿色线框视觉区分。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
