@echo off
chcp 65001 >NUL
setlocal

echo ================================================
echo   RepoCheatMenu - сборка одним кликом
echo ================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\build-all.ps1"
set EXITCODE=%ERRORLEVEL%

echo.
pause
exit /b %EXITCODE%
