# SG_Wall_v3 连线指南（阶段一）

> 配套文件：`Wall/WallBoxMapping.hlsl`
> 目标：验证「旋转跟随 + 缩放不变 + 无拉丝 + HeightMap 凹凸」四点

---

## 0. 前置：新建 Shader Graph

1. 在 `Assets/Rendering/Shaders/Wall/` 右键 -> Create -> Shader Graph -> URP -> Lit Shader Graph。
2. 命名 `SG_Wall_v3.shadergraph`。
3. 打开它，按下文操作。

## 1. DisableBatching 标签说明（重要，但不在 Shader Graph 里加）

> `DisableBatching` 防止 Unity 动态批处理把多个物体合并，导致丢失单个物体的旋转信息。
> **URP Shader Graph 编辑器里没有加这个 tag 的入口**，需要用以下方式之一处理：

**方式 A（开发阶段推荐）：先不管，测试时关闭动态批处理**

在 Project Settings -> Player -> Other Settings 里，把 `Dynamic Batching` 关掉。或者测试时场景里只放一个用该材质的物体（单个物体不会被批处理）。这样开发阶段可以快速迭代。

**方式 B（最终发布时）：导出 shader 代码手动加 tag**

1. 选中 `SG_Wall_v3.shadergraph`，在 Inspector 点 `View Generated Shader`。
2. 复制全部代码到新文件 `SG_Wall_v3_Final.shader`。
3. 在 `SubShader` 块的 `Tags` 里加 `"DisableBatching" = "True"`：
   ```
   SubShader {
       Tags { "RenderPipeline" = "UniversalPipeline" "DisableBatching" = "True" }
       ...
   }
   ```
4. 用这个 `.shader` 文件做材质。

> 缺点：每次改 Shader Graph 后要重新导出。所以开发阶段用方式 A，定稿后再用方式 B。

## 2. 创建 Properties（Graph Inspector -> Properties）

| 属性名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Base Color` | Texture2D | - | 主贴图 |
| `Normal` | Texture2D | - | 法线贴图 |
| `HeightMap` | Texture2D | - | 高度图 |
| `AO` | Texture2D | - | 环境光遮蔽 |
| `TileSize` | Vector2 | (1,1) | 每格大小（世界单位） |
| `Amplitude` | Float | 0.02 | POM 凹凸幅度 |
| `Steps` | Float | 50 | POM 步数 |

> 参考 `SG_Wall 2` 的属性设置，数值可照搬。

## 3. 核心节点连接

### 3.1 Custom Function 节点（核心）

1. Create Node -> `Custom Function`。
2. 选中它，在 Graph Inspector -> Node Settings：
   - **Type**: `File`
   - **Source**: 拖入 `WallBoxMapping.hlsl`
   - **Name**: `BoxMapping`（不带 `_float` 后缀）
3. 设置 Inputs（点 + 号添加）：
   - `WorldPos` -> 类型 Vector3
   - `WorldNormal` -> 类型 Vector3
4. 设置 Outputs：
   - `BoxUV` -> 类型 Vector2
   - `RotatedNormal` -> 类型 Vector3

### 3.2 输入连接

```
Position 节点 (Space: World)  ──►  Custom Function.WorldPos
Normal 节点   (Space: World)  ──►  Custom Function.WorldNormal
```

> 注意：Position 和 Normal 节点都要选 **World** 空间（不是 Object）。

### 3.3 UV 平铺

```
Custom Function.BoxUV  ──►  Divide.A
TileSize Property      ──►  Divide.B
Divide.Out             ──►  POM.UVs
```

> Divide 节点：BoxUV ÷ TileSize，得到平铺 UV。

### 3.4 Parallax Occlusion Mapping

创建 `Parallax Occlusion Mapping` 节点，连接：

| POM Slot | 连接来源 |
|-----------|----------|
| Heightmap | HeightMap Property |
| HeightmapSampler | (自动) |
| UVs | Divide.Out（÷TileSize 后的 UV） |
| Amplitude | Amplitude Property |
| Steps | Steps Property |
| ParallaxUVs (输出) | -> 各 Sample Texture 的 UV |

### 3.5 采样贴图

创建 4 个 `Sample Texture 2D` 节点，UV 全部连 `POM.ParallaxUVs`：

| Sample 节点 | Texture | 输出到 |
|-------------|---------|--------|
| Sample 1 | Base Color | Fragment.BaseColor |
| Sample 2 | Normal (Type: Normal) | Fragment.NormalTS |
| Sample 3 | AO | Fragment.Occlusion |
| Sample 4 | Metallic | Fragment.Metallic |

> Normal 的 Sample Texture 2D 节点要把 Type 设为 `Normal`。

## 4. 完整数据流图

```
Position(World) ──┐
                  ├─► Custom Function ──► BoxUV ──► ÷TileSize ──► POM ──► ParallaxUVs
Normal(World)   ──┘                   ──► RotatedNormal              ├─► Sample BaseColor -> Fragment.BaseColor
                                                                     ├─► Sample Normal    -> Fragment.NormalTS
                                                                     ├─► Sample AO        -> Fragment.Occlusion
                                                                     └─► Sample Metallic  -> Fragment.Metallic
```

## 5. 验证步骤

连好后，创建材质用 `SG_Wall_v3`，赋给一个 Plane：

1. **旋转测试**：旋转 Plane 90°，贴图应跟随旋转。
2. **缩放测试**：Plane Scale 设 (5,1,5)，格大小应与 (1,1,1) 一致。
3. **拉丝测试**：缩放后观察是否拉丝（应无拉丝）。
4. **HeightMap**：凹凸应正常显示。

## 6. 常见报错排查

| 报错 | 原因 | 解决 |
|------|------|------|
| `BoxMapping_float` 未定义 | hlsl 文件路径错或 Name 填错 | 确认 Source 拖入了文件，Name 是 `BoxMapping`（不带 `_float`） |
| 预览黑块 | SHADERGRAPH_PREVIEW 分支问题 | 确认 hlsl 里有 `#if defined(SHADERGRAPH_PREVIEW)` 分支 |
| 旋转不跟随 | 批处理导致 | 关闭 Dynamic Batching（方式A），或导出 shader 加 tag（方式B） |
| 缩放仍变 | Position 选了 Object 空间 | 确认 Position 是 **World** 空间 |

## 7. 我能帮你做什么

- 如果连线后报错，把 Unity Console 的错误信息发我，我帮你定位。
- 如果效果不对（旋转/缩放/拉丝），截图发我，我帮你分析。
- 阶段一验证通过后，我再补全法线空间转换等细节。
