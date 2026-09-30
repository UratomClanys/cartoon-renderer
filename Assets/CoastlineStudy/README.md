# Yellow Coast / 黄色海岸测试模型

参考视频：https://www.bilibili.com/video/BV1K2tw6oEBi/

依据参考画面的配色、等距视角和块状地貌制作的原创静态小岛，不包含原作者资产。
模型由 8 个 OBJ 部件组成：岩壁、草皮、植被、浅滩、浪边、海水底座、水面与水光。
单位约为米，海水底座 12 × 11 米，Y 轴朝上。草丛为双面三角面，树木为低面数几何体。

## 在当前项目测试

1. 等待 Unity 完成导入与脚本编译。
2. 点击菜单 **Tools → Colorido → Create Yellow Coast Test Scene**。
3. 如果当前场景有修改，先按 Unity 提示保存。工具会打开新测试场景。
4. 点击 Play。右键拖动旋转视角，滚轮缩放；左上角可开关自动旋转和像素预览。

每次操作会生成独立的 `Generated` / `Generated 1` 等文件夹，内含材质、
`YellowCoast.prefab` 和 `YellowCoastTest.unity`。可以把 Prefab 拖入自己的场景。
岩壁和草皮带静态 MeshCollider；水和装饰不参与碰撞。没有接入现有角色或投影玩法。

## 导入另一个 Unity 项目

导入 `Artifacts/CoastlineStudy/YellowCoast.unitypackage`，再使用上述菜单。
当前仓库另有 `Colorido/` 项目副本；资源放在仓库根部的 `Assets/CoastlineStudy`，
如果打开的是内层项目，请用 unitypackage 导入。

支持 Unity 2023.2 / Unity 6 的 **Built-in 与 URP** 管线，无第三方依赖。
自动选择 Standard 或 URP/Lit 材质。URP 默认使用原生分辨率的轻绘画感后处理、自发光点缀与柔和 Bloom；HDRP 暂不支持。

若导入旧版后材质变白或变粉，请更新脚本和 Shader，点击 **Tools → Colorido → Repair Yellow Coast Materials**。
修复会更新已生成的小岛材质与 Prefab，不会重新创建场景。
OBJ + MTL 可在建模软件打开；这些静态模型没有骨骼和动画。

## 修改与验证范围

修改 `Tools/generate_coastline.py` 的高度表、调色板或随机种子后重新运行，可重建 OBJ。
同步调色时也需更新编辑器脚本里的材质调色板。
预览图 `Artifacts/CoastlineStudy/Coastline-preview.png` 由同一组几何数据离线渲染，
是形体/配色预览，不是 Unity 截图；不含 Unity 阴影、动态水面与像素后处理。

交付时进行了 OBJ 索引、三角形面积、包完整性和离线预览检查。
两个 C# 脚本使用本机 Unity 2023.2 的程序集完成了离线编译检查，无错误或警告。
后续材质修复已在用户正在使用的 Unity 6000.3.25f1 / URP 项目执行。
确认 15 个材质 Shader 均受支持且未返回 Shader 编译消息，8 个部件的材质槽映射正确。
尚未完成 Play Mode 交互实测。


## 绘画感与柔和泛光（URP）

新增场景会自动安装。已有场景可点击 **Tools → Colorido → Install Stylized Coast Post Processing**，再保存场景。
安装器复制当前 Renderer，为测试相机分配独立的 Full Screen Pass；保留项目默认 Renderer。
仅为当前质量等级正在使用的 URP Asset 注册这个 Renderer。切换到另一 URP Asset 后需重新运行安装菜单。

- 原生分辨率，无方形像素网格或抖色；使用方向性 Kuwahara 平滑保留大轮廓，融合细小色块。
- Camera 上的 **Coastline Post Settings**：Brush Radius 调整笔触大小，Paint Blending 调整平滑程度，Pigment Texture 调整轻微表面纹理。
- Play 模式左上角有 **Soft toon / Stronger strokes**、笔触滑杆、效果开关和 Soft glow 开关。
- Compare 只比较绘画滤镜：左侧未加绘画处理，右侧加绘画处理；两侧均保留当前 Bloom 设置。
- 黄色植被与浪边使用少量 Emission。Bloom 参数保存在 `PostProcessing/CoastlineSoftGlow.asset`，默认 Intensity 0.28、Threshold 1.05、Scatter 0.55。
- Bloom 是屏幕泛光，不会使植被成为实际照亮环境的灯光。安装器开启相机 HDR 与 FXAA。
- 实现是轻绘画感的风格化外观，不是复刻原作者的未知 Shader，也未使用完整的卡通明暗分段光照模型。

在 Unity 6000.3.25f1 / URP / Metal 中验证了 Shader，并通过 Unity 相机渲染导出 `Painterly-glow-preview.png` 与 `Painterly-comparison.png`。
绘画滤镜每像素约 41 次颜色采样，GPU 成本高于先前像素滤镜；尚未做移动设备性能测试。
