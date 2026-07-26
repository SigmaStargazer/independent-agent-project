# 技术方案 - v0.22.12 墙面/地面 Shader 贴图朝向跟随物体旋转

> **状态**：待确认
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-07-24

---

## 1. 方案概述

用 **World Space Position**（逐像素、缩放不变、无拉丝）乘以**从 `unity_WorldToObject` 矩阵提取的纯旋转矩阵**（归一化掉 scale），得到「旋转跟随 + 缩放不变 + 无拉丝」的坐标，再按变换后法线选轴做 box mapping，÷ TileSize 平铺。此为 bgolus 经社区验证的方案。

## 2. 根因分析

### 2.1 各方案的实测结果对照

| 尝试 | UV 来源 | 缩放不变 | 旋转跟随 | 拉丝 | 结论 |
|------|---------|----------|----------|------|------|
| `SG_Wall 2` | World Pos XY | ✅ | ❌ | ✅无 | 不满足需求3 |
| `SG_Wall 2` 改 Object Space | Object Pos | ❌缩放变 | ✅ | ❌拉丝 | 用户实测失败 |
| `SG_Wall`/`废弃` Triplanar | Object Pos | ❌缩放变 | ✅ | - | 用户实测失败 |
| `SG_Wall_Test1` | Object Pos ÷ Scale | ❌仍缩放变 | ✅ | ❌拉丝 | 用户实测失败 |

### 2.2 为什么 Object Space 方案全部失败

- **缩放变**：实测 Object Space Position 在你的环境下会随物体 Scale 变化（与部分文档描述不符，以实测为准）。
- **拉丝**：Unity 自带 Plane 只有 11×11 顶点，Object Space Position 是顶点属性，在低密度网格的大三角形内靠插值，产生拉丝；World Space Position 是逐 fragment 的，无此问题。
- **÷Scale 无效**：Object 节点 Scale 与 Position 的缩放关系不是简单线性除法能抵消（`SG_Wall_Test1` 实测证明）。

### 2.3 为什么 World Position + 纯旋转矩阵可行

- World Position 逐像素 -> 无拉丝 ✅
- World Position 不含物体 Scale -> 缩放不变 ✅
- 用从矩阵提取的**纯旋转**（归一化掉 scale）变换 WorldPos -> 旋转跟随 ✅
- 三个需求同时满足。

## 3. 关键依据：bgolus 的最终解法

来源：Unity Discussions「Box / Triplanar mapping following object rotation」，bgolus 第 8 楼最终修正版。核心 HLSL：

```hlsl
// 从 unity_WorldToObject 提取 scale（各列长度）
float3 scale = float3(
    length(unity_WorldToObject._m00_m01_m02),
    length(unity_WorldToObject._m10_m11_m12),
    length(unity_WorldToObject._m20_m21_m22)
);
// 平移分量
float3 pos = unity_WorldToObject._m03_m13_m23 / scale;
// 归一化每行得到纯旋转矩阵（去除 scale）
float3x3 rot = float3x3(
    normalize(unity_WorldToObject._m00_m01_m02),
    normalize(unity_WorldToObject._m10_m11_m12),
    normalize(unity_WorldToObject._m20_m21_m22)
);
// 用纯旋转变换世界坐标 -> 旋转跟随但缩放不变
float3 map = mul(rot, IN.worldPos) + pos;
float3 norm = mul(rot, IN.worldNormal);
// 按变换后法线选轴
float3 blend = abs(norm) / dot(abs(norm), float3(1,1,1));
float2 uv;
if (blend.x > max(blend.y, blend.z))      uv = map.yz;
else if (blend.z > blend.y)                uv = map.xy;
else                                       uv = map.xz;
fixed4 c = tex2D(_MainTex, uv * (1/_TexScale));
```

要点：
- `unity_WorldToObject` 即 URP 中的 `GetWorldToObjectMatrix()`。
- `normalize` 每行 = 去除 scale 只留旋转方向。
- `DisableBatching=True` 标签必须加（否则静态/动态批处理会丢失物体旋转信息）。

## 4. 在 Shader Graph 中的实现

### 4.1 两条实现路径

**路径 A：Custom Function 节点（推荐，最可靠）**

