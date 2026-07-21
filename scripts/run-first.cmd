@echo off
setlocal
cd /d "%~dp0"
chcp 65001 >nul

rem The self-contained (-standalone) archive needs no runtime; this check is harmless there and simply passes.
where dotnet >nul 2>nul
if errorlevel 1 goto runtime_missing

dotnet --list-runtimes 2>nul | findstr /c:"Microsoft.WindowsDesktop.App 8." >nul
if not errorlevel 1 goto launch

:runtime_missing
echo Для запуска требуется .NET 8 Desktop Runtime. Пытаемся установить его автоматически...
where winget >nul 2>nul
if errorlevel 1 goto manual_install

winget install Microsoft.DotNet.DesktopRuntime.8 --accept-package-agreements --accept-source-agreements
if errorlevel 1 goto manual_install
goto launch

:manual_install
echo Автоматическая установка не удалась. Откроется страница загрузки .NET 8 Desktop Runtime.
echo Установите среду выполнения, затем снова запустите run-first.cmd.
start "" "https://dotnet.microsoft.com/ru-ru/download/dotnet/8.0/runtime"
pause
exit /b 1

:launch
start "" "Chillistica_game.exe"
exit /b 0
