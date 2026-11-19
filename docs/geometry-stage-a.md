# 阶段 A · 实体几何比较选型

## 技术决策

- **实体运算**采用 SOLIDWORKS 原生 B-Rep。在现有 STA 和只读工作副本内，将一个参考实体和一个学生实体分别复制为独立的临时实体。对每次交集或差集分别复制两个操作数。SOLIDWORKS 官方文档说明 `Operations2` 会使本次操作数失效。阶段 A 记录实际返回数组、错误码和体积。差集为空时还需要交集及体积守恒支持，才报告零差异。 [Operations2](https://help.solidworks.com/2011/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~Operations2.html) · [错误枚举 · 2025](https://help.solidworks.com/2025/english/api/swconst/SolidWorks.Interop.swconst~SolidWorks.Interop.swconst.swBodyOperationError_e.html) · [Copy · 2025](https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~Copy.html)
- **对齐**默认保持原始坐标。自选对齐时，调用 `reference.GetCoincidenceTransform2(student, out transform)` 取得学生件到参考件的候选矩阵，仅接受有限值、单位尺度和正交右手旋转。只对学生临时实体调用 `ApplyTransform`，双向差集和交集共同核对刚体候选。失败时整次对齐实验待复核。近似配准和人工基准输入留待后续。 [GetCoincidenceTransform2](https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody2~GetCoincidenceTransform2.html) · [ApplyTransform](https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~ApplyTransform.html) · [矩阵约定](https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IMathTransform~ArrayData.html)
- **xCAD.NET**的 `SwBody`、`SwTempBody` 展示了临时实体封装、变换及原生布尔错误处理。参考检测与生命周期设计，沿用项目现有 COM 层。xCAD 框架为 MIT，复用或分发代码需保留版权和许可。其 Examples 提供独立控制台建模、WPF 与多目标框架示例，仓库根目录未见统一许可证，须单独核对各示例的授权。阶段 A 无复制代码或新增包依赖。 [xCAD.NET](https://github.com/xarial/xcad) · [SwBody](https://github.com/xarial/xcad/blob/master/src/SolidWorks/Geometry/SwBody.cs) · [SwTempBody](https://github.com/xarial/xcad/blob/master/src/SolidWorks/Geometry/SwTempBody.cs) · [xCAD Examples](https://github.com/xarial/xcad-examples)
- **CodeStack**的模型对齐示例说明如何把候选矩阵应用于临时复制体，切片示例说明如何取得相交实体并预览。这里只参考 API 用法，不分发宏代码。生产计分是否使用对齐需要教师确认作业坐标策略。 [模型对齐](https://www.codestack.net/solidworks-api/geometry/bodies-diff/) · [实体切片](https://www.codestack.net/solidworks-api/geometry/slice-body/)
- **查看器候选**是 MIT 的 `HelixToolkit.Wpf` 3.1.2，包内有原生 net48 资产，适合先用 WPF 版本做相机、透明度和差集显示实验。直接依赖包括 HelixToolkit、Geometry、CommunityToolkit.Common/Diagnostics/Mvvm 和 System.IO.Compression。阶段 C 应在独立样例锁定完整依赖树、核查许可证并随分发保留许可声明。本阶段只确认包元数据兼容性。Windows 10/11、DPI、硬件性能和 NuGet 的实装验证仍待执行。 [Helix Toolkit](https://github.com/helix-toolkit/helix-toolkit) · [NuGet 3.1.2](https://www.nuget.org/packages/HelixToolkit.Wpf/3.1.2)
- **显示**后续优先用缺失蓝、多余红、交集灰。可选的实验网格由这三组布尔结果直接生成。当前网格着色仅为近似预览。偏差云图单独评估可靠的距离、表面法向/内外分类和参考件缺失侧采样。钣金正视方向在阶段 C 优先核对 CAD 展开基准或平面，暂不接入本实验几何计分。

## 证据边界

只支持配置与读取状态可验证的原生单实体零件。曲面体、多实体、钣金、未重建、引用问题和异常拓扑均留待复核。相同哈希的正式快路径保持可用，阶段 A 的实验刻意执行真实双向布尔，以便同一份文件也能验证 CAD 的空结果行为。

报告包含两侧 SHA-256、配置、CAD 修订版、源文件完整性及原始布尔错误码。`Missing`、`Extra`、`Overlap` 分别保存两次差集和交集。体积以 m³ 记录，`DifferenceRatio` 是缺失与多余体积之和除以参考体积。每侧体积守恒的核对误差为该侧体积的 `1e-8` 倍，绝对下限为 `1e-24 m³`。该校验值用于判断实验输出能否互相印证。当前批改阈值由作业规则管理。

无错误且返回空差集时，核对交集体积与该侧原始体积是否一致。交集返回无相交时，核对两份有效差集是否分别保留两侧原始体积。只有互相印证的空结果才记为 `ConfirmedEmpty`。官方把 `swBodyOperationEmptyBody` 定义为错误，本实验保留原始错误和待复核状态。真机需核对相同体、完整包含和相切模型的实际返回值。`Measured` 为 `false` 时，局部原始读数只供诊断，整体差异率留空。

## 在 Windows 上运行

用 x64 Windows 终端构建，打开一个与 `--expect` 匹配的 SOLIDWORKS 实例。使用本机保存的样本并把结果写入一个新的文件。

```powershell
dotnet build SWGrade.sln -c Release -p:Platform=x64
$probe = '.\src\SWGrade.Probe\bin\x64\Release\net48\SWGrade.Probe.exe'
& $probe --geometry --expect 2025 --reference 'C:\Samples\reference.SLDPRT' `
    --student 'C:\Samples\moved-hole.SLDPRT' --alignment original --json '.\geometry-2025.json'
```

`--alignment coincident` 提交刚体重合实验。遇到不同配置名时，先确认作业基准，再以 `--reference-config` 和 `--student-config` 明确指定两侧的名称。`--json` 可与 `--mesh` 同用，输出实际布尔结果的可视化三角坐标，长度为 m。网格导出失败独立记录在 `MeshIssue`。退出码 `0` 表示实验读数已通过内部校验，`2` 表示参数无效，`3` 表示平台不适用，`4` 表示实验无法完整验证或报告写入失败。

### 在改孔后复用已验证的放置方式

使用 `--transform-report` 选择修改前成功对齐的 JSON。此参数启用 `reuse` 模式。报告须具备 `CoincidenceVerified` 状态、零差异和完整的交集体积。参考件的文件内容和配置需与报告一致，学生件采用原配置。显式选择报告表示本次学生件仍采用校准时的放置方式。加孔等编辑可以改变学生文件哈希。探针默认读取报告记录的配置，也会检查显式提供的配置。

```powershell
.\SWGrade.GeometryProbe.exe --geometry --expect 2026 `
    --reference '.\test1.SLDPRT' --student '.\test.SLDPRT' `
    --transform-report '.\geometry-before-hole.json' --json '.\geometry-hole-diff.json'
```

若加孔前的成功报告已遗失，可将有孔件另存为副本，在副本中压缩切除特征，保持原放置方式与配置名，再用该副本作为学生件运行 `--alignment coincident --json '.\geometry-before-hole.json'`。校准保留同一个未修改的参考件。对有孔件进行复测时，在输出中核对 `PlacementReused`、差集状态和报告中的 `PlacementSource`。本模式继续沿用已有只读副本、STA 和差集守恒检查。

复用时调用 `ISldWorks.GetMathUtility` 和 `IMathUtility.CreateTransform` 在当前 CAD 会话重建矩阵，并核对 CAD 返回的旋转、平移、单位尺度。该矩阵仅施加于临时实体，变换前后的实体体积也会再次校验。 [GetMathUtility](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.isldworks~getmathutility.html) · [CreateTransform](https://help.solidworks.com/2026/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IMathUtility~CreateTransform.html)

## 第一轮真机样本

使用 SOLIDWORKS 保存三份无外部引用的普通单实体零件，活动配置名一致。可以用长宽不等的板件制作，例如 40 × 30 × 4 mm，添加一个偏心的 Ø4 mm 通孔。将三个样本保存到独立文件。

| 样本 | 制作方法 | 预期 |
| --- | --- | --- |
| `reference.SLDPRT` | 保存原始基准件。第一次将同一文件同时传给参考侧和学生侧。 | 两个差集为零，交集为原体积。 |
| `equivalent.SLDPRT` | 另存为独立文件并修改非几何自定义属性，确认 SHA-256 与基准不同。 | 不同文件，两个差集仍为零。 |
| `moved-hole.SLDPRT` | 用基准副本移动孔位，保留孔径和板厚，确保两个孔轮廓不重叠。 | 同时取得大于零的缺失和多余体积，各接近一个通孔的体积。 |

保留至少一份教师零件在 SOLIDWORKS 中打开。脚本执行三组比较，留存每组控制台日志和真实 JSON，并检查样本的 SHA-256、最后写入时间及可见 CAD 零件列表。可分别传入 `-Translated` 和 `-Rotated` 补测刚体对齐。平移样本应保留部分交集，例如向横向移动 5 mm。旋转样本应保持孔和板一同旋转。

```powershell
.\tests\Verify-GeometryProbe.ps1 -ExpectedYear 2025 `
    -Reference 'C:\Samples\reference.SLDPRT' -Equivalent 'C:\Samples\equivalent.SLDPRT' `
    -MovedHole 'C:\Samples\moved-hole.SLDPRT'
```

应分别保存 SOLIDWORKS 2025 和 2026 的证据。完整验收另含增孔、删孔、尺寸变化、相同曲面的不同剖分、配置不匹配、损坏、曲面体、多实体、钣金和批量资源检查。增孔应仅有缺失，删孔应仅有多余。发生拓扑或布尔错误时记录原始返回并保留待复核。DPI 与相机交互测试随阶段 C 的独立查看器进行。

## 当前验证记录

| 检查 | 结果 |
| --- | --- |
| Windows x64 / .NET Framework 4.8 Release | Linux 交叉构建通过，0 警告和 0 错误。 |
| `CloudChecks` | 47 项通过，含差集空结果、体积守恒、刚体矩阵、报告复用及不匹配的纯逻辑测试。 |
| `WorkflowChecks` | 97 项通过，涵盖原有评分、记录和导出流程，此处的 CAD 读取为模拟后端。 |
| CLI 失败路径 | Linux 上有效实验命令返回 3 且无结果文件。缺少网格输出路径、缺少复用所需的报告或同时选择互斥模式均返回 2。 |
| SOLIDWORKS 2026（Windows 11） | 用户真机完成四组对照。原始坐标取得非零差集，同文件同配置和不同文件同实体均确认零差异，同形异位样本对齐后确认重合。 |
| 剩余控制样本及 SOLIDWORKS 2025 | 已知移动孔位、非对称模型的独立旋转对照及 2025 运行均待验证。 |

### 2026-11-19 · Windows 11 / SOLIDWORKS 2026

根据用户提供的控制台日志，单文件探针 `0.4.1.0` 在 Windows 11 25H2 x64（build 26200.9457）连接 SOLIDWORKS 2026（修订版 `34.2.1`、Interop `34.2.1.2`）。两份源文件哈希不同，配置均为「默认」。各读到一个六面的实体、零曲面体、相同体积 `0.0130977 m³`。两份只读副本打开成功，无警告，重建状态正常。

| 运算 | 体积（m³） | SOLIDWORKS 返回 |
| --- | ---: | --- |
| 参考 − 学生 | 0.009823275 | 错误码 0，返回一个实体 |
| 学生 − 参考 | 0.009823275 | 错误码 0，返回一个实体 |
| 参考 ∩ 学生 | 0.003274425 | 错误码 0，返回一个实体 |

交集占参考体积 25%，两侧差集各占参考体积 75%，按当前公式得 `DifferenceRatio = 1.5`。两侧均满足体积守恒。日志记录两份临时文档已关闭，两份源文件哈希及最后写入时间保持一致。用户后续说明这两份样本形状一致、位置不同。用同一对样本完成的形状对齐结果见下文。教师已有文档保留待独立验证。

#### 同文件、同配置对照

用户在同一 Windows 和 CAD 版本上将参考文件同时传给两侧。探针分别打开并读取两个只读副本，SHA-256 和配置「默认」一致。两次减法都返回 `swBodyOperationNoError (0)` 与 `Null`。日志中的 `EmptyUnconfirmed` 是原始布尔返回的暂存状态。交集返回一个实体，体积 `0.013097699999999997 m³`，与两侧完整体积相等，内部校验据此确认缺失、多余各为 `0 m³`，最终差异率为 `0`。日志也记录两个临时文档均关闭，源文件哈希及最后写入时间一致。

#### 不同文件、同实体对照

同一真机的两份源文件 SHA-256 不同，均读取「默认」配置、一个六面实体及零曲面体。两次减法均返回 `swBodyOperationNoError (0)` 和 `Null`，交集返回一个实体，体积 `0.013097699999999997 m³`，与两侧原体积一致。最终缺失、多余和差异率均为 0。这组日志验证了原始坐标下可通过实体运算确认不同文件中的同一模型。两个临时文档均正常关闭，源文件哈希及写入时间检查通过。

#### 同形异位的形状对齐

用本节首组对照的同一对文件及配置运行 `--alignment coincident`。SOLIDWORKS 给出学生到参考的候选变换，矩阵包含旋转、约 `(-148.5, 0, 105) mm` 的平移分量和单位尺度。两次差集均返回错误码 0 与 `Null`，交集体积 `0.013097699999999997 m³` 覆盖两侧完整体积。最终状态为 `CoincidenceVerified`，缺失、多余及差异率均为 0。报告中的矩阵是一个经验证可使实体重合的变换。日志记录两份临时文档关闭且源文件哈希及最后写入时间一致。

| 同一对零件的比较方式 | 缺失（m³） | 多余（m³） | 交集占参考体积 | 差异率 |
| --- | ---: | ---: | ---: | ---: |
| 原始坐标 | 0.009823275 | 0.009823275 | 25% | 1.5 |
| 形状对齐 | 0 | 0 | 100% | 0 |

#### 增孔后的整件对齐限制与网格核查

用户在学生件副本上增孔后，用原参考件重新运行 `--alignment coincident`。SOLIDWORKS 正常读取两件，整件重合变换查找返回失败。探针停在 `AwaitingCoincidence`，保留待复核。这次运行在布尔差集开始之前结束。两件的真实总体积相差 `263.893782902 cm³`。刚体变换保留体积，本例的两个完整实体无法通过平移或旋转重合。

随后分别以每个模型对自身做布尔交集并导出了完整外形网格。两份报告均确认自对照零差异。网格核查确认参考件是约 `297 × 210 × 210 mm` 的无孔长方体。增孔版本外形尺寸相同，原始坐标轴次序和原点位置不同。圆柱网格两端在学生件的 `X≈0` 和 `X≈210 mm`，圆心约为 `Y=237 mm，Z=50 mm`，拟合直径约 `40 mm`。该圆柱的计算体积与 CAD 的减材体积一致。使用增孔前已验证的刚体变换核对网格，两件的外框及八个外角重合，孔轴转换为参考件的 Y 方向，横截面圆心约为 `X=88.5 mm，Z=55 mm`。

本例已验证单件网格导出以及有错位且有局部差异时的待复核行为。现已接入放置报告复用模式，仍需真机执行有孔件的双向差集。按已观察的几何预期，正确放置后参考侧缺失约 `263.894 cm³`、学生侧多余为零、差异率约为 `2.015%`。这些是预期值。已知移动孔位的两侧非零差集样本也待复验。

主要实现位于 `src/SWGrade.SolidWorks/GeometryProbe.cs`、`GeometryProbeReport.cs`、`CadAnalysis.cs` 和 `PartProbe.cs`。独立命令在 `src/SWGrade.Probe/GeometryProbeCommand.cs`。可按 `dotnet run --project tests/CloudChecks -c Release` 和 `dotnet run --project tests/WorkflowChecks -c Release` 重跑云端检查。
