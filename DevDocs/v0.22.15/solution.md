# 技术方案 - v0.22.15 基于 3D 圆台/圆锥的体积光

> **状态**：已确认（Shader 定版）
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-02

---

## 1. 方案概述

用 3D 圆锥/圆台 mesh 作为光束载体，材质使用新建的 `SG_GodRayCone.shadergraph`。Shader 由三路衰减相乘得到 Alpha：

1. **高度渐变**（UV.y）：尖端亮、远端暗  
2. **Object Space 水平 Fresnel**：相对光柱轴线的左右轮廓软边（倾斜跟随）  
3. **Depth Fade**：与场景不透明物体交界软化  

BaseColor 只输出 `_RayColor`；Additive（`SrcAlpha One`）下最终贡献 = `RayColor × Alpha`，避免衰减被平方。

现有 Plane 方案（`SG_GodRay` 等）保留不动。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | - | 无 |
| Unity | `Assets/Rendering/Shaders/Light/SG_GodRayCone.shadergraph` | 新增（本期定版） |
| Unity | 对应 Material（用户自建/挂到圆锥上） | 新增 |
| Unity | `VolumetricLightCone` 组件 / Prefab | **后续**（本期未做） |
| 协议 | 无 | 无 |

## 3. 渲染设置（定版）

Graph Inspector > URP Target：

| 设置项 | 值 | 说明 |
|--------|-----|------|
| Surface Type | Transparent | |
| Blending | Additive | `m_AlphaMode = 2` |
| Render Face | Both | Cull Off |
| Depth Write | Force No | |
| Depth Test | LEqual | |
| Cast/Receive Shadows | 当前为 True（可按需关） | |

前提：URP Asset 开启 **Depth Texture**（本项目已开）。

## 4. Properties（定版）

| 属性 | 默认值 | 作用 |
|------|--------|------|
| `_RayColor` | (0.4, 0.7, 1, 1) | 光束颜色 → 直接进 BaseColor |
| `_Intensity` | 1.0 | 乘进 Alpha |
| `_HeightFalloff` | 1.5 | `pow(UV.y, _)`，越大远端灭得越快 |
| `_EdgeSoftness` | 3.0 | 水平 Fresnel 的 Power，越大左右边缘越窄 |
| `_DepthFadeDistance` | 0.5 | Depth Fade 软化距离（米），与 EdgeSoftness **独立** |

## 5. 最终节点连线（定版，与当前 `SG_GodRayCone` 一致）

### 5.1 整体公式

```
BaseColor = _RayColor

heightGlow  = pow(UV.y, _HeightFalloff)          // 尖端 V=1 亮，底端 V=0 暗；无 OneMinus
fresnelEdge = pow(saturate(dot(nXZ, vXZ)), _EdgeSoftness)
depthFade   = saturate( (SceneDepthEye - abs(ViewPos.z)) / _DepthFadeDistance )

Alpha = heightGlow × fresnelEdge × depthFade × _Intensity
```

其中 `nXZ` / `vXZ` 为 Object Space 法线/视线去掉局部 Y（轴向）后、再 Normalize 的水平分量。

### 5.2 高度渐变

```
UV → Split → G(V) → Power(B=_HeightFalloff) → heightGlow
```

- **不要** OneMinus（光源在尖端/小端，V=1 应亮）
- 网格 UV：底（大端）V=0，顶（小端/光源）V=1

### 5.3 左右轮廓（Object Space 水平 Fresnel）

```
Normal(Object)  → Split → R(x), B(z) → Combine(x,z,*) → Normalize → nXZ
ViewDir(Object) → Split → R(x), B(z) → Combine(x,z,*) → Normalize → vXZ
nXZ · vXZ → Saturate → Power(B=_EdgeSoftness) → fresnelEdge
```

- Space 必须是 **Object**，光柱倾斜时「左右」跟着转  
- 只用 xz，避免上下轮廓也被压暗  
- **不要**在 Dot 后加 OneMinus（要中间亮、边缘暗）

### 5.4 Depth Fade（Shader Graph 12 无 Eye Depth 节点）

```
Scene Depth（Sampling Mode = Eye，UV 留空）
        │
        ▼
    Subtract[A]
        ▲
Position(View) → Split → B(Z) → Absolute ──┘
        │
        ▼
    Divide[A] ←── _DepthFadeDistance 接 B
        │
        ▼
    Saturate → depthFade
```

**实测注意（Shader Graph 12.1.7）：**

