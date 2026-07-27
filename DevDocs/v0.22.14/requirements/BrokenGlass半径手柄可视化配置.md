# BrokenGlass 圆形响声范围手柄可视化配置

## 背景

`BrokenGlass.cs`（路径：`Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Device/BrokenGlass.cs`）定义了碎玻璃被踩踏时的声音传播半径 `mAttractRadius`（`[SerializeField] private float`，默认 5f），并通过 `Gizmos.DrawWireSphere` 在 Scene 视图绘制了红色线框。

现状：半径只能在 Inspector 里改数值，不够直观；Scene 视图虽有线框，但不能直接拖动调整大小。

## 需求

希望能像 v0.22.13 的相机范围矩形那样，**在 Scene 视图中通过拖动手柄直接改变圆形响声范围的半径**，所见即所得。

具体期望：

1. 选中挂有 `BrokenGlass` 的对象时，Scene 视图中的圆形线框可通过拖动手柄改变半径。
2. 拖动手柄时半径实时更新，且能正确序列化保存（场景保存后生效）。
3. 拖动手柄不应导致对象被取消选中（避免 v0.22.13 踩过的坑：手柄必须在 `OnSceneGUI` 事件管线内实现，而非 `OnDrawGizmosSelected`）。
4. 支持 Undo/Redo。
5. 圆心以 `BrokenGlass.transform.position` 为准（半径手柄沿圆周拖动）。

## 现状参考

- `mAttractRadius`：单个 float 字段，已有 `Gizmos.DrawWireSphere(transform.position, mAttractRadius)` 绘制。
- v0.22.13 已实现 `Assets/Editor/CameraControllerEditor.cs`，用 `[CustomEditor] + OnSceneGUI + SerializedProperty` 的模式可复用。
- Unity 提供 `Handles.RadiusHandle`（专门用于可拖拽半径的 API）或在圆周放 `Handles.FreeMoveHandle` 实现缩放，两种方式均可，方案选型留待 PRD/方案阶段确定。

## 范围

- 仅 `BrokenGlass` 的 `mAttractRadius`。
- 不涉及其他 Device、不涉及协议、不涉及 Python/Agent。
- 不改造响声广播逻辑本身（冷却、事件上送等保持不变）。
