# 技术方案 - v0.22.14 BrokenGlass 半径手柄可视化配置

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-07-28

---

## 1. 方案概述

为 `BrokenGlass` 的 `mAttractRadius` 增加 Scene 视图可拖拽半径手柄。手柄实现复用 v0.22.13 验证过的 `OnSceneGUI` 模式，核心 API 为 `Handles.RadiusHandle`（已通过 Unity 2021.3 官方文档实测确认）。

Editor 工程组织选定**方案 A**（每组件一个独立 Editor），理由见 §3.4：方案 B 的工具库按手柄类型预先抽象会违反开闭原则。同时规划 `Assets/Editor/` 子目录结构，避免脚本堆砌。详见 §3.5。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | - | 无 |
| Unity | `Assets/Editor/CustomInspectors/BrokenGlassEditor.cs` | 新增：半径手柄 Editor |
| Unity | `Assets/Editor/Bootstrap/BootstrapPlayMode.cs` | 移动：从根目录迁入 |
| Unity | `Assets/Editor/CustomInspectors/CameraControllerEditor.cs` | 移动：从根目录迁入 |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Device/BrokenGlass.cs` | 修改：新增 `mGizmoColor` 字段，`OnDrawGizmos` 用它替代硬编码绿色，修正 Tooltip 文案 |
| 协议 | `Tools/message.proto` | 无 |

纯 Unity 编辑器侧改造，不涉及 Python、协议、Agent 工具链路，无需联调。

## 3. 详细设计

### 3.1 数据与协议

无协议变更。`BrokenGlass.mAttractRadius` 字段保持不变（`[SerializeField] private float`，默认 5f）。

### 3.2 Python（Brain）

无变更。

### 3.3 架构选型：Editor 手柄的工程组织方式

> 背景：用户质疑"每加一个 gizmo 手柄就新加一个 XXEditor 脚本"是否合理。本节展开三种方案，待用户选定后定稿 §3.4 之后的实现细节。

#### 3.3.1 方案 A：维持现状，每组件一个 Editor

**思路**：每个带可视化手柄的组件对应一个独立的 `[CustomEditor]` 脚本，各自完整实现 `OnSceneGUI`。

**文件结构**：
```
Assets/Editor/
  CameraControllerEditor.cs      // v0.22.13 已有
  BrokenGlassEditor.cs           // 本期新增
  （未来）XxxDeviceEditor.cs     // 每加一个组件加一个文件
```

**代码形态**（以 BrokenGlassEditor 为例，完整自包含）：
```csharp
[CustomEditor(typeof(BrokenGlass))]
public class BrokenGlassEditor : UnityEditor.Editor
{
    private SerializedProperty m_AttractRadius;
    private void OnEnable() { m_AttractRadius = serializedObject.FindProperty("mAttractRadius"); }
    private void OnSceneGUI()
    {
        serializedObject.Update();
        BrokenGlass t = (BrokenGlass)target;
        EditorGUI.BeginChangeCheck();
        float newR = Handles.RadiusHandle(Quaternion.identity, t.transform.position, m_AttractRadius.floatValue);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(target, "调整碎玻璃响声半径");
            m_AttractRadius.floatValue = Mathf.Max(0f, newR);
        }
        serializedObject.ApplyModifiedProperties();
    }
}
```

**优点**：
- 最直接，完全符合 Unity 原生 `[CustomEditor]` 一对一的设计习惯。
- 每个 Editor 职责单一，互不影响，删除/回退某个组件的手柄只需删一个文件。
- 对新手友好，无需理解抽象层。

**缺点**：
- 脚本数量随可视化组件线性增长。
- 每个 Editor 都重复写 `SerializedObject.Update/ApplyModifiedProperties`、`EditorGUI.BeginChangeCheck/EndChangeCheck`、`Undo.RecordObject` 等样板代码。
- 手柄颜色配置、负值保护等横切逻辑各自实现，容易不一致。

**适用场景**：可视化手柄组件很少（2-3 个），且短期不会大量增加。

---

#### 3.3.2 方案 B：抽出手柄工具库 + 薄 Editor（推荐）

**思路**：把手柄绘制逻辑（含 Undo、SerializedProperty 读写、颜色配置、负值保护）封装成可复用的静态工具方法；各组件仍需一个 Editor（Unity 机制约束），但每个 Editor 只剩 2-3 行调用，样板代码归零。

**文件结构**：
```
Assets/Editor/
  SceneGizmoHandles.cs           // 新增：手柄工具库（静态方法）
  CameraControllerEditor.cs      // 改造：调用工具库，变薄
  BrokenGlassEditor.cs           // 新增：调用工具库，很薄
