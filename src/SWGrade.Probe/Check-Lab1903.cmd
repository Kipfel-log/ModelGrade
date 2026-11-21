@echo off
setlocal
chcp 65001 >nul
set "log=%~dp0SWGrade-Compatibility.txt"
echo Running Windows, SOLIDWORKS and reference checks...
if not exist "%~dp0SWGrade.Probe.exe" goto incomplete
set "frameworkRelease="
for /f "tokens=3" %%A in ('reg.exe query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release /reg:64 2^>nul') do set /a "frameworkRelease=%%A"
if not defined frameworkRelease goto framework
if %frameworkRelease% LSS 528040 goto framework
"%~dp0SWGrade.Probe.exe" --lab-check > "%log%" 2>&1
set "result=%errorlevel%"
goto show

:incomplete
> "%log%" echo The diagnostic program is missing. Extract the full SWGrade ZIP to a local folder.
set "result=4"
goto show

:framework
> "%log%" echo .NET Framework 4.8 could not be verified. Ask school IT to check the 64-bit Windows installation and repair .NET Framework 4.8.
set "result=4"

:show
type "%log%"
echo(
echo Probe exit code: %result%
echo Report: "%log%"
pause
exit /b %result%