用 Custom Function 节点封装上述 HLSL，输入 WorldPos + WorldNormal，输出变换后的 map 坐标和 norm 法线。理由：矩阵逐元素访问和 normalize 构造 3x3 矩阵在纯 Shader Graph 节点里非常繁琐，Custom Function 直接写 HLSL 最清晰可维护。

**路径 B：纯节点拼装**

理论上可用 Split / Normalize / Combine 拼出矩阵运算，但节点数量多、易错、难调试。不推荐。

### 4.2 整体数据流

```
Position(World)  ──┐
                   ├─► Custom Function ──► map(旋转跟随+缩放不变) ──► 选轴 ──► ÷TileSize ──► UV ──► POM ──► Sample
Normal(World)    ──┘                   ──► norm(变换后法线)
```

Custom Function 内部完成：提取纯旋转 -> 变换 pos 和 normal -> 按法线选轴输出 UV 候选。

### 4.3 TileSize 支持 Vector2 长宽比

bgolus 原版用单一 `_TexScale`。我们需要 `TileSize` 为 Vector2，对选出的 uv 两个分量分别除。这在 Custom Function 外用 Divide(Vector2) 实现，或在函数内对 uv.xy 分别乘 `1/TileSize`。

### 4.4 HeightMap / POM 接入

POM 节点接收 UV。用同一套「选轴后的 UV ÷ TileSize」传入 POM，POM 输出偏移 UV 再采样各贴图。保证凹凸方向与贴图方向一致。

注意：box mapping 在三轴交界处会有接缝，POM 在接缝处可能跳变。可接受，或限制只在主导轴做 POM。

### 4.5 Normal 贴图空间

box mapping 选出的法线贴图在「变换后空间」。需用 Transform 节点转回 Tangent Space 接入 `NormalTS`。这是 Object/自定义空间 triplanar 的已知复杂点（bgolus 在另一帖详述）。

简化处理：若墙面/地面朝向固定（轴对齐），可直接用世界法线选轴，法线贴图变换较简单。

### 4.6 必须加 DisableBatching 标签

Shader Graph 中需在 SubShader Tags 加 `DisableBatching=True`，否则批处理后 `unity_WorldToObject` 丢失单个物体旋转，旋转跟随失效。

## 5. 实现策略：分两阶段

### 阶段一：Custom Function + box mapping（验证三点）

1. 新建 `SG_Wall_v3.shadergraph`。
2. 加 Custom Function 节点，写入 bgolus 的纯旋转提取 + 变换 + 选轴逻辑。
3. 输出 UV ÷ TileSize(Vector2) -> POM -> Sample BaseColor。
4. SubShader Tags 加 `DisableBatching=True`。

**验收**：
- [ ] 旋转 Plane 任意角度，贴图跟随。
- [ ] 缩放 Plane，格大小不变。
- [ ] 无拉丝。
- [ ] HeightMap POM 凹凸正常。

### 阶段二：法线贴图 + AO + 完整材质

阶段一通过后，补全 Normal（含空间转换）、AO、Metallic、Smoothness、Emission 等通道。

## 6. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | 无 | 无 |
| Unity | `Assets/Rendering/Shaders/Wall/SG_Wall_v3.shadergraph`（新建） | 新建，含 Custom Function + DisableBatching |
| 协议 | 无 | 无 |

## 7. 风险与回退

| 风险 | 缓解 |
|------|------|
| Custom Function 在 URP 2021.3.8 下编译问题 | 先最小化测试；可用 HLSL 文件外部引用 |
| 批处理导致旋转丢失 | `DisableBatching=True` |
| box mapping 三轴接缝 | 可接受；或对墙面/地面分别用固定轴 |
| 法线贴图空间转换复杂 | 阶段二单独处理，参考 bgolus ObjectSpaceTriplanarNormal 子图 |

## 8. 测试建议

### 阶段一
- [ ] Plane 旋转 0/45/90/180°，贴图方向正确跟随。
- [ ] Plane Scale=(2,2,2) / (5,1,5)，格大小不变。
- [ ] 贴图无拉丝。
- [ ] HeightMap 凹凸方向正确。

### 阶段二
- [ ] Normal 贴图凹凸方向与光照一致。
- [ ] 墙面+地面共用材质都正确。

---

## 9. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
