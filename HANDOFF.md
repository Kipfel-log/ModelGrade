# SWGrade 桌面版 Codex 交接

当前版本为 **0.4.1-preview.1**。请把解压后的 `SWGrade` 目录作为工作区，先读本页和 [真机验收](docs/windows-acceptance.md)，再在本机继续。用户希望在 Windows 家用电脑上完成 SOLIDWORKS 适配与实际修改。常规排查可直接推进，确认环境和样本后先处理实际报错。

## 接手时的状态

- 云端已编译 Windows x64 / .NET Framework 4.8 版。单件分析、作业规则、标准件参数对照、队列、人工复核、SQLite、CSV、HTML 已接通。 [云端主流程验证](docs/teacher-cloud-verification.txt)
- 桌面使用 WPF UI 4.3.0 控件。主窗口采用独立 Windows 标题栏。侧栏可手动折叠，设置固定在底部。概览可立即重新检测安装、运行和连接，作业页改用标准配置列表、百分比容差和分值滑块。 [作业管理与窗口验收](docs/assignment-editor-acceptance.md) · [最新云端验证](docs/assignment-editor-cloud-verification.txt)
- 可识别 SOLIDWORKS 当前活动零件、列出打开的零件，从 CAD 选择标准件或导入学生件。分析使用已保存版本的只读临时副本。新建未保存的零件需先由用户保存，已有未保存改动会有提示。 [已打开零件验证](docs/open-parts-cloud-verification.txt)
- Windows 原生启动、Windows SQLite、SOLIDWORKS COM、真实模型读数及几何比较均需要真机验证。Linux 队列检查只使用明确标记的模拟 CAD 数据。Utilities 接口探针尚无实际比较结果，几何和其他缺少证据的分值显示「待复核」。

## 家用电脑先验什么

1. 记录 Windows 版本和 build。目标首验为 Windows 11 x64 + SOLIDWORKS 2026，以现场检测为准。用同一用户启动一个 CAD 实例，再打开一份已保存、无外部引用的脱敏 `.SLDPRT`。
2. 可先直接用包内 `bin` 的已编译程序。在包根目录的 PowerShell 中执行下面两条命令。需要核对同一模型在磁盘路径读取下是否一致时，再用 `tests\Verify-ModelRead.ps1`。

   ```powershell
   .\bin\SWGrade.Probe.exe --list-open --expect 2026
   .\bin\SWGrade.Probe.exe --active --expect 2026
   ```

3. 对照 SOLIDWORKS 的质量属性，核对配置、实体数、体积和面积。连续读两次，核查源文件 SHA-256、最后写入时间和原 CAD 文档仍保持打开。首次失败时保留错误码、诊断输出及实际 CAD 修订号，定位并改源码后再试。
4. 在 `bin\SWGrade.Desktop.exe` 中测试启动页、选择与互换参考件和当前件、异常列表、未保存状态、配置和三维视图，再按验收文档完成作业批改、队列和报表流程。界面截图使用包内 `tests\Capture-DesktopUi.ps1`。确认这条主路径后，再用两个脱敏样本验证实际几何比较与许可。Win10 22H2 + SW2025 和 Win11 + SW2025 分别记录结果。

## 修改与构建

- `SWGrade.sln` 为解决方案入口，`src/SWGrade.Desktop` 是 WPF，`src/SWGrade.SolidWorks` 是 COM 和只读工作副本，`src/SWGrade.Probe` 是诊断工具。Domain、Application、Infrastructure 分别负责规则、工作流和存储/导出。
- 安装 VS 2022 17.8+ 或 .NET 8 SDK 后，可用下面的命令从源码构建。构建时从 NuGet 还原 WPF UI、SQLite 与 .NET Framework 4.8 引用程序集。程序读取本机 SOLIDWORKS 安装中的官方 Interop。

   ```powershell
   dotnet build SWGrade.sln -c Release -p:Platform=x64
   dotnet run --project tests/CloudChecks -c Release
   dotnet run --project tests/WorkflowChecks -c Release
   ```

- 检查新产物时，桌面程序位于 `src\SWGrade.Desktop\bin\x64\Release\net48\`，探针位于相应的 Probe 目录。包根目录 `bin` 是随本次交接提供的已编译版本。历史发布包在 `dist`。
- 依现场核对 2025/2026 API 和许可。Windows 10 + SOLIDWORKS 2026 属于 CAD 官方支持边界。专用 STA 串行操作 COM，任何分析只读工作副本。保留原文件以及教师已打开的文档和进程。识别不到或未经验证的数据保留待复核，真实样本和学生作业留在本机。
