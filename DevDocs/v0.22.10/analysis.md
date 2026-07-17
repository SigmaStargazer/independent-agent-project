# 分析报告 - v0.22.10 GroundLight 染色 Player 问题

> **最后更新**：2026-07-17
> **状态**：已解决
> **涉及文件**：`Assets/Rendering/Shaders/SG_GroundLight.shadergraph`、`Assets/Rendering/Materials/M_GroundLight.mat`

---

## 1. 问题描述

项目采用 2D Sprite + 3D 透视场景的画面风格。Camera Projection 为 Perspective。

场景布局：
- `GroundLight_Window_01`：水平 Plane（地面光），材质 `M_GroundLight`，shader 为 `SG_GroundLight`
- Player Sprite：站在 `GroundLight_Window_01` 上
- Player 身后有垂直墙面，地面光与墙面有交界线

现象：
1. Player 在墙前面，按理应挡住地面光与墙的交界线，但实际在 Player 身上能看到那条交界线
2. 禁用 `GroundLight_Window_01` 后，交界线消失，且 Player 上原被交界线覆盖区域的颜色变暗（恢复为正常颜色）

结论：**GroundLight 本该被 Player 遮挡的部分，反而染色了 Player**。

---

## 2. 现状取证

### 2.1 SG_GroundLight 渲染状态

| 字段 | 值 | 含义 |
|------|----|----|
| `m_SurfaceType` | `1` | Transparent |
| `m_ZWriteControl` | `2` | ForceDisabled（不写深度） |
| `m_ZTestMode` | `4` | LEqual |
| `m_AlphaMode` | `2` | **Additive** |
| `m_RenderFace` | `2` | Front（Cull Back） |
| `m_AllowMaterialOverride` | `false` | 材质 uniform 不生效 |
| SubTarget | `UniversalUnlitSubTarget` | URP Unlit |

Additive 混合生成的 Blend：`Blend SrcAlpha One, One One`

```
最终RGB = 源RGB * 源Alpha + 目标RGB * 1
最终A   = 源A   * 1      + 目标A   * 1
```

### 2.2 SG_GroundLight 节点拓扑

属性：`MainTex`（Texture2D）、`RayColor`（Color，浅蓝 `(0.39, 0.71, 1.0)`）、`Intensity`（Vector1，默认 1.0）

```
MainTex ──► Sample Texture 2D ──┬──► RGBA ──► Multiply(RayColor × RGBA) ──► Multiply(×2 × Intensity) ──► BaseColor
                                └──► A ────────────────────────────────────────────────────────────► Alpha
```

- **BaseColor** = `RayColor × MainTex.RGBA × 2 × Intensity`
- **Alpha** = `MainTex.A`（贴图 Alpha 通道）

### 2.3 材质参数（修改前）

`M_GroundLight.mat`：
- `_MainTex`：已赋值（guid `fdaecdaf...`）
- `_RayColor`：`(0.392, 0.706, 1.0, 0)`
- `_Intensity`：`1.12`
- `_QueueOffset`：`0`
- `_QueueControl`：`0`
- `m_CustomRenderQueue`：`-1`

### 2.4 渲染管线与 Sprite

- URP Forward Renderer（`m_RendererType: 1`，非 Renderer2D）
- Player Sprite 用内置 `Sprites-Default` shader（`Queue=Transparent`，`ZWrite Off`，`Blend One OneMinusSrcAlpha`）

---

## 3. 根因分析

### 3.1 绘制顺序问题（根本原因）

URP Transparent 队列按相机距离从远到近排序绘制。`GroundLight_Window_01` 是水平 Plane，Player Sprite 是站立的。

从 Perspective 相机看：
- 地面光 Plane 的包围盒中心比 Player Sprite 的包围盒中心**离相机更近**（地面光在 Player 脚下，透视投影下脚部比身体中心更靠近相机）
- 排序结果：Player 先画（远），GroundLight 后画（近）

当 GroundLight 后画时，它的 Additive 混合会把颜色叠加到已经画好的 Player 像素上：

```
最终RGB = Player RGB + GroundLight RGB × GroundLight Alpha
```

GroundLight 的 RGB = `RayColor × MainTex.RGBA × 2 × Intensity`，是亮的浅蓝色。在地面光与墙面交界处，贴图亮度变化明显（交界线），这个亮度变化通过 Additive 叠加"印"到了 Player 身上。

### 3.2 为什么 GodRay 没问题而 GroundLight 有问题

两者 shader 配置完全相同（Additive + ZWrite Off），但使用场景不同：

- **GodRay**：在 Sprite 前方，Additive 叠加到 Sprite 上是**期望效果**（光柱笼罩人物）
- **GroundLight**：在 Player 脚下的水平面，Player 应该**遮挡**地面光，但 Additive + 后画导致 GroundLight 叠加到了 Player 上

### 3.3 为什么"写深度"无法解决（方案A失败原因）

