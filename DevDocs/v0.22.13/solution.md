# 技术方案 - v0.22.13 相机移动范围限制

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-07-27

---

## 1. 方案概述

在 `CameraController` 的 `LateUpdate` 末尾，基于正交相机的视口半宽/半高对 `SmoothDamp` 后的相机中心位置做 clamp，使视口四边不超出一个世界坐标矩形；该矩形通过 `CameraController` 上新增的序列化字段定义，并用 `OnDrawGizmos` + `OnSceneGUI` 在 Scene 视图中绘制线框与拖拽手柄，实现所见即所得配置。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | — | 无 |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/CameraController.cs` | 修改：新增边界字段、clamp 逻辑、Gizmo/手柄绘制 |
| 协议 | `Tools/message.proto` | 无 |

纯 Unity 侧改造，不涉及 Python、协议、Agent 工具链路。可在不启动 Python 服务、不联调 Agent 的情况下独立自测。

## 3. 详细设计

### 3.1 数据与协议

无协议变更。仅在 `CameraController` 内部新增序列化字段（Unity Inspector / 场景文件存储）。

### 3.2 Python（Brain）

无变更。

### 3.3 Unity（Environment）

#### 3.3.1 边界配置载体选型

| 方案 | 说明 | 取舍 |
|------|------|------|
| **A. 内建在 `CameraController`**（推荐） | 直接在 `CameraController` 上加序列化字段 `bool boundsEnabled`、`Vector2 boundsCenter`、`Vector2 boundsSize`，Gizmo/手柄由本组件绘制 | 配置与相机同对象，挂载即用，无需额外 GameObject；缺点是相机对象职责略增 |
| B. 独立 `CameraBounds` 组件 | 新建组件挂到空 GameObject 上，`CameraController` 引用它 | 边界对象可复用、可单独移动；缺点是多一个对象和引用依赖，且本需求无复用场景 |

**采用方案 A**：边界本质是相机视口的约束，与相机强绑定，内建最直接，避免引用管理开销。待 PRD 第 7 节问题 1 确认后定稿。

#### 3.3.2 字段设计

```csharp
[Header("移动范围限制")]
[SerializeField] private bool boundsEnabled = false;
[SerializeField] private Vector2 boundsCenter = Vector2.zero;
[SerializeField] private Vector2 boundsSize = new Vector2(20f, 10f);
[SerializeField] private Color boundsGizmoColor = new Color(1f, 0.5f, 0f, 0.8f);
```

- `boundsEnabled`：开关，默认关，保证未配置时行为退化为现状（满足 PRD 4.3）。
- `boundsCenter` + `boundsSize`：矩形中心 + 尺寸，比四元 (xmin/xmax/ymin/ymax) 更适合 Gizmo 手柄拖拽（缩放手柄直接改 size，移动手柄直接改 center）。
- 派生量：`minX = center.x - size.x/2`，`maxX = center.x + size.x/2`，y 同理。
- 运行时私有字段 `float mBaseDepth`：`Start` 时缓存 `transform.position.z`，作为相机深度基准（非序列化，运行时重置即重读）。

#### 3.3.3 clamp 算法（正交相机）

`LateUpdate` 中 `SmoothDamp` 之后、赋值 `transform.position` 之前插入：

```csharp
if (boundsEnabled && boundsSize.x > 0f && boundsSize.y > 0f)
{
    float halfH = cam.orthographicSize;                  // 半高
    float halfW = halfH * cam.aspect;                    // 半宽 = 半高 * 宽高比
    float minX = boundsCenter.x - boundsSize.x * 0.5f + halfW;
    float maxX = boundsCenter.x + boundsSize.x * 0.5f - halfW;
    float minY = boundsCenter.y - boundsSize.y * 0.5f + halfH;
    float maxY = boundsCenter.y + boundsSize.y * 0.5f - halfH;

    // 矩形比视口还小（无解）时，退化为夹到中心，并打一次 Warning
    if (minX > maxX) { minX = maxX = boundsCenter.x; WarnBoundsTooSmall(); }
    if (minY > maxY) { minY = maxY = boundsCenter.y; WarnBoundsTooSmall(); }

    position.x = Mathf.Clamp(position.x, minX, maxX);
    position.y = Mathf.Clamp(position.y, minY, maxY);
}
```

要点：
- `cam.aspect` 运行时随 Game 窗口/分辨率变化，每帧动态取值，满足 PRD 4.2 宽高比变化正确限制。
- 用组件持有的 `Camera` 引用（`Start` 里 `GetComponent<Camera>()`），避免每帧 `GetComponent`。
- z 不参与 clamp，也不参与 `SmoothDamp` 平滑：`Start` 时缓存 `transform.position.z` 为 `mBaseDepth`，`LateUpdate` 中令 `mTargetPos.z = mBaseDepth`、且 `SmoothDamp` 仅对 x/y 平滑（或平滑后用 `mBaseDepth` 覆盖 z），保证运行时深度恒为编辑态设置的初始值，便于在 Scene 中调整相机深度后保持。原硬编码 `mTargetPos.z = -10` 删除。
- "视口最左侧 = 相机中心 x − halfW"，clamp 后 `相机中心 x − halfW ≥ 矩形左`，即视口左边不超矩形左，满足 PRD 4.2 语义。

#### 3.3.4 Gizmo 绘制（`OnDrawGizmos`）

- 始终（编辑态选中或非选中）用 `boundsGizmoColor` 绘制矩形线框，让设计师在 Scene 视图随时可见。
- `Gizmos.DrawWireCube(boundsCenter, boundsSize)` 即可，简单可靠。
- 仅当 `boundsEnabled` 时绘制，避免无意义 Gizmo。

#### 3.3.5 Scene 视图拖拽手柄（`OnDrawGizmos` 内或 `OnSceneGUI`）

> 说明：本项目未使用自定义 Editor 目录的习惯，采用 `OnDrawGizmos` + `UnityEditor.Handles` 在组件内直接实现编辑手柄，无需新增 Editor 脚本。`Handles` API 仅在编辑器编译可用，需用 `#if UNITY_EDITOR` 包裹。