```

**工具库形态**（`SceneGizmoHandles.cs`）：
```csharp
namespace IndependentAgentProject.Editor
{
    public static class SceneGizmoHandles
    {
        // 圆形半径手柄：自动处理 Undo / 序列化 / 负值保护 / 颜色
        public static void RadiusHandle(SerializedObject so, string propPath,
            Vector3 center, string undoLabel, Color? handleColor = null)
        {
            var prop = so.FindProperty(propPath);
            if (prop == null) return;
            Color oldColor = Handles.color;
            if (handleColor.HasValue) Handles.color = handleColor.Value;
            EditorGUI.BeginChangeCheck();
            float newR = Handles.RadiusHandle(Quaternion.identity, center, prop.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(so.targetObject, undoLabel);
                prop.floatValue = Mathf.Max(0f, newR);
                so.ApplyModifiedProperties();
            }
            Handles.color = oldColor;
        }

        // 矩形 center+size 手柄（v0.22.13 相机矩形用，迁移过来）
        public static void RectHandle(SerializedObject so,
            string centerPath, string sizePath, string undoLabel) { /* ... */ }
    }
}
```

**各 Editor 形态**（极薄）：
```csharp
[CustomEditor(typeof(BrokenGlass))]
public class BrokenGlassEditor : UnityEditor.Editor
{
    private void OnSceneGUI()
    {
        BrokenGlass t = (BrokenGlass)target;
        SceneGizmoHandles.RadiusHandle(serializedObject, "mAttractRadius",
            t.transform.position, "调整碎玻璃响声半径", Color.white);
    }
}
```

**优点**：
- 消除样板代码重复：Undo、序列化、负值保护、颜色处理统一在工具库。
- 新增组件只需写一个极薄 Editor（2-3 行），开发成本低。
- 横切逻辑（如统一颜色策略、未来加吸附 snapping）只需改工具库一处。
- 仍遵守 Unity 一对一 Editor 机制，无魔法、无反射、无运行时风险。
- 可顺带把 v0.22.13 的 `CameraControllerEditor` 改造为调用工具库，统一风格。

**缺点**：
- 多一个工具库文件，有少量抽象成本。
- 仍是每组件一个 Editor 文件（这是 Unity 机制约束，无法回避）。

**适用场景**：可视化手柄组件会持续增加（装置类有 20+ 个，未来可能多个需要范围可视化），希望控制重复代码。

---

#### 3.3.3 方案 C：标记接口 + 通用 Editor（激进）

**思路**：定义 `ISceneGizmo` 接口，组件实现它返回"自己的手柄描述"（类型、字段路径、颜色等）；用一个统一的 `SceneGizmoEditor` 通过反射/接口调用处理所有标记组件，理论上不再每组件写 Editor。

**文件结构**：
```
Assets/Scripts/.../ISceneGizmo.cs          // 接口定义（放主工程）
Assets/Editor/SceneGizmoEditor.cs          // 唯一的通用 Editor
BrokenGlass.cs 实现 ISceneGizmo             // 组件自描述手柄
CameraController.cs 实现 ISceneGizmo
```

**接口形态**：
```csharp
public interface ISceneGizmo
{
    GizmoDescriptor GetGizmoDescriptor();
}
public struct GizmoDescriptor
{
    public enum Shape { Circle, Rect }
    public Shape shape;
    public string[] fieldPaths;   // 序列化字段路径
    public Color color;
}
```

**致命问题**：
- Unity 的 `[CustomEditor]` **只能绑定一个具体类型**，不支持绑定接口。要让一个 Editor 服务多个组件类型，需用 `[CustomEditor(typeof(MonoBehaviour))]`（会接管所有 MonoBehaviour，严重副作用）或反射动态注册（复杂且脆弱）。
- 实际可行变体是"每个组件类型仍写一个极薄 Editor，但都委托给通用逻辑"--但这退化成了方案 B，只是把工具库换成接口调用，反而多了接口实现成本。
- 反射访问字段有性能损失，且失去编译期检查。
- 当前仅 2 个组件，抽象成本远大于收益，属典型过度设计。

**优点**（理论）：
- 理论上最优雅，组件自描述、Editor 零增长。

**缺点**：
- 与 Unity `[CustomEditor]` 一对一机制冲突，落地需技巧绕过，复杂度高、易踩坑。
- 接口 + 反射 + 描述符结构对当前 2 个组件是过度设计。
- 失去编译期字段路径检查（字符串路径写错运行时才报错）。

**适用场景**：组件类型极多（10+）且手柄形态高度统一时才值得；当前阶段不建议。

---

### 3.4 方案对比与选型建议

| 维度 | A 现状 | B 工具库（推荐） | C 接口通用 |
|------|--------|------------------|------------|
| 新增组件成本 | 写一个完整 Editor | 写一个极薄 Editor（2-3 行） | 实现接口 + 仍需薄 Editor |
| 样板代码重复 | 高 | 低（归零） | 低 |
| 与 Unity 机制契合 | 完全契合 | 完全契合 | 冲突，需绕过 |
| 抽象成本 | 无 | 一个工具库文件 | 接口+描述符+反射 |
| 当前 2 组件适配度 | 够用 | 合适 | 过度 |
| 未来扩展性 | 差 | 好 | 理论最好但落地难 |

**选定方案 A**：经评估，方案 B 的工具库按手柄类型预先抽象，每出现新形状都要回去改工具库，违反开闭原则且增加维护成本。方案 A 各组件 Editor 自包含、独立演进，符合开闭原则，当前 2 个组件用 A 够用且不阻碍未来扩展。

### 3.5 选定方案 A 的详细实现

经评估，方案 B 的工具库按手柄类型预先抽象，每出现新形状都要回去改工具库，违反开闭原则且增加维护成本。**选定方案 A**：各组件 Editor 自包含、独立演进，符合开闭原则。

#### 3.5.1 新增 `Assets/Editor/BrokenGlassEditor.cs`

与 `CameraControllerEditor.cs` 同目录，编进默认 `Assembly-CSharp-Editor`，自动引用主工程。

```csharp
using UnityEditor;
using UnityEngine;
using IndependentAgentProject;