| 枚举 | 数值 | 含义 |
|------|------|------|
| DepthSamplingMode | 0 / 1 / **2** | Linear01 / Raw / **Eye** |
| 无 Eye Depth 节点 | - | fragment 深度用 `Position(View).z` 取绝对值 |

错误示范（曾导致无效/全黑）：

- `UV` 接到 Scene Depth → 采样错乱，材质球全黑  
- `SceneDepth(Eye) - ScreenPosition(Default).A` → Default 的 A 恒为 0，depthFade 恒为 1，等于没加

### 5.5 输出

```
_RayColor → BaseColor

heightGlow × fresnelEdge → Multiply
        × depthFade → Multiply
        × _Intensity → Alpha
```

Additive 下贡献 = `BaseColor × Alpha`，故 BaseColor **不要**再乘 heightGlow / Intensity，否则会平方变暗。

## 6. 几何体约定（定版用法）

- 载体：圆锥或圆台 mesh（本期可用手工/ProBuilder 摆好的 mesh；运行时生成组件后续做）  
- 光源在 **小端（尖端）**  
- UV.y：尖端 1，大端 0  
- 建议不生成实心顶盖，或接受端面也被 shader 画到（当前未单独剔端面）

## 7. 已知问题

### 7.1 Depth Fade 屏幕空间假光斑（已确认复现）

**现象**：侧视时光柱与水平管道有明显空隙、未接触；正视时管道表面出现一块蓝色柔边「光斑」，像被光照到。

**原因**：Depth Fade 只比较同像素的场景深度与光柱片元深度，**不保证世界空间真接触**。两者在屏幕重叠且沿视线深度接近时，会软化出一块斑。这是 Soft Particle 类方案的固有局限。

**缓解（可选，未定版进 shader）**：

| 手段 | 说明 |
|------|------|
| 减小 `_DepthFadeDistance` | 如 0.05~0.1，缩小误判范围 |
| 后方强制为 0 | `depthFade *= step(0, sceneDepth - eyeDepth)`（与 Saturate 叠加更狠） |
| 临时断开 Depth Fade | 验证光斑是否消失 |
| 接受/场景规避 | 假光斑比地面硬边更烦时，可关掉 Depth Fade，靠高度衰减收远端 |

### 7.2 Soft Particle 与 ZTest

当前 ZTest = LEqual、ZWrite Off。后方片元理论上应被深度测试裁掉；假光斑仍可能来自「光柱 mesh 前表面深度已接近管道」或深度差落入 fade 距离。需结合场景间距与 `DepthFadeDistance` 一起调。

### 7.3 材质球预览

Scene Depth 在 Shader Graph / 材质球预览里没有真实场景深度，预览可能偏黑或异常；以 Scene/Game 视图为准。

## 8. 后续改进思路

| 优先级 | 项 | 说明 |
|--------|----|------|
| P0 | Depth Fade 假光斑 | 加后方裁切；或可开关的 Depth Fade；或仅对指定 Layer 软化 |
| P1 | `VolumetricLightCone` 组件 | ProBuilder 生成截锥体 + 手动 UV + MaterialPropertyBlock 调参 + Prefab |
| P1 | 默认材质资产 | `M_VolumetricLightCone.mat` 入库，避免每人自建 |
| P2 | 噪声扰动 | 可选 `_MainTex` / Simple Noise，模拟尘埃闪烁 |
| P2 | 与 Light 联动 | 颜色/强度/开关跟随某盏 Spot/Point |
| P3 | 真体积感 | Ray march / 多项叠加；成本高，非本期 |

**明确不做的合并**：不要把 `_DepthFadeDistance` 并进 `_EdgeSoftness`——一个是米制交界软化，一个是 Fresnel 幂次，调节方向常冲突。

## 9. 实现记录

| 日期 | 说明 |
|------|------|
| 2026-07-30 | 初版 PRD/方案：圆台 + Fresnel 设想 |
| 2026-07-31 | 纠正：不用 UV.x 做径向软边；改为法线·视线 Fresnel |
| 2026-08-02 | 联调定版：`SG_GodRayCone`；Object xz Fresnel；Depth Fade 用 Position(View)；记录假光斑问题 |
| 2026-08-02 | 文档定版；组件化与假光斑修复列入后续 |

---

*Shader 定版以仓库内 `SG_GodRayCone.shadergraph` 为准；组件与假光斑修复另开版本或续作本版本后续提交。*
