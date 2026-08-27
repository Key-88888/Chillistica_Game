@echo off
setlocal
cd /d "%~dp0"
chcp 65001 >nul

rem Full removal helper for Chillistica_game. Works even if the app itself is
rem broken/won't start - it does not depend on Chillistica_game.exe at all.
rem
rem Needs administrator rights (to stop the elevated app/engine, and to remove
rem the WinDivert driver service / the old pre-0.5.0 Windows service). If not
rem already elevated, self-elevate via a UAC prompt and hand off to a second,
rem elevated copy of this same window.
rem
rem Arguments are forwarded to uninstall.ps1 (and THROUGH the UAC hand-off, which
rem would otherwise silently drop them): the in-app button passes -AssumeYes so
rem the user is not asked to confirm folder deletion twice.

net session >nul 2>&1
if not %errorlevel%==0 (
    echo Нужны права администратора для полного удаления.
    echo Сейчас появится запрос управления учётными записями ^(UAC^) - подтвердите его.
    echo.
    if "%~1"=="" (
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%~dp0'"
    ) else (
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%~dp0' -ArgumentList '%*'"
    )
    exit /b
)

echo ===============================================================
echo  Chillistica_game - удаление
echo ===============================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*

rem Release the working directory BEFORE pausing: while this window sits in the
rem program folder, that folder cannot be deleted, and the deferred cleanup
rem process would keep retrying against a directory we ourselves are holding.
cd /d "%SystemRoot%"

echo.
echo Готово. Закройте это окно.
echo.
pause
