@echo off
setlocal
chcp 65001 >nul
set "log=%~dp0SWGrade-Compatibility.txt"
"%~dp0SWGrade.Probe.exe" --expect 2025 > "%log%" 2>&1
set "result=%errorlevel%"
type "%log%"
echo(
echo Probe exit code: %result%
echo Report: "%log%"
pause
exit /b %result%
