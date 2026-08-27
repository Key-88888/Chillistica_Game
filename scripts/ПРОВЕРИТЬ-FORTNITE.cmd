@echo off
rem ---------------------------------------------------------------------------
rem  ASCII ONLY. Do not put Cyrillic text in this file.
rem
rem  cmd.exe reads .cmd files using the system OEM codepage, not UTF-8. Russian
rem  text saved as UTF-8 therefore decodes into garbage, lines break apart, and
rem  cmd starts executing fragments as commands - which is how an earlier version
rem  of this file ended up relaunching itself in a UAC loop.
rem
rem  All user-facing Russian text lives in try-strategy.ps1, where PowerShell
rem  reads UTF-8 (with BOM) correctly. This file only elevates and hands over.
rem ---------------------------------------------------------------------------

setlocal
cd /d "%~dp0"

rem Already elevated? Then run the menu. Otherwise elevate ONCE and stop this
rem instance - no goto, no loop, so a failed elevation cannot repeat forever.
net session >nul 2>&1
if errorlevel 1 goto elevate

if not exist "%~dp0try-strategy.ps1" goto missing

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0try-strategy.ps1" -App fortnite -Menu
exit /b

:elevate
echo Administrator rights are required (the engine needs the WinDivert driver).
echo A UAC prompt will appear - please confirm it.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%~dp0'"
exit /b

:missing
echo try-strategy.ps1 was not found next to this file.
echo Unpack the whole archive, not single files.
echo.
pause
exit /b 1
