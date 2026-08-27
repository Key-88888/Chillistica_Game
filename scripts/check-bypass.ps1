<#
    check-bypass.ps1 — доказывает, РАБОТАЕТ ли обход, вместо того чтобы гадать.

    Зачем нужен: приложение помечает приложения как «best effort — не подтверждено»,
    и по этой метке нельзя понять, не пробила ли стратегия DPI или просто не удалось
    подтвердить результат. Скрипт меряет одни и те же цели дважды — с выключенным и с
    работающим движком — и сравнивает.

    ВАЖНО ПРО ТУННЕЛЬ: если поднят VPN/TUN (v2RayTun, sing-box, WireGuard), обычный
    запрос уходит в туннель и меряет ЕГО, а не вашего провайдера — именно так были
    испорчены все прошлые замеры. Поэтому каждый запрос привязывается к физическому
    адаптеру (curl --interface), и туннель выключать не нужно.

    Запускать ОТ ИМЕНИ АДМИНИСТРАТОРА (движку нужен драйвер WinDivert):
        powershell -ExecutionPolicy Bypass -File .\scripts\check-bypass.ps1

    Как читать результат:
        ДО=блок, ПОСЛЕ=доступен  -> обход РАБОТАЕТ, стратегия пробила DPI
        ДО=блок, ПОСЛЕ=блок      -> стратегия НЕ пробила, нужны другие параметры
        ДО=доступен, ПОСЛЕ=доступен -> сервис у вас не заблокирован, обходить нечего
#>

param(
    [string]$Apps = "youtube,discord",
    [int]$HoldSeconds = 45,

    # Перебрать ВСЕ стратегии-кандидаты для одного приложения и показать, какая
    # из них реально пробивает DPI. Без этого проверяется только кандидат 0, а
    # приложение в реальной работе перебирает всю лестницу.
    [string]$FindStrategyFor = ""
)

$ErrorActionPreference = "Continue"

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdmin)) {
    Write-Host "Нужны права администратора: движку winws нужен драйвер WinDivert." -ForegroundColor Red
    Write-Host "Откройте PowerShell от имени администратора и запустите скрипт оттуда." -ForegroundColor Red
    exit 1
}

$phys = Get-NetIPConfiguration |
    Where-Object {
        $_.IPv4DefaultGateway -and
        $_.InterfaceAlias -notmatch 'tun|v2ray|wintun|wg|tap|singbox'
    } | Select-Object -First 1

if (-not $phys) {
    Write-Host "Не нашёл физический адаптер со шлюзом - нечем мерить." -ForegroundColor Red
    exit 1
}

$srcIp = (Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $phys.InterfaceIndex -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike '169.*' } | Select-Object -First 1).IPAddress

Write-Host "Меряю через адаптер '$($phys.InterfaceAlias)' ($srcIp), мимо туннеля." -ForegroundColor Cyan

$tunnelUp = Get-NetAdapter -ErrorAction SilentlyContinue |
    Where-Object { $_.Status -eq 'Up' -and $_.Name -match 'tun|v2ray|wg|tap' }

if ($tunnelUp) {
    Write-Host "Замечен активный туннель '$($tunnelUp.Name)' - выключать его НЕ нужно, замер идёт мимо него." -ForegroundColor Yellow
}

$targets = @{
    youtube  = @("www.youtube.com", "i.ytimg.com")
    discord  = @("discord.com", "cdn.discordapp.com", "gateway.discord.gg")
    roblox   = @("www.roblox.com", "games.roblox.com", "presence.roblox.com")
    fortnite = @("www.epicgames.com", "account-public-service-prod.ol.epicgames.com", "lightswitch-public-service-prod.ol.epicgames.com", "fortnite-public-service-prod11.ol.epicgames.com")
}

$selected = @()
foreach ($a in ($Apps -split ',' | ForEach-Object { $_.Trim().ToLower() })) {
    if ($targets.ContainsKey($a)) {
        foreach ($h in $targets[$a]) {
            $selected += [pscustomobject]@{ App = $a; Target = $h }
        }
    }
}

