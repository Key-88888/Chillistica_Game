<#
    uninstall.ps1 - full removal helper for Chillistica_game (v0.5.0+, service-less build).

    Ships INSIDE the release archive, next to Chillistica_game.exe (build-release.ps1
    copies it there, same way it copies run-first.cmd). Run it via uninstall.cmd, which
    self-elevates first - running this .ps1 directly without admin rights will fail the
    driver-service, old-service and process-stop steps (see the warning below), but every
    step here is independently wrapped, so a partial failure never aborts the rest.

    What this does:
      1. Stops any running Chillistica_game.exe (this app, all instances).
      2. Stops any winws.exe launched from THIS install's Engine\winws2\bin\ (matched by
         full path, so a user's own separate zapret install is left alone).
      3. Stops and deletes the WinDivert kernel driver service, and removes its .sys
         file(s) from System32\drivers if still present.
      4. Stops and deletes the old Chillistica_game.Service Windows service - a leftover
         from versions before 0.5.0, which used a background service instead of this
         direct-launch model.
      5. Deletes logs / settings / update-staging under %LOCALAPPDATA%\Chillistica_game
         and %APPDATA%\Chillistica_game.

    What this does NOT do: delete the folder this script lives in. A running script
    cannot reliably delete its own containing directory; the console output tells the
    user it is now safe to delete it by hand.

    Safe to run more than once - every step checks state first and no-ops if there is
    nothing to do.
#>

param(
    # Passed by the in-app "Удалить программу" button, which has already shown
    # its own confirmation dialog — do not ask a second time.
    [switch]$AssumeYes,

    # Папка установки. Нужна, только если скрипт запускают НЕ из неё — например,
    # когда программу удаляют на чужой машине, где архива уже нет. Без него
    # скрипт ищет установку сам.
    [string]$Path = ""
)

$ErrorActionPreference = "Continue"

function Test-LooksLikeInstall {
    param([string]$Dir)

    if ([string]::IsNullOrWhiteSpace($Dir) -or -not (Test-Path -LiteralPath $Dir -PathType Container)) {
        return $false
    }

    # Раскладка 0.5.0+ (распаковал-и-запустил): всё в корне.
    if ((Test-Path -LiteralPath (Join-Path $Dir "Chillistica_game.exe")) -or
        (Test-Path -LiteralPath (Join-Path $Dir "Engine\winws2"))) {
        return $true
    }

    # Раскладка версий до 0.5.0: app\ + service\, движок внутри service\Engine.
    # Именно она стоит на машинах, которые обновлялись давно, и именно её надо
    # уметь найти при удалении на чужом компьютере.
    if ((Test-Path -LiteralPath (Join-Path $Dir "app\Chillistica_game.App.exe")) -or
        (Test-Path -LiteralPath (Join-Path $Dir "service\Chillistica_game.Service.exe"))) {
        return $true
    }

    return $false
}

function Find-Installation {
    <#
        Находит папку программы, когда скрипт запущен не из неё. Это основной
        сценарий удаления на чужой машине: архива с uninstall.cmd там нет,
        человек запускает один скачанный скрипт.

        Порядок источников — от самого достоверного к догадкам.
    #>

    # 1. Запущенные процессы: самый надёжный источник, путь берётся у самой ОС.
    foreach ($name in @("Chillistica_game", "winws")) {
        foreach ($p in (Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            $exePath = $null
            try { $exePath = $p.Path } catch { $exePath = $null }

            if ($exePath) {
                $dir = Split-Path $exePath -Parent
                # winws лежит в Engine\winws2\bin — поднимаемся к корню программы.
                if ($dir -match '\Engine\winws2\bin$') {
                    $dir = Split-Path (Split-Path (Split-Path $dir -Parent) -Parent) -Parent
                }
                if (Test-LooksLikeInstall $dir) { return $dir }
            }
        }
    }

    # 2. Старая служба версии до 0.5.0 знает свой путь.
    $svc = Get-CimInstance Win32_Service -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "Chillistica_game.Service" } | Select-Object -First 1

    if ($svc -and $svc.PathName) {
        $exePath = ($svc.PathName -replace '^"([^"]+)".*$', '$1').Trim('"')
        $dir = Split-Path $exePath -Parent
        # Служба ставилась в <корень>\Service\
        foreach ($cand in @($dir, (Split-Path $dir -Parent))) {
            if (Test-LooksLikeInstall $cand) { return $cand }
        }
    }

    # 3. Типичные места распаковки.
    $guesses = @(
        (Join-Path $env:ProgramFiles "Chillistica_game"),
        (Join-Path ${env:ProgramFiles(x86)} "Chillistica_game"),
        (Join-Path $env:USERPROFILE "Downloads"),
        (Join-Path $env:USERPROFILE "Desktop"),
        (Join-Path $env:USERPROFILE "Documents")
    ) | Where-Object { $_ }

    foreach ($g in $guesses) {
        if (Test-LooksLikeInstall $g) { return $g }

        if (Test-Path -LiteralPath $g -PathType Container) {
            foreach ($sub in (Get-ChildItem -LiteralPath $g -Directory -ErrorAction SilentlyContinue |
                              Where-Object { $_.Name -like "*hillistica*" })) {
                if (Test-LooksLikeInstall $sub.FullName) { return $sub.FullName }
            }
        }
    }

    return $null
}