namespace IndependentAgentProject.Editor
{
    [CustomEditor(typeof(BrokenGlass))]
    public class BrokenGlassEditor : UnityEditor.Editor
    {
        private SerializedProperty m_AttractRadius;

        private void OnEnable()
        {
            m_AttractRadius = serializedObject.FindProperty("mAttractRadius");
        }

        private void OnSceneGUI()
        {
            if (m_AttractRadius == null) return;
            serializedObject.Update();

            BrokenGlass t = (BrokenGlass)target;
            Vector3 center = t.transform.position;

            // 手柄固定白色，与既有绿色线框区分
            Color oldColor = Handles.color;
            Handles.color = Color.white;

            EditorGUI.BeginChangeCheck();
            float newRadius = Handles.RadiusHandle(Quaternion.identity, center, m_AttractRadius.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "调整碎玻璃响声半径");
                m_AttractRadius.floatValue = Mathf.Max(0f, newRadius);
            }

            Handles.color = oldColor;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
```

要点：
- `Handles.RadiusHandle(Quaternion, Vector3, float)` 签名已通过 Unity 2021.3 官方文档实测确认，返回新半径。
- `Quaternion.identity`：2D 场景 XY 平面，无需特殊朝向。
- `Mathf.Max(0f, newRadius)`：防止负半径。
- `Undo.RecordObject` + `ApplyModifiedProperties`：支持撤销/重做并序列化。
- 手柄固定白色（用户确认），与既有绿色 `OnDrawGizmos` 线框视觉区分；用 `Handles.color` 临时切换，绘制后恢复原色。

#### 3.5.2 `BrokenGlass.cs` 改动

按用户确认，颜色改成和 `CameraController` 一样可配置，默认绿色；并修正 Tooltip 颜色文案不一致问题。

1. 新增序列化字段：
```csharp
[SerializeField] private Color mGizmoColor = Color.green;
```

2. `OnDrawGizmos` 用 `mGizmoColor` 替代硬编码 `Color.green`：
```csharp
private void OnDrawGizmos()
{
    Gizmos.color = mGizmoColor;
    Gizmos.DrawWireSphere(transform.position, mAttractRadius);
}
```

3. 修正 `mAttractRadius` 的 Tooltip 文案，去掉"红色"改为与实际一致（"绿色 Gizmos 可视化"或直接写"Gizmos 可视化"）。

注：`mAttractRadius` 字段本身、广播逻辑、冷却逻辑均不动。

#### 3.5.3 `CameraControllerEditor.cs` 是否改动

**不改**。v0.22.13 已验收通过，方案 A 下各 Editor 独立，不回迁改动已交付代码。

#### 3.5.4 `Assets/Editor/` 目录规划

随着自定义 Editor 脚本增加，`Assets/Editor/` 需要按职责分子文件夹，避免脚本堆砌混乱。规划如下：

```
Assets/Editor/
  Bootstrap/                        ← 编辑器全局逻辑（不绑定具体组件）
    BootstrapPlayMode.cs            （从根目录迁入）
  CustomInspectors/                 ← [CustomEditor] 组件自定义 Inspector/Scene 手柄
    CameraControllerEditor.cs       （从根目录迁入）
    BrokenGlassEditor.cs            （本期新增）
```

分类原则：
- `Bootstrap/`：编辑器生命周期、启动流程、全局工具类（`[InitializeOnLoad]`、`EditorWindow`、`MenuItem` 等不绑定具体组件的脚本）。
- `CustomInspectors/`：所有 `[CustomEditor(typeof(XXX))]` 的组件自定义 Editor，含 Inspector 扩展和 Scene 视图手柄。
- 未来若出现其它类别（如 `PropertyDrawers/` 属性绘制器、`Settings/` 项目设置面板），再按需新增同级子文件夹。

本期实施：
1. 新建 `Assets/Editor/Bootstrap/` 和 `Assets/Editor/CustomInspectors/` 两个子文件夹。
2. 把 `BootstrapPlayMode.cs`（含 `.meta`）移入 `Bootstrap/`。
3. 把 `CameraControllerEditor.cs`（含 `.meta`）移入 `CustomInspectors/`。
4. 新增的 `BrokenGlassEditor.cs` 直接放 `CustomInspectors/`。

注意：移动脚本时需连同 `.meta` 文件一起移动（保持 GUID 不变），避免场景/prefab 上已挂载的脚本引用丢失。Unity 编辑器内用 Project 视图拖动移动可自动处理 `.meta`；若在文件系统操作，必须手动带上 `.meta`。

### 3.6 工具 / ActionSequence（如适用）

不涉及。

## 4. 实现步骤

1. 规划 `Assets/Editor/` 子目录：新建 `Bootstrap/` 与 `CustomInspectors/`，把 `BootstrapPlayMode.cs` 移入前者、`CameraControllerEditor.cs` 移入后者（含 `.meta`，保 GUID）。
2. 改 `BrokenGlass.cs`：新增 `[SerializeField] private Color mGizmoColor = Color.green;`，`OnDrawGizmos` 用 `mGizmoColor` 替代硬编码 `Color.green`，修正 `mAttractRadius` 的 Tooltip 颜色文案。
3. 新建 `Assets/Editor/CustomInspectors/BrokenGlassEditor.cs`：`[CustomEditor(typeof(BrokenGlass))]` + `OnEnable` 缓存 `mAttractRadius` 的 `SerializedProperty` + `OnSceneGUI` 调用 `Handles.RadiusHandle`（白色手柄、负值保护、Undo 支持）。
4. 自测（见第 6 节），通过后提交验收。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `Handles.RadiusHandle` 在 2D 场景朝向异常 | 已确认 API 签名；`Quaternion.identity` 在 XY 平面工作正常；自测覆盖 |
| 半径被拖成负值 | `Mathf.Max(0f, newRadius)` 保护 |
| 拖动导致取消选中 | 用 `OnSceneGUI`（非 `OnDrawGizmosSelected`），v0.22.13 已验证该模式不会取消选中 |
| 移动 Editor 脚本时 `.meta` 丢失导致 GUID 变化 | 移动时连同 `.meta` 一起移动；优先在 Unity Project 视图内拖动移动（自动处理 `.meta`）；移动后自测确认 v0.22.13 相机手柄仍正常（T10） |
| 回退 | 删除 `BrokenGlassEditor.cs`、还原 `BrokenGlass.cs`、把两个 Editor 脚本移回 `Assets/Editor/` 根目录即恢复原状 |

## 6. 测试建议

纯 Unity 编辑器侧，可独立自测，无需 Python 联调。自测用例矩阵：

| 用例 | 前置条件 | 操作 | 期望 |
|------|----------|------|------|
| T1 手柄出现 | 场景中放置挂有 BrokenGlass 的对象 | 选中该对象 | Scene 视图出现半径手柄（圆周可拖动点） |
| T2 拖动改半径 | T1 | 拖动圆周手柄 | `mAttractRadius` 实时变化，圆周线框同步缩放 |
| T3 不取消选中 | T2 拖动中 | 观察 Hierarchy | 对象保持选中状态 |
| T4 序列化保存 | T2 拖动后 | Ctrl+S 保存场景，关闭并重开场景 | 半径为拖动后的值 |
| T5 撤销 | T2 拖动后 | Ctrl+Z | 半径恢复拖动前值 |
| T6 负半径保护 | T1 | 把手柄拖向圆心甚至越过 | 半径不出现负值，最小为 0 |
| T7 线框颜色可配置 | 未选中 BrokenGlass | 观察 Scene 视图；在 Inspector 改 `mGizmoColor` 为红色 | 默认绿色线框；改色后线框颜色随之变化 |
| T8 广播逻辑不变 | 运行场景，角色踩碎玻璃 | 观察 EnemyAnomalyEvent | 事件正常广播，Radius 为配置值，冷却正常 |
| T9 跟随移动 | 运行场景，碎玻璃被移动（若有） | 观察 | 圆心始终跟随 transform.position |
| T10 目录迁移回归 | 完成目录迁移后 | 选中挂 CameraController 的相机，拖动手柄 | v0.22.13 相机矩形手柄仍正常工作（确认移动脚本未丢引用） |
| T11 Bootstrap 回归 | 完成目录迁移后 | Unity 启动 / 进入 PlayMode | BootstrapPlayMode 仍生效（从 Bootstrap 场景启动），无报错 |

自测在 Unity 编辑器内完成 T1–T11 后再提交用户验收。需联调项：无（与 Agent/Python 无关）。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-07-28 | 完成 v0.22.14 开发：1) 规划 `Assets/Editor/` 子目录，`BootstrapPlayMode.cs` 迁入 `Bootstrap/`、`CameraControllerEditor.cs` 迁入 `CustomInspectors/`（含 `.meta` 保 GUID）；2) `BrokenGlass.cs` 新增 `mGizmoColor` 字段（默认绿色），`OnDrawGizmos` 改用 `mGizmoColor`，修正 Tooltip 颜色文案；3) 新增 `Assets/Editor/CustomInspectors/BrokenGlassEditor.cs`，用 `[CustomEditor] + OnSceneGUI + Handles.RadiusHandle` 实现白色可拖拽半径手柄（负值保护 + Undo）。`Handles.RadiusHandle` 签名已在方案阶段通过 Unity 2021.3 官方文档实测确认。三个文件 linter 检查均无错误。待用户在 Unity 编辑器内验收 T1–T11。 |
| 2026-07-28 | 用户验收通过。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
