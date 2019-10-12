@echo off
SET Platform=
powershell -ExecutionPolicy ByPass -NoProfile -command "& '%~dp0eng\common\Build.ps1' -restore %*"
exit /b %ErrorLevel%
