# 阶段 0 环境检查与真机验收

## 探针行为

- 从 Windows 桌面系统、注册表和运行环境报告版本、build、位数、.NET Framework Release、CAD 安装迹象和 COM 注册。注册项缺失只表示探针的安装路径检查未确认安装。
- 只附加当前用户、当前 Windows 会话中唯一的可见 SOLIDWORKS 实例。连接后用 API 核实 PID 和 RevisionNumber。多实例时提示手动保留目标实例，探针不会替用户关闭窗口。CAD 已启动时，探针应与其使用相同的权限级别。
- 从已运行程序的 `api\redist` 加载 Interop，打印程序集标识。在本机调用接口后再确认相应能力。
- 对源文件只使用读权限。验证 SHA-256 后创建随机命名的本地副本，在 CAD 中静默只读打开。发现已知外部文档依赖时拒绝分析。阶段 0 样本应为独立零件。捕获打开错误/警告，读取活动配置、重建状态、曲面与实体数、实体体积/面积、特征类型。源文件的 SHA-256 和最后写入时间在读后再次校验。
- 无材料、多实体、质量覆盖或返回值异常时，相应质量指标标为待复核。重建状态是读取时 CAD 报告的状态。探针未主动重建。
- 只关闭路径、对象和标题匹配的临时文档。若关闭或清理失败，控制台会报告工作路径。确认临时 CAD 文档关闭后，可手动清理相应的 `%LOCALAPPDATA%\SWGrade\workspace\<ID>` 目录。
- Utilities 插件对象和几何工具可单独失败，零件基本读取仍可继续。取得 `ICompareGeometry` 后仅核对 `CompareGeometry3` 接口。真实报告、比较许可及数值留待双样本验证。

## 现场步骤

在 PowerShell 进入探针目录后执行。若需记录中文输出，可先设置 `[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)`。

```powershell
.\SWGrade.Probe.exe --help
.\SWGrade.Probe.exe
.\SWGrade.Probe.exe --expect 2026 --model 'C:\Samples\part.SLDPRT'
$LASTEXITCODE
```

退出码 `0` 表示所请求的连接诊断或模型读取完成。可选材料、质量和 Utilities 有各自的结果。退出码 `2` 表示参数错误，`3` 表示平台不可用，`4` 表示诊断或模型读取未完成。需要完整验收时，记录 Utilities 的单独输出及 CAD 内显示的许可/插件启用状态。

## 第一台电脑的验收清单

1. CAD 未启动时启动 Probe。确认程序打印 Windows 具体 build、运行时和安装/COM 信息，并返回明确诊断，输出中没有模型读数。
2. CAD 启动并进入主界面后探测空参数模式。核对 PID、Interop 和 RevisionNumber，记录 Utilities 结果。若 CAD 提示许可错误，记录 CAD 提示与 Probe 输出。
3. 使用真实独立样本执行两次相同的模型命令。与 SOLIDWORKS「评估 → 质量属性」逐项核对体积、面积、材料和可用的质量；核对 CAD 配置和特征类型。分别记录两次读数以及打开状态。
4. 核对源模型的 SHA-256 和 `LastWriteTimeUtc`，确认临时文档被关闭，教师原先打开的文档与 CAD 实例仍在。可分别用 `Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\Samples\part.SLDPRT'` 和 `(Get-Item -LiteralPath 'C:\Samples\part.SLDPRT').LastWriteTimeUtc` 获取源文件证据。
5. 使用无材料零件、错误配置名以及损坏样本分别验证“待复核”与失败输出。未来版本样本只用于验证诊断，使用对应 CAD 版本的零件完成正常读取验收。

## Windows 10 22H2 x64 + SOLIDWORKS 2025 学校待验清单

- [ ] 解压同一发布包，确认 build 19045、x64、.NET Framework 4.8 或更高。
- [ ] 用 `--expect 2025` 核对 RevisionNumber 主版本 33、Interop、PID 和具体服务包。
- [ ] 使用 2025 可读的脱敏样本执行上方读两次及文件保护检查。
- [ ] 记录 Utilities 接口状态及实际许可。若无插件，确认模型读取仍可继续。
- [ ] 确认学校普通账户下可启动，临时目录可写，诊断信息完整。

| 环境 | 当前状态 |
| --- | --- |
| Windows 11 x64 + SOLIDWORKS 2026 | 待用户本机首验 |
| Windows 11 x64 + SOLIDWORKS 2025 | 尚未实测 |
| Windows 10 22H2 x64 + SOLIDWORKS 2025 | 设计兼容，尚待学校真机验收 |
| Windows 10/11 x64、无 SOLIDWORKS | 启动与诊断待实测 |
| Windows 10 x64 + SOLIDWORKS 2026 | 官方支持范围外，代码诊断分支待实测 |

官方的 Windows 10 客户端支持截止至 SOLIDWORKS 2025 SP5，官方另有 Windows 版本生命周期与系统版本类型限制。 [SOLIDWORKS 系统要求](https://www.solidworks.com/support/system-requirements)

先用至少一个正式目标组合完成真实读取，然后按 [阶段 1 清单](user-guide.md) 验收云端已编译的桌面程序。回到电脑后，需要现有 Windows / SOLIDWORKS、可用许可和一份独立脱敏零件。
