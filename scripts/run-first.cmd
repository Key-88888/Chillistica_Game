@echo off
rem ---------------------------------------------------------------------------
rem  ASCII ONLY. Do not put Cyrillic text in this file.
rem
rem  cmd.exe reads .cmd files using the system OEM codepage, not UTF-8, and chcp
rem  does not change how the file itself is parsed. Russian text stored as UTF-8
rem  decodes into garbage here: lines split apart and cmd runs the fragments as
rem  commands. This file shipped that way in v0.5.0, so on some machines the
rem  runtime-missing path printed junk instead of instructions.
rem
rem  Russian text belongs in the .ps1 files, where PowerShell reads UTF-8 (with
rem  BOM) correctly, or in the app's own UI.
rem ---------------------------------------------------------------------------

setlocal
cd /d "%~dp0"

rem The self-contained (-standalone) archive needs no runtime; this check is
rem harmless there and simply passes.
set "DOTNET_EXE="
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET_EXE for /f "delims=" %%I in ('"%SystemRoot%\System32\where.exe" dotnet.exe 2^>nul') do if not defined DOTNET_EXE set "DOTNET_EXE=%%I"
if not defined DOTNET_EXE goto runtime_missing

"%DOTNET_EXE%" --list-runtimes 2>nul | "%SystemRoot%\System32\findstr.exe" /c:"Microsoft.WindowsDesktop.App 8." >nul
if not errorlevel 1 goto launch

:runtime_missing
echo .NET 8 Desktop Runtime is required. Trying to install it automatically...
if not exist "%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe" goto manual_install

"%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe" install Microsoft.DotNet.DesktopRuntime.8 --accept-package-agreements --accept-source-agreements
if errorlevel 1 goto manual_install
goto launch

:manual_install
echo.
echo Automatic install failed. The .NET 8 Desktop Runtime download page will open.
echo Install the runtime, then run run-first.cmd again.
echo.
start "" "https://dotnet.microsoft.com/download/dotnet/8.0/runtime"
pause
exit /b 1

:launch
start "" "%~dp0Chillistica_game.exe"
exit /b 0
