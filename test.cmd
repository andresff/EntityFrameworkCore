@echo off
SET Platform=
powershell -ExecutionPolicy ByPass -NoProfile -command "& '%~dp0eng\common\Build.ps1' -test %*"
exit /b %ErrorLevel%
