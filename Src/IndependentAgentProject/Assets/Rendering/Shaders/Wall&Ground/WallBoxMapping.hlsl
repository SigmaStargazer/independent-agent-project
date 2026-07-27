#ifndef WALL_BOX_MAPPING_INCLUDED
#define WALL_BOX_MAPPING_INCLUDED

// 用于 SG_Wall_v3：把 World Position 用物体纯旋转矩阵变换，
// 得到「旋转跟随 + 缩放不变 + 逐像素无拉丝」的坐标，并按法线选轴输出 UV。
// 依据 bgolus 方案：https://discussions.unity.com/t/box-triplanar-mapping-following-object-rotation/680563
//
// 用法：Custom Function 节点 (File 模式) 引用本文件，Name 填 BoxMapping_float
// Inputs:  WorldPos(Vector3), WorldNormal(Vector3)
// Outputs: BoxUV(Vector2), RotatedNormal(Vector3)

void BoxMapping_float(float3 WorldPos, float3 WorldNormal, out float2 BoxUV, out float3 RotatedNormal)
{
#if defined(SHADERGRAPH_PREVIEW)
    // 预览模式下没有矩阵，用世界坐标直接选轴作为 fallback
    float3 n = WorldNormal;
    float3 blend = abs(n) / dot(abs(n), float3(1,1,1));
    if (blend.x > max(blend.y, blend.z))
        BoxUV = WorldPos.yz;
    else if (blend.z > blend.y)
        BoxUV = WorldPos.xy;
    else
        BoxUV = WorldPos.xz;
    RotatedNormal = n;
#else
    float4x4 w2o = GetWorldToObjectMatrix();

    // 从矩阵各行提取 scale（各列长度的倒数，这里直接取 w2o 各行长度）
    float3 scale = float3(
        length(float3(w2o._m00, w2o._m01, w2o._m02)),
        length(float3(w2o._m10, w2o._m11, w2o._m12)),
        length(float3(w2o._m20, w2o._m21, w2o._m22))
    );

    // 平移分量（除以 scale 归一化）
    float3 pos = float3(w2o._m03, w2o._m13, w2o._m23) / scale;

    // 归一化每行得到纯旋转矩阵（去除 scale，只留旋转方向）
    float3x3 rot = float3x3(
        normalize(float3(w2o._m00, w2o._m01, w2o._m02)),
        normalize(float3(w2o._m10, w2o._m11, w2o._m12)),
        normalize(float3(w2o._m20, w2o._m21, w2o._m22))
    );

    // 用纯旋转变换世界坐标 -> 旋转跟随但缩放不变
    float3 map = mul(rot, WorldPos) + pos;
    float3 norm = mul(rot, WorldNormal);

    // 按变换后法线选轴（box mapping）
    float3 blend = abs(norm) / dot(abs(norm), float3(1,1,1));
    if (blend.x > max(blend.y, blend.z))
        BoxUV = map.yz;
    else if (blend.z > blend.y)
        BoxUV = map.xy;
    else
        BoxUV = map.xz;

    RotatedNormal = norm;
#endif
}

#endif // WALL_BOX_MAPPING_INCLUDED
