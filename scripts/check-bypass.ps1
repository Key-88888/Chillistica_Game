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
    [int]$HoldSeconds = 45
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
    discord  = @("discord.com", "cdn.discordapp.com")
    roblox   = @("www.roblox.com")
    fortnite = @("www.epicgames.com")
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

    $raw = & curl.exe -s -o NUL --max-time 12 --interface $SourceIp -w "%{http_code}|%{time_appconnect}" "https://$TargetHost/" 2>$null

    $code = 0
    $tls = 0.0

    if ($raw -and $raw -match '^\s*(\d+)\|([\d\.]+)') {
        $code = [int]$Matches[1]
        $tls = [double]$Matches[2]
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

$exe = Join-Path (Split-Path $PSScriptRoot -Parent) "Chillistica_game.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    $exe = Join-Path $PSScriptRoot "Chillistica_game.exe"
}

if (-not (Test-Path -LiteralPath $exe)) {
    $found = Get-ChildItem -Path (Split-Path $PSScriptRoot -Parent) -Filter "Chillistica_game.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($found) { $exe = $found.FullName }
}

if (-not (Test-Path -LiteralPath $exe)) {
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