if ($selected.Count -eq 0) {
    Write-Host "Не распознал ни одного приложения в '-Apps $Apps'." -ForegroundColor Red
    exit 1
}

function Measure-Target {
    param([string]$TargetHost, [string]$SourceIp)

    $code = 0
    $tls = 0.0

    # Несколько попыток: фильтрация по SNI срабатывает не на 100% соединений,
    # поэтому одиночная проба даёт то ложный успех, то ложный провал. Хватает
    # одного удавшегося соединения, чтобы считать цель доступной.
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $raw = & curl.exe -s -o NUL --max-time 8 --interface $SourceIp -w "%{http_code}|%{time_appconnect}" "https://$TargetHost/" 2>$null

        if ($raw -and $raw -match '^\s*(\d+)\|([\d\.]+)') {
            $c = [int]$Matches[1]

            if ($c -gt 0) {
                $code = $c
                $tls = [double]$Matches[2]
                if ($c -lt 500) { break }
            }
        }

        Start-Sleep -Milliseconds 400
    }

    [pscustomobject]@{
        Target = $TargetHost
        Ok     = ($code -gt 0 -and $code -lt 500)
        Code   = $code
        Tls    = $tls
    }
}

Write-Host ""
Write-Host "== Замер 1: движок ВЫКЛЮЧЕН ==" -ForegroundColor Cyan

$before = @{}
foreach ($t in $selected) {
    $r = Measure-Target -TargetHost $t.Target -SourceIp $srcIp
    $before[$t.Target] = $r
    $state = if ($r.Ok) { "доступен (code=$($r.Code))" } else { "ЗАБЛОКИРОВАН (обрыв, tls=$($r.Tls))" }
    "{0,-24} {1}" -f $t.Target, $state
}

# --- поиск exe (нужен и режиму перебора, и обычному) ------------------------
function Resolve-AppExe {
    param([string]$ScriptRoot)

    $candidates = @(
        (Join-Path (Split-Path $ScriptRoot -Parent) "Chillistica_game.exe"),
        (Join-Path $ScriptRoot "Chillistica_game.exe")
    )

    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { return $c }
    }

    $found = Get-ChildItem -Path (Split-Path $ScriptRoot -Parent) -Filter "Chillistica_game.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($found) { return $found.FullName }
    return $null
}

function Invoke-EngineWindow {
    <#
        Поднимает движок на заданное время и возвращает объект процесса.
        Замер делается ПОКА он жив, поэтому ждать завершения здесь нельзя.
    #>
    param([string]$ExePath, [string]$Spec, [int]$Seconds, [string]$ResultFile)

    Start-Process -FilePath $ExePath -ArgumentList @("--selftest-engine", $Spec, $ResultFile, "$Seconds") -PassThru
}