- 移动手柄：在 `boundsCenter` 处放一个 `Handles.PositionHandle`，拖动改 `boundsCenter`。
- 缩放手柄：在矩形四角放 `Handles.FreeMoveHandle`（或 `Handles.ScaleValueHandle`），拖动改 `boundsSize`。四角手柄各自只改对应方向的 size，保持对角固定更直观（即拖右下角时左上角不动）。
- 手柄仅 `boundsEnabled` 时显示。
- 拖拽后 `EditorUtility.SetDirty` 以确保序列化保存。

> 线框与手柄仅 Scene 视图可见（`OnDrawGizmos` / `Handles` 默认行为）；Game 视图与打包发布版不绘制，玩家不可见，无需额外编译宏隔离运行时绘制（`#if UNITY_EDITOR` 仅用于 `Handles`/`EditorUtility` 的编辑器 API 编译隔离）。

#### 3.3.6 最小尺寸保护与警告

- clamp 前判断 `boundsSize` 各分量 > 0；为 0 或负则跳过 clamp（退化为不限）。
- 当矩形比视口小（`minX > maxX` 等）时，把该轴夹到中心并 `Debug.LogWarning` 一次（用 `bool warned` 去重，避免刷屏）。

### 3.4 工具 / ActionSequence（如适用）

不涉及。本需求与 Agent 工具链路无关。

## 4. 实现步骤

1. 读取 `CameraController.cs`，在 `Start` 中缓存 `Camera` 引用与 `mBaseDepth = transform.position.z`。
2. 新增边界序列化字段（§3.3.2）。
3. 在 `LateUpdate` 的 `SmoothDamp` 之后、`transform.position = position` 之前插入 clamp 逻辑（§3.3.3），并清理旧的注释 clamp 代码。
4. 新增 `OnDrawGizmos`：绘制矩形线框（§3.3.4）。
5. 用 `#if UNITY_EDITOR` 新增 `OnSceneGUI`/手柄绘制（§3.3.5），实现拖拽配置。
6. 自测（见第 6 节），通过后提交验收。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `Handles`/`OnSceneGUI` 用法不当导致编辑态异常 | 全部用 `#if UNITY_EDITOR` 包裹；手柄仅在 `boundsEnabled` 时显示；自测覆盖编辑态拖拽 |
| 矩形比视口小导致 clamp 无解 | §3.3.6 退化为夹到中心 + Warning |
| 改造影响现有跟随手感 | clamp 放在 `SmoothDamp` 之后，不改偏移量与 `smoothTime`；自测对比改造前后跟随曲线 |
| 旧场景未配置边界导致行为变化 | `boundsEnabled` 默认 false，未配置时完全不变 |
| 回退 | 改动集中在单个文件，回退即恢复 `CameraController.cs` 原状（git revert 该文件） |

