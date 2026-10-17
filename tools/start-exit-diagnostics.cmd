@echo off
setlocal
set "PLAYER=%~dp0VirtualCharacterRender.exe"
if not "%~1"=="" set "PLAYER=%~1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0collect-exit-diagnostics.ps1" -PlayerPath "%PLAYER%"
echo.
echo The diagnostic ZIP is stored in %%LOCALAPPDATA%%\VirtualCharacterRender\ExitDiagnostics
pause