# --- режим перебора стратегий -----------------------------------------------
if ($FindStrategyFor) {
    $app = $FindStrategyFor.Trim().ToLower()

    if (-not $targets.ContainsKey($app)) {
        Write-Host "Не знаю приложение '$app'. Доступны: $($targets.Keys -join ', ')" -ForegroundColor Red
        exit 1
    }

    $exePath = Resolve-AppExe -ScriptRoot $PSScriptRoot
    if (-not $exePath) {
        Write-Host "Не нашёл Chillistica_game.exe рядом со скриптом." -ForegroundColor Red
        exit 1
    }

    $catalog = Join-Path (Join-Path (Split-Path $exePath -Parent) "Engine\winws2\strategies") "$app.json"

    if (-not (Test-Path -LiteralPath $catalog)) {
        Write-Host "Не нашёл каталог стратегий: $catalog" -ForegroundColor Red
        exit 1
    }

    $strategies = (Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json).Strategies
    $appTargets = $targets[$app]

    Write-Host ""
    Write-Host "== Перебираю стратегии '$app': всего $($strategies.Count) ==" -ForegroundColor Cyan

    # База: что недоступно БЕЗ движка. Пробовать пробить то, что и так работает,
    # смысла нет, и такие цели только зашумят вывод.
    $baseBlocked = @()
    foreach ($h in $appTargets) {
        $r = Measure-Target -TargetHost $h -SourceIp $srcIp
        if (-not $r.Ok) { $baseBlocked += $h }
    }

    if ($baseBlocked.Count -eq 0) {
        Write-Host "Все цели '$app' и так доступны без движка - перебирать нечего." -ForegroundColor Green
        exit 0
    }

    Write-Host "Заблокировано без движка: $($baseBlocked -join ', ')" -ForegroundColor Yellow

    $results = @()
    $engineFailures = 0

    for ($i = 0; $i -lt $strategies.Count; $i++) {
        $sid = $strategies[$i].StrategyId
        Write-Host ""
        Write-Host "--- [$i] $sid ---" -ForegroundColor Cyan

        $rf = Join-Path $env:TEMP ("chillistica-find-" + [guid]::NewGuid().ToString('N') + ".txt")
        $proc = Invoke-EngineWindow -ExePath $exePath -Spec "${app}:$i" -Seconds 25 -ResultFile $rf

        Start-Sleep -Seconds 6

        $won = 0
        foreach ($h in $baseBlocked) {
            $r = Measure-Target -TargetHost $h -SourceIp $srcIp
            if ($r.Ok) { $won++ }
            "   {0,-24} {1}" -f $h, $(if ($r.Ok) { "ПРОБИЛО (code=$($r.Code))" } else { "нет" })
        }

        $results += [pscustomobject]@{ Index = $i; Id = $sid; Won = $won; Total = $baseBlocked.Count }

        if ($proc) { try { Wait-Process -Id $proc.Id -Timeout 60 -ErrorAction SilentlyContinue } catch { } }

        # ВАЖНО: без этой проверки не отличить "стратегия не пробила" от "движок
        # вообще не запустился" — в обоих случаях цели остаются недоступны, и
        # вывод одинаково показывал бы "не пробила", обвиняя стратегию зря.
        $engineOk = $false
        $engineSays = "результат движка не прочитан"

        if (Test-Path -LiteralPath $rf) {
            $out = Get-Content -LiteralPath $rf -Raw
            $engineOk = ($out -match 'startResult=ENGINE_STARTED') -and ($out -match 'isRunning=True')

            if (-not $engineOk) {
                $sr = if ($out -match 'startResult=([^\r\n]+)') { $Matches[1] } else { "?" }
                $ex = if ($out -match 'exception=([^\r\n]+)') { " / " + $Matches[1] } else { "" }
                $engineSays = "startResult=$sr$ex"
            }

            try { [System.IO.File]::Delete($rf) } catch { }
        }

        if (-not $engineOk) {
            Write-Host "   ДВИЖОК НЕ ЗАПУСТИЛСЯ: $engineSays" -ForegroundColor Red
            $engineFailures++
        }

        if ($won -eq $baseBlocked.Count) {
            Write-Host "   -> пробила ВСЕ заблокированные цели" -ForegroundColor Green
        }
    }

    Write-Host ""
    Write-Host "===================== ИТОГ =====================" -ForegroundColor Yellow
    foreach ($r in $results) {
        $mark = if ($r.Won -eq $r.Total) { "ПОЛНОСТЬЮ" } elseif ($r.Won -gt 0) { "частично" } else { "не пробила" }
        "[{0}] {1,-42} {2}/{3}  {4}" -f $r.Index, $r.Id, $r.Won, $r.Total, $mark
    }

    $best = $results | Sort-Object -Property Won -Descending | Select-Object -First 1

    Write-Host ""

    if ($engineFailures -eq $results.Count) {
        Write-Host "ДВИЖОК НЕ ЗАПУСТИЛСЯ НИ РАЗУ - результат ничего не говорит о стратегиях." -ForegroundColor Red
        Write-Host "Сначала разберитесь, почему не стартует winws (см. строки выше)." -ForegroundColor Red
        exit 2
    }

    if ($engineFailures -gt 0) {
        Write-Host "Внимание: движок не запустился на $engineFailures из $($results.Count) прогонов - эти строки не про стратегии." -ForegroundColor Yellow
    }

    if ($best.Won -eq $best.Total) {
        Write-Host "Лучшая: [$($best.Index)] $($best.Id) - пробивает всё. Её стоит поставить первой в $app.json." -ForegroundColor Green
    }
    elseif ($best.Won -gt 0) {
        Write-Host "Лучшая: [$($best.Index)] $($best.Id) - пробивает $($best.Won) из $($best.Total)." -ForegroundColor Yellow
        Write-Host "Полностью не справилась ни одна: нужны новые параметры desync." -ForegroundColor Yellow
    }
    else {
        Write-Host "Ни одна стратегия не пробила. Нужны другие параметры desync." -ForegroundColor Red
    }

    exit 0
}


