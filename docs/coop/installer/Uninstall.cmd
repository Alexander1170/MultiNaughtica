@echo off
rem Double-click this file to remove MultiNaughtica from this PC. Your worlds and settings are kept unless you say otherwise.
rem It runs from a copy in the temp folder, because it deletes the folder it lives in.
if /i "%~dp0"=="%TEMP%\" goto run
copy /y "%~f0" "%TEMP%\MultiNaughtica-Uninstall.cmd" >nul
copy /y "%~dp0uninstall.ps1" "%TEMP%\MultiNaughtica-uninstall.ps1" >nul
set "NITROXCOOP_INSTALLDIR=%~dp0."
(call "%TEMP%\MultiNaughtica-Uninstall.cmd" %*) & exit /b %errorlevel%

:run
title MultiNaughtica uninstaller
echo.
echo  MultiNaughtica uninstaller
echo  ------------------------
echo.
if defined NITROXCOOP_INSTALLDIR (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\MultiNaughtica-uninstall.ps1" -InstallDir "%NITROXCOOP_INSTALLDIR%" %*
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\MultiNaughtica-uninstall.ps1" %*
)
set "result=%errorlevel%"
echo.
pause
exit /b %result%
