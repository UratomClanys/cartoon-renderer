# Cartoon Renderer

A small real-time cartoon rendering test project made in Unity.

This project explores stylized character rendering through simplified lighting, color grouping, outlines, and post-processing. The main goal is to study how anime / illustration-like visuals can be reproduced in a real-time 3D environment.

[中文](#中文)

---

## Overview

The current rendering experiments mainly focus on:

- Cel / Toon Shading
- Simplified light and shadow grouping
- Character outlines
- Controlled color ranges
- Stylized material response
- Post-processing
- Anime-inspired character presentation

The project is currently used as a visual testbed rather than a finished game.

## Rendering Goals

Instead of pursuing physically realistic materials, this project focuses on controlling how information is presented visually.

The renderer aims to reduce unnecessary lighting details and organize the image into clearer graphic shapes, making the final result closer to 2D animation and illustration.

Some of the main areas being tested include:

### Shadow Simplification

Lighting is reduced into a limited number of clear tonal regions instead of continuous realistic shading.

### Color Control

Material colors are adjusted to maintain a stable and readable palette under different lighting conditions.

### Outline Rendering

Outlines are used to strengthen silhouettes and important internal forms.

### Stylized Materials

Materials are designed around the final visual result rather than physically accurate surface properties.

### Post Processing

Post-processing is used to further unify the image and control contrast, color, and overall presentation.

## Screenshots

> Screenshots and comparison images will be added as the project develops.

<!-- Example:
![Render Preview](Screenshots/render-preview.png)
-->

## Status

🚧 Work in Progress

This repository currently contains rendering experiments and visual tests.

Shaders, materials, lighting setups, and post-processing settings may change frequently.

## Built With

- Unity
- Shader-based real-time rendering
- Git LFS for large assets

## Future Experiments

Planned areas of exploration include:

- More stable face and hair shading
- Better control of shadow shapes
- Directional face lighting
- Improved outline behavior
- Material-specific toon shading
- Emissive / unlit material experiments
- More consistent rendering across different environments
- Character-focused lighting setups

## Author

**UratomClanys**

Concept Art / Character Design / Stylized Rendering

## License

This project is currently intended for personal research and portfolio use.

Unless otherwise stated, project assets should not be redistributed or used commercially without permission.

---

# 中文

这是一个使用 Unity 制作的实时卡通渲染测试项目。

项目主要用于研究风格化角色渲染，包括光影归纳、颜色控制、描边、材质表现以及后期处理等内容。

项目的目标并不是追求物理真实感，而是尝试让实时 3D 画面更加接近二维动画、插画以及角色原画中的视觉表现。

---

## 项目简介

目前的渲染实验主要包含：

- Cel / Toon Shading 卡通渲染
- 光影归纳
- 角色轮廓描边
- 色彩范围控制
- 风格化材质表现
- 后期处理
- 动漫 / 插画风角色表现

目前该项目主要作为一个视觉测试场使用，并不是完整的游戏项目。

## 渲染目标

这个项目并不以 PBR 或真实材质表现为主要目标，而是更关注如何控制画面中的视觉信息。

通过减少不必要的光照细节，将阴影、颜色以及材质表现归纳为更加明确的图形区域，使最终画面更接近二维动画和插画。

目前主要研究以下几个方向：

### 光影归纳

将连续的真实光照简化为数量有限、边界明确的明暗区域。

相比传统写实渲染，更强调阴影形状本身对于角色结构和画面设计的作用。

### 色彩控制

控制材质在不同光照环境下的颜色变化，使角色能够保持相对稳定的固有色和整体色彩关系。

### 轮廓描边

通过轮廓线强化角色剪影，并提高角色在复杂背景中的可读性。

同时尝试控制部分内部结构线，使模型呈现更接近二维绘画的视觉效果。

### 风格化材质

材质设计以最终画面效果为优先，而不是完全遵循真实世界中的物理材质属性。

不同部位可以使用不同的光照逻辑，例如：

- 皮肤
- 头发
- 布料
- 金属
- 眼睛
- 发光材质

### 后期处理

通过后期处理进一步统一画面，包括：

- 对比度控制
- 色彩调整
- 明暗关系
- 画面整体色调
- 最终视觉统一

## 截图

> 随着项目继续开发，这里会加入更多效果截图和对比图。

<!-- 示例：
![Render Preview](Screenshots/render-preview.png)
-->

后续计划加入类似：

```text
Standard Rendering → Cartoon Renderer
```

的 Before / After 对比，用于展示普通渲染与卡通渲染之间的差异。

## 当前状态

🚧 开发 / 测试中

目前仓库主要包含渲染实验和视觉测试内容。

Shader、材质、灯光配置以及后期处理参数都可能随着测试持续调整。

## 使用技术

- Unity
- 实时 Shader 渲染
- Toon / Cel Shading
- Post Processing
- Git LFS

## 后续实验方向

计划继续研究：

- 更稳定的角色面部阴影
- 头发明暗归纳
- 面部定向光照
- 更自然的轮廓线表现
- 不同材质对应不同 Toon Shader
- Emissive / Unlit 材质表现
- 不同环境光照下保持稳定的角色颜色
- 面向角色展示的灯光系统
- 更接近动画原画的阴影形状控制

## 作者

**UratomClanys**

Concept Art / Character Design / Stylized Rendering

概念设计 / 角色设计 / 风格化渲染

## 许可

目前该项目主要用于个人研究以及作品集展示。

除非另有说明，项目中的原创资源不允许在未经许可的情况下重新分发或用于商业用途。
