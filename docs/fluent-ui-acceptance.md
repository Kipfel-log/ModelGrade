# Fluent 界面与 Windows 验收

## 重构范围

- 引入 WPF UI 4.3.0。NuGet 为 .NET Framework 4.8 选择库的 net472 资产。图标、提示和外观开关使用该库。 [WPF UI NuGet 与目标框架](https://www.nuget.org/packages/WPF-UI/4.3.0)
- 独立的 Windows 标题栏左上显示图标和 SWGrade，右上提供最小化、最大化、还原和关闭。Windows 11 支持 Mica 时使用 DWM。效果未启用或设置失败时以完整的不透明调色板绘制窗口。设置可切换浅色、深色及 Mica，偏好保存于当前用户。
- 导航为概览、模型分析、作业管理、批量批改、批改记录和设置。概览显示真实检测状态及本地记录，完整诊断在设置折叠区。模型与批改读取仍用原有 STA 工作线程和服务。
- 展开侧栏 232 DIP，折叠后 60 DIP。顶部按钮手动切换，折叠后保留图标、工具提示和辅助技术名称，设置固定在底部。主标题 28 DIP，分区标题 19 DIP，正文基准 15 DIP。表格使用可换行的文字和滚动区域。启动窗口在当前屏幕工作区内适配大小。
- 窗口清单为 PerMonitorV2，旧配置回退 PerMonitor。启用布局取整和文字 Display 排版。页面淡入为 140 ms，跟随 Windows 动画偏好。

## 现场步骤

1. 在 Windows 上启动包中 `bin\SWGrade.Desktop.exe`。检查顶部仅有图标与 SWGrade，拖动、双击标题栏、最小化、最大化、还原和关闭可用。若系统支持贴靠布局，检查最大化按钮悬停行为。
2. 在「设置」关闭 Mica，检查浅色与深色。Windows 10 应显示不透明背景。Windows 11 可在开启 Mica 和关闭系统透明效果后分别验证。检查标题栏及各表格没有黑色或透明区域，文字仍清晰。
3. 重跑连接检测，核对未连接的提示及成功后的 CAD 版本。展开「详细诊断」检查 build、HRESULT 和框架信息。概览只显示简明状态。
4. 依序打开六个页面。在模型分析里选实际参考件与当前件，试交换、读取、导出和三维预览。在作业管理创建规则快照，在批量批改运行一份样本，在批改记录打开同一个结果。
5. 在 1080p 显示器逐项检查表格、按钮、侧栏以及短窗口滚动。改变系统缩放后重新定位窗口。在混合 DPI 双屏之间移动窗口，检查尺寸和文字是否随目标显示器更新。

| 操作系统 | 100% | 125% | 150% | 200% |
| --- | --- | --- | --- | --- |
| Windows 10 22H2，1080p，不透明 | 待现场 | 待现场 | 待现场 | 待现场 |
| Windows 11，Mica | 待现场 | 待现场 | 待现场 | 待现场 |
| Windows 11，不透明 | 待现场 | 待现场 | 待现场 | 待现场 |

## 获取实际窗口截图

在交接包根目录的 Windows PowerShell 中运行以下命令。将参数设为显示器实际缩放。

```powershell
.\tests\Capture-DesktopUi.ps1 -ExpectedScale 100
```

脚本启动真实程序，检查六项导航、窗口最小化和最大化能力及窗口 DPI，截图存于 `ui-evidence`。它会输出程序 PID。已经打开窗口时，可切换至需要记录的页面后指定原 PID。

```powershell
.\tests\Capture-DesktopUi.ps1 -ExpectedScale 150 -ExistingProcessId 1234 -CaptureLabel dark-solid
```

Linux 云端完成了 x64 构建、打包与回归检查。Windows 原生截图、交互、Mica 和各 DPI 档位仍待真机执行。

本次侧栏、概览检测和作业设置的详细场景见 [作业管理与窗口验收](assignment-editor-acceptance.md)。
