@echo off
rem ---------------------------------------------------------------------------
rem  ASCII ONLY. Do not put Cyrillic text in this file.
rem
rem  cmd.exe reads .cmd files using the system OEM codepage, not UTF-8, and chcp
rem  does not change how the already-open file is parsed. Russian text stored as
rem  UTF-8 therefore decodes into garbage: lines split apart and cmd executes the
rem  fragments as commands (a Russian word ends up quoted as "is not recognized
rem  external command"). All Russian output lives in uninstall.ps1 instead, where
rem  PowerShell reads UTF-8 (with BOM) correctly.
rem
rem  Full removal helper. Works even if the app itself is broken and will not
rem  start - it does not depend on Chillistica_game.exe at all.
rem
rem  Needs administrator rights: to stop the elevated app/engine, and to remove
rem  the WinDivert driver service and the old pre-0.5.0 Windows service.
rem
rem  Arguments are forwarded to uninstall.ps1 AND through the UAC hand-off, which
rem  would otherwise silently drop them: the in-app button passes -AssumeYes so
rem  folder removal is not confirmed twice.
rem ---------------------------------------------------------------------------

setlocal
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 goto elevate

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*

rem Release the working directory BEFORE pausing: while this window sits inside
rem the program folder, that folder cannot be deleted, and the deferred cleanup
rem process would keep retrying against a directory we ourselves are holding.
cd /d "%SystemRoot%"

echo.
pause
exit /b

:elevate
echo Administrator rights are required for full removal.
echo A UAC prompt will appear - please confirm it.
echo.
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%~dp0'"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%~dp0' -ArgumentList '%*'"
)
exit /b
