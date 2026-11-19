# 阶段 C · 三维实体差异

实体视图直接使用与计分共用的 SOLIDWORKS 布尔结果。灰色显示交集，红色显示学生件多余，蓝色显示缺少。可切换单件、完整叠加、仅差异及单侧差集。参考件可半透明显示，支持原有相机交互及四个标准视图。未取得完整差集网格时，旧网格仅用于明确标注的近似预览。钣金正视取 CAD 展开实体中面积占主导的平面方向。

在 Windows 11 上，用独立的 .NET Framework 4.8 项目验证了 HelixToolkit.Wpf 3.1.2 的加载和离屏渲染。已解析到 HelixToolkit、Geometry、Maths 3.1.2，CommunityToolkit 8.3.2，以及 Microsoft/System 传递依赖。Windows 10 和实际屏幕缩放、交互兼容性待验。主程序沿用现有 WPF Viewport3D，因此此次分发没有新增 Helix 依赖及其许可文件。 [Helix 源码和 MIT 许可](https://github.com/helix-toolkit/helix-toolkit) · [NuGet 包与依赖](https://www.nuget.org/packages/HelixToolkit.Wpf/3.1.2)

Windows 真机控制样本通过桌面分析正常入口生成两个 50.265482 mm³ 的差集。离屏 WPF 检查核对红／蓝着色、三个实体叠加、透明参考件以及 96、120、144 DPI 下渲染。37° 旋转偏心孔板通过刚体重合验证。真实桌面交互与离屏渲染分别验收。