## 6. 测试建议

本功能纯 Unity 侧，可在 Unity 编辑器内独立自测，无需 Python 联调。自测用例矩阵如下：

| 用例 | 前置条件 | 操作 | 期望 |
|------|----------|------|------|
| T1 边界关闭时行为不变 | `boundsEnabled=false` | 运行场景，玩家移动到边缘 | 相机随玩家平滑跟随，可穿帮（与改造前一致） |
| T2 边界开启限制左侧 | 配置矩形，玩家在矩形内左侧 | 玩家走到矩形左边界 | 相机视口左边停在矩形左边界，不超出 |
| T3 边界开启限制右侧 | 同上 | 玩家走到矩形右边界 | 相机视口右边停在矩形右边界 |
| T4 上下边界 | 同上 | 玩家走到矩形上/下边界 | 视口上/下边停在矩形边界 |
| T5 宽高比变化 | `boundsEnabled=true`，Game 窗口 Free Aspect | 切换 16:9 → 4:3 | 相机仍被限制，无穿帮；切回 16:9 仍正确 |
| T6 Scene 拖拽配置 | 选中相机对象，Scene 视图 | 拖移动手柄改 center、拖缩放手柄改 size | 矩形跟随更新，Game 视图限制即时生效 |
| T7 Gizmo 可见性 | 选中相机 | 观察 Scene 视图 | 矩形线框按 `boundsGizmoColor` 可见 |
| T8 矩形过小 | `boundsSize` 设得比视口还小 | 运行 | 相机夹到 center，控制台打印一次 Warning，不报错 |
| T9 跟随手感回归 | `boundsEnabled=true` | 玩家在矩形中央往返移动 | 平滑跟随曲线与改造前一致（偏移、smoothTime 不变） |
| T10 相机深度保持 | 编辑态在 Scene 把相机 z 调为 -8（非 -10） | 运行场景，玩家移动 | 相机 z 恒为 -8，不被 `-10` 覆盖；clamp 仅作用于 x/y |

自测在 Unity 编辑器内完成 T1–T9 后再提交用户验收。需联调项：无（与 Agent/Python 无关）。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-07-27 | 完成 `CameraController.cs` 改造：新增 `boundsEnabled/boundsCenter/boundsSize/boundsGizmoColor` 序列化字段；`Start` 缓存 `Camera` 引用与 `mBaseDepth`；`LateUpdate` 在 `SmoothDamp` 后对 x/y 做 clamp（正交相机视口半宽半高动态计算），z 锁定为 `mBaseDepth`；矩形过小退化+Warning 去重；`OnDrawGizmos` 画线框（Scene 视图始终可见）；顺手补 `mPlayer` null 保护，清理无用 using。Unity 编辑器 API 签名已通过 Unity 2021.3 官方文档实测确认。 |
| 2026-07-27 | 修复拖拽手柄导致取消选中的问题：根因是 `OnDrawGizmosSelected` 里的 `Handles` 手柄不在 Scene 视图事件管线内，点击会穿透到选择系统。改为新建 `Assets/Editor/CameraControllerEditor.cs`，用 `[CustomEditor(typeof(CameraController))]` + `OnSceneGUI` 实现中心移动手柄+四角缩放手柄，通过 `SerializedProperty` 访问私有序列化字段并支持 Undo/Redo。`CameraController.cs` 删掉原 `#if UNITY_EDITOR` 的 `OnDrawGizmosSelected` 段，只保留 `OnDrawGizmos` 线框绘制。 |
| 2026-07-28 | 用户验收通过。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