直觉上让 GroundLight 写深度，Player 通过 ZTest 遮挡它即可。但实际无效，原因：

1. Transparent 队列按距离排序，GroundLight 近处那段排在 Player **之后**绘制。
2. Player 用 `Sprites-Default` 是 `ZWrite Off`，**Player 不写深度**。
3. GroundLight 绘制时做 `ZTest LEqual`，对比的是地面/墙的深度（不透明物体写入的），不会因 Player 而失败。
4. 于是 GroundLight 照样 Additive 叠到 Player 像素上。

**关键**：写深度只影响"在该物体之后绘制的物体"能否通过 ZTest。而本问题中 GroundLight 是后画的，它的 ZWrite 对自己当前帧的绘制无影响，无法阻止它自己叠加到已画的 Player 上。Player 不写深度，也提供不了深度值来挡 GroundLight。

### 3.4 根因总结

**根本矛盾是绘制顺序**：GroundLight 被排在 Player 之后绘制，Additive 叠加染色了 Player。正确的顺序应该是 GroundLight 先画（叠加到不透明背景上发光），Player 后画（覆盖被挡住的 GroundLight 像素）。

---

## 4. 解决方案

### 4.1 方案 A（已否决）：GroundLight 写深度

**修改**：Depth Write `Force Disabled` -> `Force Enabled`

**结果**：测试无效，无任何效果。

**失败原因**：见 §3.3。Player 是 `ZWrite Off` 不写深度，GroundLight 后画时 ZWrite 无法阻止它自己叠加到 Player 像素上。

### 4.2 方案 B（已否决）：Alpha 混合 + 写深度

**修改**：Blend Mode `Additive` -> `Alpha`，Depth Write `Force Disabled` -> `Force Enabled`

**结果**：把 MainTex（亮度高斯分布图）原本黑色需要转成透明的部分，直接以黑色渲染出来，不符合需求。

**失败原因**：`Alpha` 模式是 `Blend SrcAlpha OneMinusSrcAlpha`，MainTex 未做 premultiply。黑色像素（RGB=0）在 Alpha 混合下 `0*α + 背景*(1-α)`，当 α 较大时直接把背景拉黑。Additive 才能让黑色（贡献为0）自然消失为透明。

### 4.3 方案 C（最终采纳）：调整绘制顺序（Queue Offset）

**修改**：在 `M_GroundLight.mat` 材质 Inspector 的 Advanced Options 里，把 **Queue Offset** 调成负值（如 `-1`）。

对应材质文件改动：`_QueueOffset: 0` -> `_QueueOffset: -1`。

**原理**：
- GroundLight 的队列从 `Transparent(3000)` 变为 `3000 + (-1) = 2999`，**早于** Player 的 `3000` 绘制。
- GroundLight 先画：Additive 叠加到不透明背景（地面/墙）上，发光正常。
- Player 后画：`Sprites-Default` 的 `ZTest LEqual` 通过（Player 比墙近），直接覆盖掉被挡的 GroundLight 像素，不再被染色。

**优点**：
- 不改 Shader、不改 Blend、不改 ZWrite，只改绘制顺序，最不侵入。
- 保留 Additive 发光效果。
- 不引入黑色渲染问题。

**注意事项**：
- Queue Offset 负值过大会让 GroundLight 过早绘制，可能被本应在其后绘制的其他透明物体异常遮挡。当前用 `-1` 已解决问题，无需更激进。
- 若场景新增其他需与 GroundLight 交互排序的透明物体，需重新评估 Queue Offset 值。

---

## 5. 实施记录

- [x] 方案 A 测试：无效，否决
- [x] 方案 B 测试：黑色渲染问题，否决
- [x] 方案 C 测试：有效，用户确认（2026-07-17）
- [x] 文档更新：记录最终方案与失败方案分析

**最终方案**：方案 C（Queue Offset = -1），用户已确认有效。

---

## 6. 附录：枚举值对照

### AlphaMode（`UniversalTarget.cs`）

| 值 | 名称 | Blend（RGB, A） |
|----|------|-----------------|
| 0 | Alpha | `(SrcAlpha, OneMinusSrcAlpha, One, OneMinusSrcAlpha)` |
| 1 | Premultiply | `(One, OneMinusSrcAlpha, One, OneMinusSrcAlpha)` |
| 2 | Additive | `(SrcAlpha, One, One, One)` |
| 3 | Multiply | `(DstColor, Zero, Zero, One)` |

### ZWriteControl

| 值 | 名称 |
|----|------|
| 0 | Auto（Opaque 写，Transparent 不写） |
| 1 | ForceEnabled |
| 2 | ForceDisabled |

---

*本报告基于对 `SG_GroundLight.shadergraph`、`M_GroundLight.mat`、URP Asset/Renderer 配置、`Sprites-Default` shader 源码的静态分析，以及方案 A/B/C 的实测验证。*
