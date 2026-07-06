param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

function New-CleanDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path $Path) {
        Remove-Item $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

$repoRoot = Split-Path -Parent $PSScriptRoot

$serviceProject = Join-Path $repoRoot "Chillistica_game.Service\Chillistica_game.Service.csproj"
$appProject = Join-Path $repoRoot "Chillistica_game.App\Chillistica_game.App.csproj"
$appsettingsPath = Join-Path $repoRoot "Chillistica_game.Service\appsettings.json"

if (-not (Test-Path $serviceProject)) {
    throw "Service project not found: $serviceProject"
}

if (-not (Test-Path $appProject)) {
    throw "App project not found: $appProject"
}

if (-not (Test-Path $appsettingsPath)) {
    throw "Service appsettings not found: $appsettingsPath"
}

$appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
$activeProfile = [string]$appsettings.EngineProfile.ActiveProfilePath

Write-Host "ActiveProfilePath=$activeProfile"

$artifacts = Join-Path $repoRoot "artifacts"
$releaseDir = Join-Path $artifacts "release"
$staging = Join-Path $artifacts "staging"

New-CleanDirectory -Path $artifacts
New-CleanDirectory -Path $releaseDir
New-CleanDirectory -Path $staging

$servicePublishDir = Join-Path $staging "service"
$appPublishDir = Join-Path $staging "app"

Write-Host "Publishing service..." -ForegroundColor Cyan

dotnet publish `
    $serviceProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $servicePublishDir

if (-not (Test-Path (Join-Path $servicePublishDir "Chillistica_game.Service.exe"))) {
    throw "Service publish failed: exe was not created."
}

Write-Host "Publishing app..." -ForegroundColor Cyan

dotnet publish `
    $appProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $appPublishDir

if (-not (Test-Path (Join-Path $appPublishDir "Chillistica_game.App.exe"))) {
    throw "App publish failed: exe was not created."
}

Copy-Item ".\scripts\install-package.ps1" (Join-Path $staging "install-package.ps1") -Force
Copy-Item ".\scripts\install.cmd" (Join-Path $staging "install.cmd") -Force

if (Test-Path ".\scripts\uninstall-service.ps1") {
    Copy-Item ".\scripts\uninstall-service.ps1" (Join-Path $staging "uninstall-service.ps1") -Force
}

@"
Chillistica_game $Version

Установка:
1. Распаковать архив.
2. Запустить install.cmd.
3. Подтвердить запуск от администратора.
4. После установки приложение откроется автоматически.

Состав:
- Chillistica_game.App.exe
- Chillistica_game.Service.exe
- install.cmd
- install-package.ps1
"@ | Set-Content (Join-Path $staging "README_INSTALL.txt") -Encoding UTF8

$zipPath = Join-Path $releaseDir "Chillistica_game-$Version-win-x64.zip"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive `
    -Path (Join-Path $staging "*") `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal `
    -Force

Write-Host "RELEASE_ZIP=$zipPath" -ForegroundColor Green
