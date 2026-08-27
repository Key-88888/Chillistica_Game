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
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        }
        catch {
            # A previous run of the packaged app leaves the WinDivert driver
            # loaded, and a loaded driver keeps WinDivert64.sys open. The delete
            # then fails PART WAY THROUGH, having already removed some engine
            # files, leaving a staging dir that looks built but is missing
            # binaries. Fail loudly with the fix instead of corrupting it.
            throw "Не удалось очистить $Path : $($_.Exception.Message)`n`nСкорее всего загружен драйвер WinDivert от предыдущего запуска - он держит WinDivert64.sys. Снимите его из-под администратора и повторите сборку:`n    sc.exe stop WinDivert`n    sc.exe delete WinDivert"
        }
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

function Assert-EngineTrust {
    <#
        Re-runs, at package time, exactly the check EngineIntegrity.VerifyOrThrow
        performs on the user's machine: every pinned file must hash to its
        manifest value, and every sealed directory must contain nothing else.

        This exists because the app fails CLOSED on any mismatch — the engine
        simply refuses to start — so a package whose bytes drift from the pinned
        manifest is not "slightly off", it is dead on arrival. A Windows checkout
        silently rewriting LF to CRLF in a pinned JSON is enough to cause it, and
        that is invisible in the zip listing.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory,

        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,

        [Parameter(Mandatory = $true)]
        [string]$BuildName
    )

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $pinned = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)

    foreach ($entry in $manifest.TrustedBinaries) {
        $relative = $entry.Path -replace '/', '\'
        [void]$pinned.Add($relative)

        $fullPath = Join-Path $PublishDirectory $relative

        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            if (-not $entry.Required) {
                continue
            }

            throw "$BuildName engine trust check failed: pinned file missing: $relative"
        }

        $actual = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash

        if ($actual -ne $entry.Sha256.Trim()) {
            $size = (Get-Item -LiteralPath $fullPath).Length
            throw @"
$BuildName engine trust check failed: $relative does not match trusted-manifest.json.
  expected SHA256 $($entry.Sha256.Trim())
  actual   SHA256 $actual ($size bytes)
The shipped app would refuse to start the engine. If this is a text file, check
that .gitattributes marks it -text so no CRLF conversion happens on checkout.
"@
        }
    }

    foreach ($sealed in $manifest.SealedDirectories) {
        $relativeDir = $sealed -replace '/', '\'
        $sealedPath = Join-Path $PublishDirectory $relativeDir

        if (-not (Test-Path -LiteralPath $sealedPath -PathType Container)) {
            throw "$BuildName engine trust check failed: sealed directory missing: $relativeDir"
        }

        foreach ($file in Get-ChildItem -LiteralPath $sealedPath -Recurse -File) {
            $relative = $file.FullName.Substring($PublishDirectory.TrimEnd('\').Length + 1)

            if (-not $pinned.Contains($relative)) {
                throw "$BuildName engine trust check failed: unpinned file in sealed directory: $relative"
            }
        }
    }

    Write-Host "$BuildName engine trust check passed ($($pinned.Count) pinned files)." -ForegroundColor Green
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "Chillistica_game.App\Chillistica_game.App.csproj"
$trustManifest = Join-Path $repoRoot "Engine\winws2\trusted-manifest.json"

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
Assert-EngineTrust `
    -PublishDirectory $frameworkDependentDir `
    -ManifestPath $trustManifest `
    -BuildName "Framework-dependent"

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
Assert-EngineTrust `
    -PublishDirectory $selfContainedDir `
    -ManifestPath $trustManifest `
    -BuildName "Self-contained"

$runFirstPath = Join-Path $PSScriptRoot "run-first.cmd"
Copy-Item -LiteralPath $runFirstPath -Destination (Join-Path $frameworkDependentDir "run-first.cmd") -Force
Copy-Item -LiteralPath $runFirstPath -Destination (Join-Path $selfContainedDir "run-first.cmd") -Force

# Shipped INSIDE the archive so removal never depends on the app still being
# able to start, on GitHub being reachable, or on the user finding the repo.
$uninstallCmdPath = Join-Path $PSScriptRoot "uninstall.cmd"
$uninstallPs1Path = Join-Path $PSScriptRoot "uninstall.ps1"
$checkBypassPath = Join-Path $PSScriptRoot "check-bypass.ps1"
$tryStrategyPath = Join-Path $PSScriptRoot "try-strategy.ps1"
$traceGamePath = Join-Path $PSScriptRoot "trace-game.ps1"
Copy-Item -LiteralPath $uninstallCmdPath -Destination (Join-Path $frameworkDependentDir "uninstall.cmd") -Force
Copy-Item -LiteralPath $uninstallPs1Path -Destination (Join-Path $frameworkDependentDir "uninstall.ps1") -Force
Copy-Item -LiteralPath $uninstallCmdPath -Destination (Join-Path $selfContainedDir "uninstall.cmd") -Force
Copy-Item -LiteralPath $uninstallPs1Path -Destination (Join-Path $selfContainedDir "uninstall.ps1") -Force

# Diagnostic: proves whether the bypass actually punches through, instead of
# leaving the user with an ambiguous "best effort" label in the UI.
Copy-Item -LiteralPath $checkBypassPath -Destination (Join-Path $frameworkDependentDir "check-bypass.ps1") -Force
Copy-Item -LiteralPath $checkBypassPath -Destination (Join-Path $selfContainedDir "check-bypass.ps1") -Force

# Ручной перебор: для игр обход нельзя подтвердить автоматически - веб-адреса
# отвечают и без него, а зайти в матч скрипт не может.
Copy-Item -LiteralPath $tryStrategyPath -Destination (Join-Path $frameworkDependentDir "try-strategy.ps1") -Force
Copy-Item -LiteralPath $tryStrategyPath -Destination (Join-Path $selfContainedDir "try-strategy.ps1") -Force

# Трассировка: показывает, куда реально ходит игра и что из этого не отвечает.
# Нужна там, где проверка известных адресов ничего не даёт.
Copy-Item -LiteralPath $traceGamePath -Destination (Join-Path $frameworkDependentDir "trace-game.ps1") -Force
Copy-Item -LiteralPath $traceGamePath -Destination (Join-Path $selfContainedDir "trace-game.ps1") -Force

@"
Chillistica_game $Version

1. Распакуйте архив в отдельную папку.
2. Дважды щёлкните run-first.cmd (или запустите Chillistica_game.exe напрямую).
3. Подтвердите запрос контроля учётных записей (UAC).
4. Нажмите единственную кнопку «Включить защиту».

Для этой версии требуется .NET 8 Desktop Runtime. Если он отсутствует, run-first.cmd установит его автоматически.

Чтобы полностью удалить программу: закройте её и запустите uninstall.cmd из этой же папки
(или нажмите «Удалить программу» внутри приложения) — он остановит движок, снимет
драйвер WinDivert и почистит логи/настройки. Папку после этого удалите вручную.
"@ | Set-Content -LiteralPath (Join-Path $frameworkDependentDir "README_FIRST.txt") -Encoding UTF8

@"
Chillistica_game $Version

1. Распакуйте архив в отдельную папку.
2. Дважды щёлкните run-first.cmd (или запустите Chillistica_game.exe напрямую).
3. Подтвердите запрос контроля учётных записей (UAC).
4. Нажмите единственную кнопку «Включить защиту».

Чтобы полностью удалить программу: закройте её и запустите uninstall.cmd из этой же папки
(или нажмите «Удалить программу» внутри приложения) — он остановит движок, снимет
драйвер WinDivert и почистит логи/настройки. Папку после этого удалите вручную.
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
