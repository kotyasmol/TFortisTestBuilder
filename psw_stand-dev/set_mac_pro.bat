@echo off

REM ----------- Ищем WinSCP.com -----------------
set WINSCP_EXE=
REM Проверяем текущую папку
if exist "%~dp0WinSCP\WinSCP.com" set WINSCP_EXE=%~dp0WinSCP\WinSCP.com
REM Проверяем PATH
if not defined WINSCP_EXE (
    for %%i in (WinSCP.com) do (
        where %%i >nul 2>nul
        if %ERRORLEVEL%==0 set WINSCP_EXE=%%i
    )
)
REM Проверяем стандартную установку (Program Files)
if not defined WINSCP_EXE (
    if exist "C:\Program Files (x86)\WinSCP\WinSCP.com" set WINSCP_EXE="C:\Program Files (x86)\WinSCP\WinSCP.com"
    if exist "C:\Program Files\WinSCP\WinSCP.com" set WINSCP_EXE="C:\Program Files\WinSCP\WinSCP.com"
)

if not defined WINSCP_EXE (
    echo ERROR: WinSCP.com не найден. Установите WinSCP.
    pause
    exit /b 1
)

echo Using WinSCP: %WINSCP_EXE%
REM ----------------------------------------------

"%WINSCP_EXE%" /command ^
  "open scp://root:root@192.168.0.1/ -hostkey=*" ^
  "call fw_setenv ethaddr %1" ^
  "call fw_setenv boardversion %2" ^
  "call /etc/tf_clish/scripts/tools/macset.sh" ^
  "call ubus call tf_hwsys setParam '{""name"":""RTC_TIMESTAMP"",""value"":""%3""}'" ^
  "call echo root:VR2MSISdpcb5 | chpasswd > /dev/null 2>&1" ^
  "exit"
  
REM Проверяем код возврата WinSCP
set "RC=%ERRORLEVEL%"

if "%RC%"=="0" (
  echo.
  echo ================================
  echo SUCCESSFUL
  echo ================================
) else (
  echo.
  echo =======================================
  echo ERROR %RC%.
  echo =======================================
)

