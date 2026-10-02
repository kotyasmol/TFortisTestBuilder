@echo off
cd /d "%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Check-Installation.ps1"
set "CHECK_RESULT=%ERRORLEVEL%"
echo.
echo Report: installation-check.txt
pause
exit /b %CHECK_RESULT%
