<#
    try-strategy.ps1 — включает ОДНУ выбранную стратегию и держит её, пока вы
    проверяете приложение вручную.

    Зачем нужен: для игр нельзя проверить обход автоматически. Веб-адреса Epic
    отвечают и без обхода, а зайти в матч скрипт не может — значит единственный
    честный тест это «включили стратегию, зашли в игру, посмотрели». Скрипт
    убирает из этого всю возню: запускает движок с конкретным кандидатом и
    держит его, пока вы играете.

    Запускать ОТ ИМЕНИ АДМИНИСТРАТОРА (движку нужен драйвер WinDivert).

    Примеры:
        # первый кандидат для Fortnite на 5 минут
        powershell -ExecutionPolicy Bypass -File .\try-strategy.ps1 -App fortnite -Index 0

        # второй кандидат, 10 минут
        powershell -ExecutionPolicy Bypass -File .\try-strategy.ps1 -App fortnite -Index 1 -Minutes 10

        # просто список кандидатов, без запуска
        powershell -ExecutionPolicy Bypass -File .\try-strategy.ps1 -App fortnite -List

    Как искать рабочий вариант: запустить с -Index 0, зайти в игру. Не помогло —
    закрыть окно скрипта, запустить с -Index 1, и так далее. Как только игра
    заработала, запомните номер: его и надо будет поставить первым.
#>

param(
    [string]$App = "fortnite",
    [int]$Index = 0,
    [int]$Minutes = 5,
    [switch]$List,

    # Интерактивный режим: печатает лестницу и спрашивает номер, пока не выйдут.
    # Нужен потому, что меню НЕЛЬЗЯ держать в .cmd: cmd.exe читает такие файлы в
    # системной кодировке, русский текст в UTF-8 рассыпается, строки рвутся, и
    # управляющие конструкции перестают работать. Здесь же кодировка корректна.
    [switch]$Menu
)

$ErrorActionPreference = "Continue"

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-AppExe {
    param([string]$ScriptRoot)

    foreach ($c in @(
        (Join-Path $ScriptRoot "Chillistica_game.exe"),
        (Join-Path (Split-Path $ScriptRoot -Parent) "Chillistica_game.exe"))) {
        if (Test-Path -LiteralPath $c) { return $c }
    }

    $found = Get-ChildItem -Path (Split-Path $ScriptRoot -Parent) -Filter "Chillistica_game.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($found) { return $found.FullName }
    return $null
}

$exe = Resolve-AppExe -ScriptRoot $PSScriptRoot

if (-not $exe) {
    Write-Host "Не нашёл Chillistica_game.exe рядом со скриптом." -ForegroundColor Red
    exit 1
}

$app = $App.Trim().ToLower()
$catalog = Join-Path (Join-Path (Split-Path $exe -Parent) "Engine\winws2\strategies") "$app.json"

if (-not (Test-Path -LiteralPath $catalog)) {
    Write-Host "Нет каталога стратегий для '$app': $catalog" -ForegroundColor Red
    exit 1
}

$strategies = (Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json).Strategies

Write-Host "Кандидаты для '$app' (всего $($strategies.Count)):" -ForegroundColor Cyan
for ($i = 0; $i -lt $strategies.Count; $i++) {
    $mark = if ($i -eq $Index -and -not $List) { ">>" } else { "  " }
    "{0} [{1}] {2}" -f $mark, $i, $strategies[$i].StrategyId
}

if ($List) { exit 0 }