# Папка программы: параметр -> папка скрипта -> автопоиск.
if ($Path) {
    $scriptDir = $Path
}
elseif (Test-LooksLikeInstall $PSScriptRoot) {
    $scriptDir = $PSScriptRoot
}
else {
    $scriptDir = Find-Installation
}

if (-not $scriptDir) {
    # Папку не нашли, но служба/драйвер/настройки могли остаться — их всё равно
    # надо снять, поэтому работаем дальше с пустым путём программы.
    $scriptDir = ""
}

function Write-Step {
    param([string]$Text)
    Write-Host ""
    Write-Host "== $Text ==" -ForegroundColor Cyan
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$isAdmin = Test-IsAdmin

Write-Host "Chillistica_game - полное удаление" -ForegroundColor Yellow
Write-Host "Папка программы: $scriptDir"

if (-not $isAdmin) {
    Write-Host ""
    Write-Host "ВНИМАНИЕ: скрипт запущен БЕЗ прав администратора." -ForegroundColor Red
    Write-Host "Приложение и движок работают с повышенными правами, поэтому без прав" -ForegroundColor Red
    Write-Host "администратора их не остановить, драйвер WinDivert и старую службу не снять." -ForegroundColor Red
    Write-Host "Запустите uninstall.cmd (не этот .ps1 напрямую) - он сам запросит права." -ForegroundColor Red
}

# ---- 1. Останавливаем сам процесс приложения ------------------------------
Write-Step "Останавливаем Chillistica_game.exe"

$appProcesses = Get-Process -Name "Chillistica_game" -ErrorAction SilentlyContinue

if ($appProcesses) {
    foreach ($p in $appProcesses) {
        try {
            Stop-Process -Id $p.Id -Force -ErrorAction Stop
            Write-Host "Остановлен процесс PID=$($p.Id)" -ForegroundColor Green
        }
        catch {
            Write-Host "Не удалось остановить PID=$($p.Id): $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    Start-Sleep -Milliseconds 500
}
else {
    Write-Host "Chillistica_game.exe не запущен."
}

# ---- 2. Останавливаем winws.exe именно этой установки ---------------------
Write-Step "Останавливаем движок winws.exe"

$ourWinws = Join-Path $scriptDir "Engine\winws2\bin\winws.exe"

try {
    $ourWinws = (Resolve-Path -LiteralPath $ourWinws -ErrorAction Stop).Path
}
catch {
    # Engine folder missing/moved - fall back to the unresolved path; the
    # comparison below just will not match anything, which is the safe default.
}

$winwsProcesses = Get-Process -Name "winws" -ErrorAction SilentlyContinue
$killedWinws = $false

if ($winwsProcesses) {
    foreach ($p in $winwsProcesses) {
        $path = $null
        try { $path = $p.Path } catch { $path = $null }

        if ($path -and ($path -ieq $ourWinws)) {
            try {
                Stop-Process -Id $p.Id -Force -ErrorAction Stop
                Write-Host "Остановлен winws.exe PID=$($p.Id)" -ForegroundColor Green
                $killedWinws = $true
            }
            catch {
                Write-Host "Не удалось остановить winws.exe PID=$($p.Id): $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
}

if (-not $killedWinws) {
    Write-Host "winws.exe этой установки не запущен (или не удалось определить его путь без прав администратора)."
}

Start-Sleep -Milliseconds 500

# ---- 3. Снимаем драйвер WinDivert ------------------------------------------
#
# WinDivert - ОБЩИЙ системный драйвер, а не наш: его ставят и используют
# GoodbyeDPI, отдельно установленный zapret и другие обходные утилиты. Остановка
# службы рвёт хендлы у ВСЕХ процессов сразу, поэтому снимать драйвер вслепую
# нельзя - у человека с работающим вторым инструментом трафик молча перестанет
# фильтроваться. Шаг 2 уже отличает наш winws от чужого по полному пути; здесь
# применяется то же правило: есть посторонний потребитель - драйвер не трогаем.
#
# Если потребителей нет, удаление безопасно и не ломает чужой софт навсегда:
# WinDivert.dll переустанавливает службу сама при следующем WinDivertOpen.
Write-Step "Снимаем драйвер WinDivert"

function Get-ForeignWinDivertUsers {
    param([string]$OurWinwsPath)

    $foreign = @()

    foreach ($p in (Get-Process -Name "winws" -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $p.Path } catch { $path = $null }

        # Путь не читается (чужой elevated процесс) - считаем ЧУЖИМ: пропустить
        # удаление безопаснее, чем оборвать неизвестный работающий инструмент.
        if (-not $path -or ($path -ine $OurWinwsPath)) {
            $shown = if ($path) { $path } else { "путь недоступен" }
            $foreign += "winws.exe (PID=$($p.Id), $shown)"
        }
    }

    foreach ($name in @("goodbyedpi", "GoodbyeDPI", "zapret", "winws1", "WinDivertProxy")) {
        foreach ($p in (Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            $foreign += "$($p.ProcessName).exe (PID=$($p.Id))"
        }
    }

    return $foreign
}

if ($isAdmin) {
    $windivertSvc = Get-Service -Name "WinDivert" -ErrorAction SilentlyContinue

    if ($windivertSvc) {
        $foreignUsers = Get-ForeignWinDivertUsers -OurWinwsPath $ourWinws

        if ($foreignUsers.Count -gt 0) {
            Write-Host "Драйвер WinDivert НЕ снят - его сейчас использует другая программа:" -ForegroundColor Yellow
            foreach ($u in $foreignUsers) {
                Write-Host "   - $u" -ForegroundColor Yellow
            }
            Write-Host "Это не наша программа (например GoodbyeDPI или отдельный zapret)." -ForegroundColor Yellow
            Write-Host "Снятие драйвера оборвало бы ей защиту, поэтому шаг пропущен." -ForegroundColor Yellow
            Write-Host "Закройте ту программу и запустите uninstall.cmd ещё раз, если драйвер нужно убрать." -ForegroundColor Yellow
        }
        else {
            sc.exe stop WinDivert | Out-Null
            Start-Sleep -Seconds 1
            sc.exe delete WinDivert | Out-Null

            # sc.exe - внешняя программа: она НЕ бросает исключение при ошибке,
            # поэтому об успехе судим по состоянию службы, а не по отсутствию throw.
            if (Get-Service -Name "WinDivert" -ErrorAction SilentlyContinue) {
                Write-Host "Не удалось снять службу WinDivert (код sc.exe: $LASTEXITCODE)." -ForegroundColor Red
                Write-Host "Обычно она исчезает после перезагрузки компьютера." -ForegroundColor Yellow
            }
            else {
                Write-Host "Служба WinDivert остановлена и удалена." -ForegroundColor Green
            }
        }
    }
    else {
        Write-Host "Служба WinDivert не установлена - нечего снимать."
    }

    # Файл драйвера трогаем только если службы уже нет: пока служба жива (в том
    # числе чужая), удалять .sys нельзя.
    if (-not (Get-Service -Name "WinDivert" -ErrorAction SilentlyContinue)) {
        foreach ($driverName in @("WinDivert64.sys", "WinDivert32.sys")) {
            $driverFile = Join-Path $env:SystemRoot "System32\drivers\$driverName"

            if (Test-Path -LiteralPath $driverFile) {
                try {
                    Remove-Item -LiteralPath $driverFile -Force -ErrorAction Stop
                    Write-Host "Удалён файл драйвера: $driverFile" -ForegroundColor Green
                }
                catch {
                    Write-Host "Файл драйвера ещё занят: $driverFile" -ForegroundColor Yellow
                    Write-Host "Он исчезнет сам после перезагрузки компьютера." -ForegroundColor Yellow
                }
            }
        }
    }
}
else {
    Write-Host "Пропущено (нужны права администратора)." -ForegroundColor Yellow
}

# ---- 4. Удаляем старую службу версии до 0.5.0 ------------------------------
Write-Step "Удаляем старую службу Chillistica_game.Service (версии до 0.5.0)"

if ($isAdmin) {
    $oldSvc = Get-Service -Name "Chillistica_game.Service" -ErrorAction SilentlyContinue

    if ($oldSvc) {
        sc.exe stop Chillistica_game.Service | Out-Null
        Start-Sleep -Seconds 1
        sc.exe delete Chillistica_game.Service | Out-Null

        # Как и выше: sc.exe не бросает исключений, судим по состоянию службы.
        if (Get-Service -Name "Chillistica_game.Service" -ErrorAction SilentlyContinue) {
            Write-Host "Не удалось удалить службу Chillistica_game.Service (код sc.exe: $LASTEXITCODE)." -ForegroundColor Red
            Write-Host "Она исчезнет после перезагрузки компьютера." -ForegroundColor Yellow
        }
        else {
            Write-Host "Служба Chillistica_game.Service остановлена и удалена." -ForegroundColor Green
            Write-Host "Именно она могла годами висеть в автозапуске после обновления с версии до 0.5.0." -ForegroundColor Green
        }
    }
    else {
        Write-Host "Служба Chillistica_game.Service не установлена - нечего удалять."
    }
}
else {
    Write-Host "Пропущено (нужны права администратора)." -ForegroundColor Yellow
}

# ---- 5. Логи и настройки ----------------------------------------------------
Write-Step "Удаляем логи и настройки"

$localData = Join-Path $env:LOCALAPPDATA "Chillistica_game"
$roamingData = Join-Path $env:APPDATA "Chillistica_game"

foreach ($dir in @($localData, $roamingData)) {
    if (Test-Path -LiteralPath $dir) {
        try {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop
            Write-Host "Удалено: $dir" -ForegroundColor Green
        }
        catch {
            Write-Host "Не удалось удалить $dir : $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    else {
        Write-Host "Не найдено (и не нужно): $dir"
    }
}

# ---- Итог --------------------------------------------------------------------
Write-Step "Проверка"

$remaining = @()

if (Get-Process -Name "Chillistica_game" -ErrorAction SilentlyContinue) {
    $remaining += "Chillistica_game.exe всё ещё запущен"
}
if (Get-Process -Name "winws" -ErrorAction SilentlyContinue) {
    $remaining += "winws.exe всё ещё запущен (если это не наш - возможно, отдельный zapret, это нормально)"
}
if ($isAdmin -and (Get-Service -Name "WinDivert" -ErrorAction SilentlyContinue)) {
    $remaining += "служба WinDivert всё ещё установлена"
}
if ($isAdmin -and (Get-Service -Name "Chillistica_game.Service" -ErrorAction SilentlyContinue)) {
    $remaining += "служба Chillistica_game.Service всё ещё установлена"
}

if ($remaining.Count -eq 0) {
    Write-Host ""
    Write-Host "Готово: программа, движок, драйвер WinDivert и старая служба удалены." -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "Осталось (см. предупреждения выше, что пошло не так):" -ForegroundColor Yellow
    foreach ($item in $remaining) {
        Write-Host " - $item" -ForegroundColor Yellow
    }
}

# ---- 6. Удаляем саму папку программы ----------------------------------------
#
# Без этого шага жалоба "невозможно удалить приложение" остаётся в силе: всё
# вычищено, а папка на месте. Скрипт не может удалить каталог, в котором сам
# выполняется, поэтому удаление ОТКЛАДЫВАЕТСЯ: отдельный процесс переживает нас,
# ждёт закрытия окон и убирает папку.
Write-Step "Удаляем папку программы"

function Test-SafeToDelete {
    <#
        Предохранитель. rd /s /q по неверному пути необратим, а люди регулярно
        распаковывают архив НЕ в отдельную папку, а прямо в Загрузки/Рабочий стол
        — тогда наивное удаление "своей папки" снесло бы весь каталог со всеми
        чужими файлами. Поэтому удаляем только каталог, который действительно
        выглядит как распакованная программа И не является известным системным
        или пользовательским каталогом.
    #>
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) {
        return @{ Ok = $false; Reason = "папка не найдена" }
    }

    $full = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')

    # Корень диска (C:\) — сегментов меньше двух.
    if ($full -match '^[A-Za-z]:\\?$') {
        return @{ Ok = $false; Reason = "это корень диска" }
    }

    $forbidden = @(
        $env:SystemRoot,
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)},
        $env:ProgramData,
        $env:USERPROFILE,
        $env:APPDATA,
        $env:LOCALAPPDATA,
        (Join-Path $env:USERPROFILE "Desktop"),
        (Join-Path $env:USERPROFILE "Downloads"),
        (Join-Path $env:USERPROFILE "Documents"),
        (Join-Path $env:USERPROFILE "OneDrive"),
        "C:\Users"
    ) | Where-Object { $_ } | ForEach-Object { $_.TrimEnd('\') }

    foreach ($bad in $forbidden) {
        if ($full -ieq $bad) {
            return @{ Ok = $false; Reason = "это системный или пользовательский каталог ($full), а не папка программы" }
        }
    }

    # Рабочая копия репозитория тоже содержит Engine\winws2, но это ИСХОДНИКИ, а
    # не распакованный релиз — снести их значит уничтожить чужую работу.
    foreach ($marker in @(".git", ".gitattributes", "Chillistica_game.sln")) {
        if (Test-Path -LiteralPath (Join-Path $full $marker)) {
            return @{ Ok = $false; Reason = "похоже на репозиторий с исходниками (найден $marker), а не на распакованную программу" }
        }
    }

    # Каталог обязан выглядеть как наша распакованная программа — в раскладке
    # 0.5.0+ (всё в корне) либо в раскладке версий до 0.5.0 (app\ + service\).
    $hasExe = Test-Path -LiteralPath (Join-Path $full "Chillistica_game.exe")
    $hasEngine = Test-Path -LiteralPath (Join-Path $full "Engine\winws2")
    $hasLegacyApp = Test-Path -LiteralPath (Join-Path $full "app\Chillistica_game.App.exe")
    $hasLegacySvc = Test-Path -LiteralPath (Join-Path $full "service\Chillistica_game.Service.exe")

    if (-not ($hasExe -or $hasEngine -or $hasLegacyApp -or $hasLegacySvc)) {
        return @{ Ok = $false; Reason = "в папке нет ни Chillistica_game.exe, ни Engine\winws2, ни app\/service\ от старых версий - не похоже на папку программы" }
    }

    return @{ Ok = $true; Reason = "" }
}

$safety = Test-SafeToDelete -Path $scriptDir

if (-not $safety.Ok) {
    Write-Host "Папку программы автоматически удалять не буду: $($safety.Reason)." -ForegroundColor Yellow
    Write-Host "Удалите её вручную, если нужно: $scriptDir" -ForegroundColor Yellow

    try { Start-Process explorer.exe -ArgumentList "/select,`"$PSCommandPath`"" } catch { }
}
else {
    $doDelete = $AssumeYes

    if (-not $doDelete) {
        Write-Host "Удалить саму папку программы?" -ForegroundColor Yellow
        Write-Host "   $scriptDir" -ForegroundColor Yellow
        $answer = Read-Host "Введите Y (удалить) или N (оставить)"
        $doDelete = ($answer -match '^[YyДд]')
    }

    if ($doDelete) {
        # Отложенный удалятель: живёт в %TEMP% (вне удаляемой папки), ждёт, пока
        # закроются наши окна, и повторяет попытки — файл может быть ещё занят
        # антивирусом или самим cmd-окном.
        $target = (Resolve-Path -LiteralPath $scriptDir).Path
        $deleterPath = Join-Path $env:TEMP "chillistica-cleanup-$([guid]::NewGuid().ToString('N')).ps1"

        $deleterBody = @"
Start-Sleep -Seconds 3

for (`$i = 0; `$i -lt 30; `$i++) {
    try {
        Remove-Item -LiteralPath '$target' -Recurse -Force -ErrorAction Stop
        break
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

Remove-Item -LiteralPath '`$PSCommandPath' -Force -ErrorAction SilentlyContinue
"@

        Set-Content -LiteralPath $deleterPath -Value $deleterBody -Encoding UTF8

        try {
            Start-Process -FilePath "powershell.exe" `
                -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", "`"$deleterPath`"" `
                -WorkingDirectory $env:SystemRoot `
                -WindowStyle Hidden

            Write-Host "Папка будет удалена сразу после закрытия этих окон:" -ForegroundColor Green
            Write-Host "   $target" -ForegroundColor Green
            Write-Host "Закройте окно — больше ничего делать не нужно." -ForegroundColor Green
        }
        catch {
            Write-Host "Не удалось запланировать удаление папки: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "Удалите её вручную: $target" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "Папка оставлена: $scriptDir"
    }
}
