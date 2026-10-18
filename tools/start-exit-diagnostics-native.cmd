@echo off
setlocal
set "PLAYER=%~dp0VirtualCharacterRender.exe"
if not "%~1"=="" set "PLAYER=%~1"
echo Native shutdown diagnosis requested.
echo The recorder will create a thread-stack minidump if shutdown lasts over 5 seconds.
echo Native minidumps can contain private paths and process memory.
echo Do not share the minidump publicly.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0collect-exit-diagnostics.ps1" -PlayerPath "%PLAYER%" -NativeDump -DumpDelaySeconds 5
echo.
echo Diagnostic ZIP: %%LOCALAPPDATA%%\VirtualCharacterRender\ExitDiagnostics
pause
