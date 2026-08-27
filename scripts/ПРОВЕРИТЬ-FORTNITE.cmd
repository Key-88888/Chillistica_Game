@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
chcp 65001 >nul
title Проверка Fortnite - подбор стратегии обхода

rem Обёртка для ручного подбора стратегии: двойной щелчок, без единой команды.
rem Существует потому, что копирование команд в PowerShell раз за разом ломалось
rem об относительные пути - скрипт запускался не из своей папки и не находился.
rem Здесь путь всегда %~dp0, то есть папка самого файла.

net session >nul 2>&1
if not %errorlevel%==0 (
    echo Нужны права администратора - движку нужен драйвер WinDivert.
    echo Сейчас появится запрос UAC, подтвердите его.
    echo.
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%~dp0'"
    exit /b
)

if not exist "%~dp0try-strategy.ps1" (
    echo НЕ НАЙДЕН try-strategy.ps1 рядом с этим файлом.
    echo Распакуйте архив целиком, а не отдельные файлы.
    echo.
    pause
    exit /b 1
)

:menu
cls
echo ===============================================================
echo   Подбор стратегии обхода для Fortnite
echo ===============================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0try-strategy.ps1" -App fortnite -List
echo.
echo ===============================================================
echo  Введите номер стратегии (0, 1, 2 ...) и нажмите Enter.
echo  Начинайте с 0 - он покрывает всё сразу.
echo  Пустой ввод или q - выход.
echo ===============================================================
echo.

set "choice="
set /p "choice=Номер: "

if "!choice!"=="" goto :eof
if /i "!choice!"=="q" goto :eof

echo.
echo Включаю стратегию !choice! на 10 минут.
echo ОТКРОЙТЕ EPIC GAMES LAUNCHER И ПРОВЕРЬТЕ ИГРУ.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0try-strategy.ps1" -App fortnite -Index !choice! -Minutes 10

echo.
echo ===============================================================
echo  Заработало - запомните номер !choice! и сообщите его.
echo  Не заработало - попробуйте следующий номер.
echo ===============================================================
echo.
pause
goto menu
