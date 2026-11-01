@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul
set "PROJECT_ROOT=%~dp0."
set "LAUNCHER=%~dp0tools\GitHubBackupTool\start_wheelbarrows_github.ps1"
title LERT WHEELBARROWS - GITHUB PUBLISH
if not exist "%LAUNCHER%" (
    echo ERROR: Extract the complete ZIP before launching this BAT.
    pause
    exit /b 2
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%LAUNCHER%" -ProjectRoot "%PROJECT_ROOT%"
set "PUBLISH_EXIT=%ERRORLEVEL%"
if not "%PUBLISH_EXIT%"=="0" (
    echo.
    echo Startup failed. Read the message above or tools\GitHubBackupTool\startup_error.log.
    pause
)
exit /b %PUBLISH_EXIT%
