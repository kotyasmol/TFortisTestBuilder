@echo off
setlocal

if "%~1"=="" (
  echo ERROR: MAC address is not specified.
  exit /b 2
)

if "%~2"=="" (
  echo ERROR: boardversion is not specified.
  exit /b 2
)

if "%~3"=="" (
  echo ERROR: timestamp is not specified.
  exit /b 2
)

set "WINSCP_EXE="
set "ROOT_PASS=VR2MSISdpcb5"

if exist "%~dp0WinSCP\WinSCP.com" set "WINSCP_EXE=%~dp0WinSCP\WinSCP.com"

if not defined WINSCP_EXE (
  where WinSCP.com >nul 2>nul
  if not errorlevel 1 set "WINSCP_EXE=WinSCP.com"
)

if not defined WINSCP_EXE if exist "%ProgramFiles(x86)%\WinSCP\WinSCP.com" set "WINSCP_EXE=%ProgramFiles(x86)%\WinSCP\WinSCP.com"
if not defined WINSCP_EXE if exist "%ProgramFiles%\WinSCP\WinSCP.com" set "WINSCP_EXE=%ProgramFiles%\WinSCP\WinSCP.com"

if not defined WINSCP_EXE (
  echo ERROR: WinSCP.com was not found. Put the WinSCP folder next to TestBuilder.exe or install WinSCP.
  exit /b 1
)

echo MAC=%~1
echo BOARDVERSION=%~2
echo TIMESTAMP=%~3
echo Using WinSCP: %WINSCP_EXE%

"%WINSCP_EXE%" /command ^
  "open scp://root:@192.168.0.1/ -hostkey=*" ^
  "call fw_setenv ethaddr %~1" ^
  "call fw_setenv boardversion %~2" ^
  "call /etc/tf_clish/scripts/tools/macset.sh" ^
  "call ubus call tf_hwsys setParam '{""name"":""RTC_TIMESTAMP"",""value"":""%~3""}'" ^
  "call echo root:%ROOT_PASS% | chpasswd > /dev/null 2>&1" ^
  "exit"

set "RC=%ERRORLEVEL%"
if "%RC%"=="0" goto success

echo First connection attempt failed with code %RC%. Retrying with the configured device password.

"%WINSCP_EXE%" /command ^
  "open scp://root:%ROOT_PASS%@192.168.0.1/ -hostkey=*" ^
  "call fw_setenv ethaddr %~1" ^
  "call fw_setenv boardversion %~2" ^
  "call /etc/tf_clish/scripts/tools/macset.sh" ^
  "call ubus call tf_hwsys setParam '{""name"":""RTC_TIMESTAMP"",""value"":""%~3""}'" ^
  "exit"

set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo ERROR: WinSCP failed with code %RC%.
  exit /b %RC%
)

:success
echo SUCCESSFUL
exit /b 0
