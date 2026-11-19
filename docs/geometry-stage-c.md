# 阶段 C · 三维实体差异

实体视图使用 SOLIDWORKS 布尔结果。灰色显示交集，红色显示学生件多余，蓝色显示缺少。可切换单件、完整叠加、仅差异及单侧差集。参考件可半透明显示，支持原有相机交互及四个标准视图。若形状重合不可验证，原坐标差集通过独立核对后可用于预览，对齐规则的几何分仍待复核。普通零件没有完整差集网格时显示灰色单件。钣金正视取 CAD 展开实体中面积占主导的平面方向。

在 Windows 11 上，用独立的 .NET Framework 4.8 项目验证了 HelixToolkit.Wpf 3.1.2 的加载和离屏渲染。已解析到 HelixToolkit、Geometry、Maths 3.1.2，CommunityToolkit 8.3.2，以及 Microsoft/System 传递依赖。Windows 10 和实际屏幕缩放、交互兼容性待验。主程序沿用现有 WPF Viewport3D，因此此次分发没有新增 Helix 依赖及其许可文件。 [Helix 源码和 MIT 许可](https://github.com/helix-toolkit/helix-toolkit) · [NuGet 包与依赖](https://www.nuget.org/packages/HelixToolkit.Wpf/3.1.2)

Windows 真机控制样本通过桌面分析正常入口生成两个 50.265482 mm³ 的差集。离屏 WPF 检查核对红／蓝着色、三个实体叠加、透明参考件以及 96、120、144 DPI 下渲染。37° 旋转偏心孔板通过刚体重合验证。真实桌面交互与离屏渲染分别验收。

## 偏差云图

云图按需计算，沿用实体比较的坐标策略与经过验证的临时实体。显示网格决定采样位置。采样点投影到实际有裁剪边界的 CAD 曲面，在对侧所有 CAD 面中查找最近点。由最近候选的外法向判别所在侧，曲面法向按面方向修正。最近面相切或法向冲突、且无法建立可靠方向时显示灰色。两侧分别采样，参考件侧可显示孔口或缺料导致学生件上没有对应表面的区域。 [最近曲面点](https://help.solidworks.com/2024/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IFace2~GetClosestPointOn.html) · [曲面法向](https://help.solidworks.com/2024/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISurface~EvaluateAtPoint.html) · [面法向方向](https://help.solidworks.com/2025/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFace2~FaceInSurfaceSense.html)

程序会细分真实差集附近的采样网格，覆盖原始平面网格可能跳过的局部差异。距离用 mm 标示，色带在采样点间插值，局部点取显示插值估计。小于采样间距的局部细节仍可通过实体差集定位。云图不参与自动评分。复杂曲面超出采样预算或 CAD 返回无法确认时保留待复核。

Windows 上可运行下列检查，核对模型表面实际渲染的红、绿、蓝和渐变像素。合成输入只验证着色、参考件侧和容差更新。距离与符号由独立的 SOLIDWORKS 真机样本核对。

```powershell
dotnet run --project tests/DeviationRenderingChecks -c Release -p:Platform=x64
```

## 作业基准对齐

基准设置保存三张标准件外侧平面的单位法向、平面偏移、文件 SHA-256 与配置。用户可在模型上点选或从列表选择。程序用 CAD 实体的极值点判断每个平面是否为外支撑面，以法向夹角匹配学生件候选，按三张对应平面计算单位比例的刚体变换。孔壁等内部面不会参与定位。 [SOLIDWORKS 平面参数](https://help.solidworks.com/2026/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISurface~PlaneParams.html) · [实体极值点](https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~GetExtremePoint.html)

候选必须通过三个选定面与额外外侧面的核对。平面匹配阈值为 0.02 mm 和约 0.057°，候选至少匹配四个外侧平面，且至少覆盖两件平面数较少一侧的 60%。当最高匹配数量相同，SOLIDWORKS 会核对各候选下的学生实体是否占据同一空间。若实体结果有不同，转人工复核。比较前后均验证临时学生件体积，差集和交集使用同一变换。

定位过程适用于具有明确外侧平面的普通单实体。单件试算从已保存作业载入同一套基准，批改、差集视图及偏差云图复用经过确认的坐标。对照页和新建作业提供原始 CAD 坐标和基准对齐。
