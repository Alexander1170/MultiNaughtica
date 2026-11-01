@echo off
rem Double-click this file. It starts install.ps1 (next to it) with the PowerShell rules switched off for this one run, so nothing needs to be set up first.
setlocal
title Nitrox Co-op installer
echo.
echo  Nitrox Co-op installer
echo  ----------------------
echo  This installs the program on this PC. It may ask Windows for permission once or twice.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set "result=%errorlevel%"
echo.
if not "%result%"=="0" (
    echo  The installer stopped. The message above says why. If you need help, send the file
    echo  %TEMP%\NitroxCoop-install.log to the person who gave you this zip.
    echo.
)
pause
exit /b %result%
