param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$AssemblyVersion
)

$ErrorActionPreference = "Stop"

if ($Version.StartsWith('v') -and [string]::IsNullOrWhiteSpace($env:CHILLISTICA_SIGNING_KEY_PEM)) {
    throw "CHILLISTICA_SIGNING_KEY_PEM must be set for tagged releases; refusing to produce an unsigned release."
}

if ([string]::IsNullOrWhiteSpace($AssemblyVersion)) {
    $AssemblyVersion = $Version -replace '^v', ''
}

function New-CleanDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Assert-PublishOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory,

        [Parameter(Mandatory = $true)]
        [string]$BuildName
    )

    $appPath = Join-Path $PublishDirectory "Chillistica_game.exe"
    $enginePath = Join-Path $PublishDirectory "Engine\winws2\bin\winws.exe"

    if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
        throw "$BuildName publish failed: Chillistica_game.exe was not created at $appPath"
    }

    if (-not (Test-Path -LiteralPath $enginePath -PathType Leaf)) {
        throw "$BuildName publish failed: engine executable was not created at $enginePath"
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "Chillistica_game.App\Chillistica_game.App.csproj"

$artifacts = Join-Path $repoRoot "artifacts"
$releaseDir = Join-Path $artifacts "release"
$frameworkDependentDir = Join-Path $artifacts "staging-fd"
$selfContainedDir = Join-Path $artifacts "staging-sc"

New-CleanDirectory -Path $artifacts
New-CleanDirectory -Path $releaseDir
New-CleanDirectory -Path $frameworkDependentDir
New-CleanDirectory -Path $selfContainedDir

Write-Host "Publishing framework-dependent package..." -ForegroundColor Cyan
dotnet publish `
    $appProject `
    -c Release `
    -r win-x64 `
    -p:Version=$AssemblyVersion `
    --self-contained false `
    -p:PublishSingleFile=true `
    -o $frameworkDependentDir

Assert-PublishOutput -PublishDirectory $frameworkDependentDir -BuildName "Framework-dependent"

Write-Host "Publishing self-contained package..." -ForegroundColor Cyan
dotnet publish `
    $appProject `
    -c Release `
    -r win-x64 `
    -p:Version=$AssemblyVersion `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $selfContainedDir

Assert-PublishOutput -PublishDirectory $selfContainedDir -BuildName "Self-contained"

$runFirstPath = Join-Path $PSScriptRoot "run-first.cmd"
Copy-Item -LiteralPath $runFirstPath -Destination (Join-Path $frameworkDependentDir "run-first.cmd") -Force
Copy-Item -LiteralPath $runFirstPath -Destination (Join-Path $selfContainedDir "run-first.cmd") -Force

@"
Chillistica_game $Version

1. Распакуйте архив в отдельную папку.
2. Дважды щёлкните run-first.cmd (или запустите Chillistica_game.exe напрямую).
3. Подтвердите запрос контроля учётных записей (UAC).
4. Нажмите единственную кнопку «Включить защиту».

Для этой версии требуется .NET 8 Desktop Runtime. Если он отсутствует, run-first.cmd установит его автоматически.
"@ | Set-Content -LiteralPath (Join-Path $frameworkDependentDir "README_FIRST.txt") -Encoding UTF8

@"
Chillistica_game $Version

1. Распакуйте архив в отдельную папку.
2. Дважды щёлкните run-first.cmd (или запустите Chillistica_game.exe напрямую).
3. Подтвердите запрос контроля учётных записей (UAC).
4. Нажмите единственную кнопку «Включить защиту».
"@ | Set-Content -LiteralPath (Join-Path $selfContainedDir "README_FIRST.txt") -Encoding UTF8

$frameworkDependentZip = Join-Path $releaseDir "Chillistica_game-$Version-win-x64.zip"
$selfContainedZip = Join-Path $releaseDir "Chillistica_game-$Version-win-x64-standalone.zip"

Compress-Archive `
    -Path (Join-Path $frameworkDependentDir "*") `
    -DestinationPath $frameworkDependentZip `
    -CompressionLevel Optimal

Compress-Archive `
    -Path (Join-Path $selfContainedDir "*") `
    -DestinationPath $selfContainedZip `
    -CompressionLevel Optimal

if (-not [string]::IsNullOrWhiteSpace($env:CHILLISTICA_SIGNING_KEY_PEM)) {
    $signScript = Join-Path $PSScriptRoot "sign-release.ps1"
    & $signScript -FilePath $frameworkDependentZip -PrivateKeyPem $env:CHILLISTICA_SIGNING_KEY_PEM
    & $signScript -FilePath $selfContainedZip -PrivateKeyPem $env:CHILLISTICA_SIGNING_KEY_PEM
}
else {
    Write-Warning "CHILLISTICA_SIGNING_KEY_PEM is not set; release zips are unsigned."
}

Write-Host "RELEASE_ZIP=$frameworkDependentZip" -ForegroundColor Green
Write-Host "RELEASE_ZIP=$selfContainedZip" -ForegroundColor Green
