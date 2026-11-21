# Windows 10 1903 机房试用

目标环境为 Windows 10 1903 x64（内部版本 18362）和 SOLIDWORKS 2025。试用端需要 .NET Framework 4.8，Windows 10 1903 已包含该版本。

## 单机验收

1. 完整解压到可写目录。使用同一个 Windows 账号、同样的权限启动 SOLIDWORKS 2025 和学生端。等 SOLIDWORKS 主界面和许可证就绪。
2. 双击 `Check-Lab1903.cmd`。它会在目录中生成 `SWGrade-Compatibility.txt`，记录 Windows 版本、.NET Framework、CAD 版本和连接结果。探针退出码 0 表示已连接到期望的 CAD。
3. 打开 `SWGrade.Student.exe`，标准件选 `reference\零件3.SLDPRT`，再把同一文件选为学生件。预期 100 分，抽壳后和平板型式后两处偏差均为 0。标准件的最后保存版本由 SOLIDWORKS 版本历史确认为 2025。
4. 用一份已知错误的学生件验证扣分。抽壳厚度从 1.0 mm 改为 1.2 mm 的样例，抽壳参数项应扣 8 分。如果几何结果无法核实，该项应标为待复核。
5. 用课上样件判分，导出 `.part3.json`，再发送到教师大屏并核对分数。连接失败时，在学生端点击「导出连接日志」，保存这次检测的步骤和原始错误。

## 构建试用版

分别构建学生端和探针，两个项目均使用 `Release|x64` 和 `-p:ClassroomLab1903=true`。将探针 EXE 及其配置与学生端放在同一目录，使用两者共用的试用版程序集。
