# SWGrade

面向教师的 SOLIDWORKS 单零件作业分析与批改程序。0.4.1-preview.1已在 Linux 云端交叉编译为 Windows x64 / .NET Framework 4.8。桌面和命令行共用真实 CAD 读取代码。Windows 原生运行、CAD 读数、Windows 版 SQLite 和三种目标组合均待现场验收。

## 在 Windows 上运行

解压便携包，双击 `bin\SWGrade.Desktop.exe`。请保留 `bin` 下的配置、托管程序集和原生 SQLite DLL。运行需要 .NET Framework 4.8 或以上。

1. 启动 SOLIDWORKS，等待主界面和许可就绪。在「概览」查看自动检测结果或点击「立即检测」，然后进入「模型分析」。从 CAD 或磁盘选择「当前件」，也可同时选择「参考件」并互换。
2. 在「作业管理」新建作业，选择标准件，在下拉框确认用于批改的配置，用百分比档位和权重滑块设置规则后保存快照。用「标准件预检」核对参考数据。
3. 在「批量批改」导入 CAD 中打开的零件、文件或班级文件夹，开始执行队列。选择一份提交可查看逐项对照、证据、执行记录和复核历史。
4. 对需复核的项目由教师确认终评。导出班级 CSV 或单件 HTML。分析结果页可展开配置列表、查看需要关注的事项、试算评分并导出 JSON。设置页开启开发模式后可看详细诊断。

三维叠加使用同一零件坐标系，灰色表示重叠，蓝色和橙色分别表示当前件与参考件显示网格的差异。可仅显示差异，或单件旋转、平移和缩放。着色用于定位复核区域。几何自动确认目前限于两次独立只读且验证为同一已保存文件内容、同一配置的合格单实体。其他模型的几何分值待复核。

## 命令行与验收

在便携包 `bin` 目录用 PowerShell 运行。

```powershell
.\SWGrade.Probe.exe
.\SWGrade.Probe.exe --expect 2026 --model 'C:\Samples\part.SLDPRT'
```

可用 `--list-open` 列出当前打开的零件，或用 `--active` 读取活动零件的已保存版本。

在 2025 实例上使用 `--expect 2025`，可加 `--config '配置名'` 和 `--json 'part-analysis.json'`。从便携包根目录可运行 `tests\Verify-ModelRead.ps1` 进行真实样本的重复读值及源文件校验。

| 运行组合 | 状态 |
| --- | --- |
| Windows 11 x64 + SOLIDWORKS 2026 | 等待用户本机首验 |
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
