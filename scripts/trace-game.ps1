<#
    trace-game.ps1 — показывает, КУДА на самом деле ходит игра и что из этого
    у вас блокируется.

    Зачем нужен: проверять несколько известных адресов Epic бесполезно — они
    отвечают и без обхода, пока игра пишет "сервер отключен". Значит режется
    что-то другое. Скрипт не гадает: он снимает реальные обращения игры (по
    DNS-кэшу и активным соединениям), а затем проверяет каждый найденный адрес
    мимо VPN-туннеля и показывает, какие из них недоступны.

    Права администратора НЕ нужны. VPN выключать не нужно — проверка идёт мимо
    него, а вот игру для чистоты стоит запускать БЕЗ VPN, иначе трассировать
    будет нечего.

    Как пользоваться:
        1) Закройте игру и лаунчер.
        2) Запустите скрипт: powershell -ExecutionPolicy Bypass -File .\trace-game.ps1
        3) Когда он попросит — запустите игру и дойдите до ошибки.
        4) Вернитесь в окно скрипта и нажмите Enter.

    В конце будет список адресов игры с пометкой, какие из них не отвечают.
    Именно они и есть цель для обхода.
#>

param(
    [int]$WatchSeconds = 180,
    [string]$SourceIp = ""
)

$ErrorActionPreference = "Continue"

# Процессы, которые считаем "игрой": лаунчер, сам клиент, сервис Epic.
$gamePatterns = @("Fortnite", "EpicGames", "EpicWebHelper", "EasyAntiCheat")

function Get-PhysicalSourceIp {
    $phys = Get-NetIPConfiguration |
        Where-Object {
            $_.IPv4DefaultGateway -and
            $_.InterfaceAlias -notmatch 'tun|v2ray|wintun|wg|tap|singbox'
        } | Select-Object -First 1

    if (-not $phys) { return $null }

    return (Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $phys.InterfaceIndex -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '169.*' } | Select-Object -First 1).IPAddress
}

if (-not $SourceIp) { $SourceIp = Get-PhysicalSourceIp }

if (-not $SourceIp) {
    Write-Host "Не нашёл физический адаптер - проверка доступности будет через обычный маршрут." -ForegroundColor Yellow
}
else {
    Write-Host "Проверять доступность буду через адаптер $SourceIp (мимо VPN)." -ForegroundColor Cyan
}

# --- снимок ДО -------------------------------------------------------------
$before = @{}
foreach ($e in (Get-DnsClientCache -ErrorAction SilentlyContinue)) { $before[$e.Entry] = $true }

Write-Host ""
Write-Host "=============================================================" -ForegroundColor Yellow
Write-Host " ЗАПУСТИТЕ ИГРУ СЕЙЧАС и дойдите до ошибки." -ForegroundColor Yellow
Write-Host " Потом вернитесь сюда и нажмите Enter." -ForegroundColor Yellow
Write-Host "=============================================================" -ForegroundColor Yellow
Write-Host ""

$deadline = (Get-Date).AddSeconds($WatchSeconds)
$seenIps = @{}
$seenProcs = @{}

# Ждём Enter, попутно собирая соединения игровых процессов.
while ((Get-Date) -lt $deadline) {
    if ($Host.UI.RawUI.KeyAvailable) {
        $k = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        if ($k.VirtualKeyCode -eq 13) { break }
    }

    foreach ($p in (Get-Process -ErrorAction SilentlyContinue)) {
        $match = $false
        foreach ($pat in $gamePatterns) { if ($p.ProcessName -like "*$pat*") { $match = $true; break } }
        if (-not $match) { continue }

        $seenProcs[$p.ProcessName] = $true

        foreach ($c in (Get-NetTCPConnection -OwningProcess $p.Id -ErrorAction SilentlyContinue)) {
            if ($c.RemoteAddress -and $c.RemoteAddress -notmatch '^(127\.|0\.0\.0\.0|::)') {
                $seenIps[$c.RemoteAddress] = $c.State
            }
        }
    }

    Start-Sleep -Milliseconds 700
}

# --- снимок ПОСЛЕ: какие домены появились в DNS-кэше ------------------------
$newNames = @()
foreach ($e in (Get-DnsClientCache -ErrorAction SilentlyContinue)) {
    if (-not $before.ContainsKey($e.Entry)) { $newNames += $e.Entry }
}

$newNames = $newNames | Sort-Object -Unique

Write-Host ""
Write-Host "Процессы игры, которые видел: $(if ($seenProcs.Keys.Count) { ($seenProcs.Keys -join ', ') } else { 'ни одного (игра не запускалась?)' })"
Write-Host "Новых имён в DNS-кэше: $($newNames.Count); соединений: $($seenIps.Keys.Count)"

# Оставляем только то, что похоже на инфраструктуру игры.
$interesting = $newNames | Where-Object {
    $_ -match 'epic|fortnite|unreal|easyanticheat|akamai|aws|cloudfront|ol\.epicgames'
}

if (-not $interesting) { $interesting = $newNames }

Write-Host ""
Write-Host "== Проверяю адреса игры мимо туннеля ==" -ForegroundColor Cyan

$blocked = @()
$ok = @()

foreach ($h in $interesting) {
    $name = $h.TrimEnd('.')
    if ($name -notmatch '^[a-z0-9\.\-]+$') { continue }

    $code = 0
    for ($i = 0; $i -lt 2; $i++) {
        $args = @("-s", "-o", "NUL", "--max-time", "8", "-w", "%{http_code}")
        if ($SourceIp) { $args += @("--interface", $SourceIp) }
        $args += "https://$name/"

        $raw = & curl.exe @args 2>$null
        if ($raw -match '^\s*(\d+)') { $c = [int]$Matches[1]; if ($c -gt 0) { $code = $c; break } }
    }

    if ($code -gt 0) { $ok += $name; "  {0,-58} отвечает (code={1})" -f $name, $code }
    else { $blocked += $name; "  {0,-58} НЕ ОТВЕЧАЕТ" -f $name }
}

Write-Host ""
Write-Host "===================== ИТОГ =====================" -ForegroundColor Yellow

if ($blocked.Count -gt 0) {
    Write-Host "Заблокировано ($($blocked.Count)):" -ForegroundColor Red
    $blocked | ForEach-Object { "   $_" }
    Write-Host ""
    Write-Host "Эти адреса и надо добавить в хостлист обхода." -ForegroundColor Yellow
}
else {
    Write-Host "Все адреса игры отвечают напрямую." -ForegroundColor Green
    Write-Host "Значит дело не в блокировке по имени хоста: причина в другом —" -ForegroundColor Yellow
    Write-Host "например, режется игровой трафик по портам, а не веб-запросы." -ForegroundColor Yellow
}

$report = Join-Path ([Environment]::GetFolderPath('Desktop')) "fortnite-trace.txt"
$lines = @("Процессы: " + ($seenProcs.Keys -join ', '), "", "НЕ ОТВЕЧАЮТ:") + $blocked + @("", "ОТВЕЧАЮТ:") + $ok + @("", "IP-соединения:")
foreach ($ip in $seenIps.Keys) { $lines += ("  " + $ip + "  " + $seenIps[$ip]) }
$lines | Set-Content -LiteralPath $report -Encoding UTF8

Write-Host ""
Write-Host "Полный отчёт сохранён на Рабочий стол: $report" -ForegroundColor Cyan
