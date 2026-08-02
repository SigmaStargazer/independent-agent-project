# PRD - v0.22.15 基于 3D 圆台/圆锥的体积光

> **状态**：已确认
> **对应需求**：`requirements/`（用户口头需求）
> **最后更新**：2026-08-02

---

## 1. 背景与目标

现有体积光方案基于 `SG_GodRay.shadergraph` + Plane 贴图，本质是平面 Billboard 径向渐变。用户不满意：任意视角缺乏 3D 体积感，侧面易穿帮。

**目标**：用 3D 圆锥/圆台几何体作为光束载体，配合专用 URP Shader Graph，做出光源端（尖端）亮、远端暗、左右轮廓软、与地面交界可软化的体积光效果。

## 2. 范围

### 2.1 本期已定版

- 新增体积光 Shader：`Assets/Rendering/Shaders/Light/SG_GodRayCone.shadergraph`
- 效果：高度渐变 + Object Space 水平面 Fresnel（左右暗）+ Depth Fade（交界软化）
- 现有 Plane 方案（`SG_GodRay` 等）保留不动

### 2.2 本期未完成 / 后续迭代

- ProBuilder 运行时生成截锥体 + `VolumetricLightCone` 组件 + Prefab（可先用手工圆锥/圆台挂材质验证）
- Depth Fade 屏幕空间假光斑问题的彻底修复（见方案 §已知问题）
- 噪声扰动、与 Unity Light 参数联动、精确阴影遮挡、ray marching 真体积光

### 2.3 明确不做

- 不改协议 / Python / Agent 工具链路
- 不删除现有 GodRay Plane 资产

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 关卡设计 | 场景中放置体积光 | 圆锥/圆台挂 `SG_GodRayCone` 材质即可调参 |
| 玩家观察 | 多角度观察光束 | 有 3D 体积感；尖端亮；左右轮廓软 |

## 4. 功能需求（定版语义）

1. **高度渐变**：光源在小端（尖端），尖端亮、大端暗；由 UV.y + `_HeightFalloff` 控制
2. **左右轮廓软边**：相对光柱局部坐标，侧面中间亮、左右暗；光柱倾斜时跟着转（Object Space xz 投影）
3. **交界 Depth Fade**：与地面等不透明物体相交时软化硬边；由 `_DepthFadeDistance` 控制
4. **颜色与强度**：`_RayColor`、`_Intensity`；Additive 叠加

## 5. 非功能需求

- URP 12.1.7 / Unity 2021.3 / Shader Graph 12.x
- 需开启 URP Depth Texture（项目已开 `m_RequireDepthTexture: 1`）
- 文件 UTF-8

## 6. 验收标准（定版）

- [x] 尖端（小端/光源）亮，远端暗
- [x] 侧面左右暗、中间相对亮；光柱倾斜后左右方向跟随光柱
- [x] Additive 叠加，双面可见
- [x] Depth Fade 可软化与地面交界硬边
- [ ] Depth Fade 不在「屏幕重叠但未接触」的前景物体上产生假光斑（已知问题，见方案）
- [ ] Prefab + 组件参数化生成截锥体（后续）

## 7. 待确认问题（已关闭）

- [x] 光源在小端（尖端）
- [x] 不复用 `SM_GodRayCone`，新写 `SG_GodRayCone`
- [x] EdgeSoftness 与 DepthFadeDistance 保持独立，不合并

---

*定版说明：Shader 效果已按联调结果定稿；组件化与 Depth Fade 假光斑修复列入后续改进。*
