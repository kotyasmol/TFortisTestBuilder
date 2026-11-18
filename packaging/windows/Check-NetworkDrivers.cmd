@echo off
chcp 65001 >nul
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Check-NetworkDrivers.ps1"
set "TFORTIS_DRIVER_CHECK_EXIT=%ERRORLEVEL%"
pause
exit /b %TFORTIS_DRIVER_CHECK_EXIT%