if ($Menu) {
    if (-not (Test-IsAdmin)) {
        Write-Host ""
        Write-Host "Нужны права администратора: движку нужен драйвер WinDivert." -ForegroundColor Red
        Write-Host "Закройте это окно и запустите ПРОВЕРИТЬ-FORTNITE.cmd заново." -ForegroundColor Red
        Read-Host "Enter — выход"
        exit 1
    }

    while ($true) {
        Write-Host ""
        Write-Host "===============================================================" -ForegroundColor Yellow
        Write-Host " Введите номер стратегии и нажмите Enter. Начните с 0." -ForegroundColor Yellow
        Write-Host " Пустая строка или q — выход." -ForegroundColor Yellow
        Write-Host "===============================================================" -ForegroundColor Yellow

        $answer = Read-Host "Номер"

        if ([string]::IsNullOrWhiteSpace($answer) -or $answer -match '^[qQ]') { exit 0 }

        $picked = 0
        if (-not [int]::TryParse($answer.Trim(), [ref]$picked) -or $picked -lt 0 -or $picked -ge $strategies.Count) {
            Write-Host "Нужно число от 0 до $($strategies.Count - 1)." -ForegroundColor Red
            continue
        }

        # Запускаем себя же с выбранным номером — в отдельном процессе, чтобы
        # ошибка одного прогона не роняла меню целиком.
        & powershell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -App $app -Index $picked -Minutes $Minutes

        Write-Host ""
        Write-Host "Заработало — запомните номер $picked и сообщите его." -ForegroundColor Cyan
        Write-Host "Нет — введите следующий номер." -ForegroundColor Cyan
    }
}

if ($Index -lt 0 -or $Index -ge $strategies.Count) {
    Write-Host ""
    Write-Host "Индекс $Index вне диапазона 0..$($strategies.Count - 1)." -ForegroundColor Red
    exit 1
}

if (-not (Test-IsAdmin)) {
    Write-Host ""
    Write-Host "Нужны права администратора: движку winws нужен драйвер WinDivert." -ForegroundColor Red
    Write-Host "Откройте PowerShell от имени администратора и запустите скрипт оттуда." -ForegroundColor Red
    exit 1
}

$seconds = [Math]::Max(60, [Math]::Min(600, $Minutes * 60))
$rf = Join-Path $env:TEMP ("chillistica-try-" + [guid]::NewGuid().ToString('N') + ".txt")

Write-Host ""
Write-Host "Включаю [$Index] $($strategies[$Index].StrategyId)" -ForegroundColor Yellow
Write-Host $strategies[$Index].Description -ForegroundColor DarkGray
Write-Host ""
Write-Host "Движок будет работать $([int]($seconds/60)) мин. ЗАПУСКАЙТЕ ИГРУ СЕЙЧАС." -ForegroundColor Green
Write-Host "Закройте это окно, чтобы выключить обход досрочно." -ForegroundColor DarkGray

$proc = Start-Process -FilePath $exe `
    -ArgumentList @("--selftest-engine", "${app}:$Index", $rf, "$seconds") `
    -PassThru

Start-Sleep -Seconds 6

if ($proc.HasExited -and -not (Test-Path -LiteralPath $rf)) {
    Write-Host ""
    Write-Host ("Движок не запустился (код выхода 0x{0:X8})." -f $proc.ExitCode) -ForegroundColor Red
    Write-Host "Похоже, папка программы повреждена — скачайте архив заново." -ForegroundColor Red
    exit 2
}

Write-Host ""
Write-Host "Обход включён. Проверяйте игру." -ForegroundColor Green

try { Wait-Process -Id $proc.Id -Timeout ($seconds + 60) -ErrorAction SilentlyContinue } catch { }

Write-Host ""
Write-Host "Время вышло, обход выключен." -ForegroundColor Yellow

if (Test-Path -LiteralPath $rf) {
    $out = Get-Content -LiteralPath $rf -Raw

    if ($out -match 'startResult=ENGINE_STARTED' -and $out -match 'isRunning=True') {
        Write-Host "Движок отработал штатно." -ForegroundColor Green
    }
    else {
        $sr = if ($out -match 'startResult=([^\r\n]+)') { $Matches[1] } else { "?" }
        Write-Host "Движок сообщил о проблеме: $sr" -ForegroundColor Red
    }

    try { [System.IO.File]::Delete($rf) } catch { }
}

Write-Host ""
Write-Host "Заработала игра — запомните номер [$Index]." -ForegroundColor Cyan
Write-Host "Не заработала — повторите с -Index $($Index + 1)" -ForegroundColor Cyan
