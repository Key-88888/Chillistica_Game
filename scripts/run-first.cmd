@echo off
setlocal
cd /d "%~dp0"
chcp 65001 >nul

rem The self-contained (-standalone) archive needs no runtime; this check is harmless there and simply passes.
set "DOTNET_EXE="
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET_EXE for /f "delims=" %%I in ('"%SystemRoot%\System32\where.exe" dotnet.exe 2^>nul') do if not defined DOTNET_EXE set "DOTNET_EXE=%%I"
if not defined DOTNET_EXE goto runtime_missing

"%DOTNET_EXE%" --list-runtimes 2>nul | "%SystemRoot%\System32\findstr.exe" /c:"Microsoft.WindowsDesktop.App 8." >nul
if not errorlevel 1 goto launch

:runtime_missing
echo Для запуска требуется .NET 8 Desktop Runtime. Пытаемся установить его автоматически...
if not exist "%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe" goto manual_install

"%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe" install Microsoft.DotNet.DesktopRuntime.8 --accept-package-agreements --accept-source-agreements
if errorlevel 1 goto manual_install
goto launch

:manual_install
echo Автоматическая установка не удалась. Откроется страница загрузки .NET 8 Desktop Runtime.
echo Установите среду выполнения, затем снова запустите run-first.cmd.
start "" "https://dotnet.microsoft.com/ru-ru/download/dotnet/8.0/runtime"
pause
exit /b 1

:launch
start "" "%~dp0Chillistica_game.exe"
exit /b 0
