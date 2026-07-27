# Texture处理指南：

1. 场景需要5种纹理：

   Base Color / Albedo 

   Metallic 

   Normal

   Roughness

   Height / Displacement

2. 所有的纹理贴图都要设置：Wrap Mode: Repeat

3. 如果材质太黑，且Metallic的白色部分太亮，需要处理

   打开Photoshop导入图像 -> 图像 -> 调整 -> 色阶，把输出色阶改成0~26的