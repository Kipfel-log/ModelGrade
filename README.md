# SWGrade

## 零件 3 课堂专项版

本分支提供两个独立的 Windows x64 程序，均需 .NET Framework 4.8 或更新版本。

学生端离线包附带微软的 .NET Framework 4.8 运行时。在缺少运行时的电脑上，完整解压后运行 `prerequisites\NDP48-x86-x64-AllOS-ENU.exe`，安装需要管理员权限。若安装程序提示重新启动，请重启后再打开学生端。学生端会分别报告 Windows 版本、位数和 .NET Framework 检测结果。[微软离线安装程序](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/thank-you/net48-offline-installer)

- **学生端 `SWGrade.Student.exe`**。启动 SOLIDWORKS，选择教师指定的 `.SLDPRT` 标准件和已保存的学生件。标准件可以换成尺寸不同的版本，须能读取本题评分步骤并重建抽壳态和展开态。结果列出基础建模 40 分、钣金 60 分的得分依据，以及两个阶段的原始 CAD 坐标实体差异。
- **教师端 `SWGrade.Teacher.exe`**。启动即可显示课堂码和局域网地址，无需安装 SOLIDWORKS。统计页显示提交人数、平均分、最高分、待复核人数、分数段柱状图和各步骤扣分人数。点击「提交明细」查看单个学号的结果，点击「全屏」投屏，按 Esc 退出全屏。
- 学生可发现局域网内的教师端，也可手动填写教师端 IPv4 地址和课堂码。两种方式均可发送本次成绩或上传已导出的 `.part3.json`。教室网络不可用时，教师端可直接批量导入文件。接收使用 TCP 和 UDP 47936。
- 同一学号保留最新成绩。分布、当前平均分和当前最高分按所有提交的已得分计算。分布图用黄色标出待复核成绩。大屏数据保存在本次会话内，请保留导出的成绩文件。

构建 `SWGrade.sln` 的 `Release|x64` 后，从各自项目的输出目录启动程序，分发时保留 EXE 旁的依赖文件。

Windows 10 1903 x64 + SOLIDWORKS 2025 另有机房试用构建。[试用与验收说明](docs/classroom-lab-1903.md)

## 通用版使用说明

以下内容对应主分支的通用版。

面向教师的 SOLIDWORKS 单零件作业分析与批改程序。0.4.1-preview.1 已在 Linux 云端交叉编译为 Windows x64 / .NET Framework 4.8。桌面和命令行共用真实 CAD 读取代码。Windows 11 + SOLIDWORKS 2026 的探针已验证原始坐标差集、不同文件同实体以及形状对齐后的重合。桌面、Windows 版 SQLite 和完整组合验收仍待现场执行。

## 在 Windows 上运行

解压便携包，双击 `bin\SWGrade.Desktop.exe`。请保留 `bin` 下的配置、托管程序集和原生 SQLite DLL。运行需要 .NET Framework 4.8 或以上。

1. 启动 SOLIDWORKS，等待主界面和许可就绪。在「概览」查看自动检测结果或点击「立即检测」，然后进入「模型分析」。从 CAD 或磁盘选择「当前件」，也可同时选择「参考件」并互换。
2. 在「作业管理」新建作业，选择标准件，在下拉框确认用于批改的配置，用百分比档位和权重滑块设置规则后保存快照。用「标准件预检」核对参考数据。
3. 在「批量批改」导入 CAD 中打开的零件、文件或班级文件夹，开始执行队列。选择一份提交可查看逐项对照、证据、执行记录和复核历史。
4. 对需复核的项目由教师确认终评。导出班级 CSV 或单件 HTML。分析结果页可展开配置列表、查看需要关注的事项、试算评分并导出 JSON。设置页开启开发模式后可看详细诊断。

符合条件的单实体零件使用 SOLIDWORKS 原生实体差集。三维视图以红色显示多余、蓝色显示缺少、灰色显示交集，差集体积与几何评分共用同一份证据。原始 CAD 坐标是默认策略，可显式选择形状刚体对齐。无法验证实体或配准时，几何分留待复核。偏差云图支持两侧曲面采样、以 mm 为单位的可调色带和点取局部估计。 [几何与三维验收](docs/geometry-stage-c.md)

## 命令行与验收

在便携包 `bin` 目录用 PowerShell 运行。

```powershell
.\SWGrade.Probe.exe
.\SWGrade.Probe.exe --expect 2026 --model 'C:\Samples\part.SLDPRT'
```

可用 `--list-open` 列出当前打开的零件，或用 `--active` 读取活动零件的已保存版本。

开发中的 `--geometry` 命令使用 SOLIDWORKS 对已保存的单实体零件执行双向实体差集实验。局部修改后可用 `--transform-report` 复用已验证的放置方式。SOLIDWORKS 2026 已通过移孔、非对称旋转和增孔后的放置复用对照。运行命令和结果见 [阶段 A 实体几何比较](docs/geometry-stage-a.md)。

在 2025 实例上使用 `--expect 2025`，可加 `--config '配置名'` 和 `--json 'part-analysis.json'`。从便携包根目录可运行 `tests\Verify-ModelRead.ps1` 进行真实样本的重复读值及源文件校验。

| 运行组合 | 状态 |
| --- | --- |
| Windows 11 x64 + SOLIDWORKS 2026 | 几何探针控制样本通过，完整批改流程待验收 |
| Windows 10 22H2 x64 + SOLIDWORKS 2025 | 设计兼容，待学校机房真机验收 |
| Windows 11 x64 + SOLIDWORKS 2025 | 设计兼容，尚未实测 |
| Windows 10/11 x64，未启动或未安装 SOLIDWORKS | 有明确诊断路径，待 Windows 现场验证 |
| Windows 10 + SOLIDWORKS 2026 | CAD 官方支持范围外，禁止批改 |

## 从源码构建

用 Visual Studio 2022 17.8+ 打开 `SWGrade.sln`，选择 `Release | x64`。也可使用 .NET 8 SDK。

```text
dotnet build SWGrade.sln -c Release -p:Platform=x64
dotnet run --project tests/CloudChecks -c Release
dotnet run --project tests/WorkflowChecks -c Release
```

桌面产物位于 `src/SWGrade.Desktop/bin/x64/Release/net48/`，探针在对应的 Probe 目录。NuGet 用于构建时获取 .NET Framework 引用程序集、WPF UI 和 SQLite 依赖。正式包使用 CAD 安装目录中的 Interop，无 CAD 样本或模拟数据。模拟读取仅在 WorkflowChecks 中使用。

[教师使用说明](docs/user-guide.md) · [规则说明](docs/grading-rules.md) · [真机验收流程](docs/windows-acceptance.md) · [API 核对](docs/api-compatibility.md) · [作业管理与窗口验收](docs/assignment-editor-acceptance.md) · [最新云端验证](docs/assignment-editor-cloud-verification.txt) · [批改流程验证](docs/teacher-cloud-verification.txt)