$exe = Resolve-AppExe -ScriptRoot $PSScriptRoot

if (-not $exe) {
    Write-Host "Не нашёл Chillistica_game.exe рядом со скриптом." -ForegroundColor Red
    exit 1
}

$resultFile = Join-Path $env:TEMP ("chillistica-checkbypass-" + [guid]::NewGuid().ToString('N') + ".txt")

Write-Host ""
Write-Host "== Запускаю движок на $HoldSeconds сек ($Apps) ==" -ForegroundColor Cyan

$proc = Start-Process -FilePath $exe -ArgumentList @("--selftest-engine", $Apps, $resultFile, "$HoldSeconds") -PassThru

Start-Sleep -Seconds 6

Write-Host "== Замер 2: движок РАБОТАЕТ ==" -ForegroundColor Cyan

$after = @{}
foreach ($t in $selected) {
    $r = Measure-Target -TargetHost $t.Target -SourceIp $srcIp
    $after[$t.Target] = $r
    $state = if ($r.Ok) { "доступен (code=$($r.Code))" } else { "ЗАБЛОКИРОВАН (обрыв, tls=$($r.Tls))" }
    "{0,-24} {1}" -f $t.Target, $state
}

if ($proc) {
    try { Wait-Process -Id $proc.Id -Timeout ($HoldSeconds + 30) -ErrorAction SilentlyContinue } catch { }
}

Write-Host ""
Write-Host "===================== ИТОГ =====================" -ForegroundColor Yellow
"{0,-24} {1,-12} {2,-12} {3}" -f "ЦЕЛЬ", "ДО", "ПОСЛЕ", "ВЫВОД"

$fixed = 0
$failed = 0
$notBlocked = 0

foreach ($t in $selected) {
    $b = $before[$t.Target].Ok
    $a = $after[$t.Target].Ok

    if ((-not $b) -and $a) {
        $fixed++
        $verdict = "ОБХОД СРАБОТАЛ"
    }
    elseif ((-not $b) -and (-not $a)) {
        $failed++
        $verdict = "стратегия НЕ пробила"
    }
    elseif ($b -and $a) {
        $notBlocked++
        $verdict = "не заблокирован, обходить нечего"
    }
    else {
        $verdict = "стало хуже - движок мешает"
    }

    "{0,-24} {1,-12} {2,-12} {3}" -f $t.Target, $(if ($b) { "доступен" } else { "блок" }), $(if ($a) { "доступен" } else { "блок" }), $verdict
}

Write-Host ""

if ($failed -gt 0) {
    Write-Host "Стратегия не пробивает DPI на целях: $failed. Это НЕ ошибка диагностики -" -ForegroundColor Red
    Write-Host "обход действительно не работает, нужны другие параметры desync." -ForegroundColor Red
}

if ($fixed -gt 0) {
    Write-Host "Обход подтверждён на целях: $fixed (без движка блок, с движком доступ)." -ForegroundColor Green
}

if ($notBlocked -eq $selected.Count) {
    Write-Host "Ни одна цель не заблокирована у вашего провайдера - обходить нечего." -ForegroundColor Green
}

if (Test-Path -LiteralPath $resultFile) {
    Write-Host ""
    Write-Host "Что сказал движок:" -ForegroundColor Cyan
    Get-Content -LiteralPath $resultFile | Select-String -Pattern "startResult|isRunning|capture is started|version" | ForEach-Object { "  $_" }
    Remove-Item -LiteralPath $resultFile -Force -ErrorAction SilentlyContinue
}
