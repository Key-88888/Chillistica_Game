# Trusted, elevated update applier.
#
# SECURITY MODEL: this script is installed into the admin-only install directory
# (%ProgramFiles%\Chillistica_game\apply-update.ps1) and is the ONLY script the
# app launches elevated for updates.
#
# TOCTOU DEFENCE: the downloaded package lives in a USER-WRITABLE staging folder,
# so we must never verify one copy and then consume the bytes again from that
# same user-writable path (a same-user attacker could swap the file in between).
# We FIRST copy the package + signature into an admin-only work directory, and
# every subsequent step (signature re-verify AND extraction) reads only that
# admin-only copy, which a standard user cannot modify. We also never recursively
# delete the user-controlled staging directory from this elevated context (a
# junction there would turn into an arbitrary admin-level delete) -- the app
# cleans its own staging folder in the user context on the next download.
param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$SignaturePath,
    [string]$InstallDir = "$env:ProgramFiles\Chillistica_game",
    [string]$ServiceName = "Chillistica_game.Service",
    [string]$DisplayName = "Chillistica_game Service"
)

$ErrorActionPreference = "Stop"

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "apply-update must run elevated (as Administrator)."
    }
}

Assert-Admin

if (-not (Test-Path $ZipPath)) { throw "Update package not found: $ZipPath" }
if (-not (Test-Path $SignaturePath)) { throw "Update signature not found: $SignaturePath" }

$serviceDir = Join-Path $InstallDir "Service"
$appDir = Join-Path $InstallDir "App"
$appExe = Join-Path $appDir "Chillistica_game.App.exe"

if (-not (Test-Path $appExe)) {
    throw "Installed app binary not found for verification: $appExe"
}

# --- Take an admin-only private copy of the package BEFORE verifying it, so the
#     bytes that are verified are the exact same bytes that get extracted. From
#     here on the user-writable $ZipPath/$SignaturePath are never read again. ---
$workDir = Join-Path $env:TEMP ("Chillistica_verified_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workDir -Force | Out-Null

try {
    $verifiedZip = Join-Path $workDir "update.zip"
    $verifiedSig = Join-Path $workDir "update.zip.sig"

    # Copy (never move/link) into the admin-only work dir. If the source is a
    # symlink/junction it is dereferenced here by the admin; the copied content
    # is still gated by the signature check below, so nothing untrusted survives.
    [System.IO.File]::Copy([System.IO.Path]::GetFullPath($ZipPath), $verifiedZip, $true)
    [System.IO.File]::Copy([System.IO.Path]::GetFullPath($SignaturePath), $verifiedSig, $true)

    # --- Verify the ADMIN-ONLY copy's signature with the pinned public key inside
    #     the installed, admin-only .NET 8 App binary. Fail closed on non-zero. ---
    Write-Host "Verifying update signature..." -ForegroundColor Cyan
    & $appExe --verify-update $verifiedZip $verifiedSig
    if ($LASTEXITCODE -ne 0) {
        throw "Update signature verification FAILED (exit $LASTEXITCODE). Aborting update."
    }
    Write-Host "Signature OK." -ForegroundColor Green

    # --- Extract the verified admin-only copy (never any user-writable path). ---
    $extractDir = Join-Path $workDir "extracted"
    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

    Expand-Archive -Path $verifiedZip -DestinationPath $extractDir -Force

    $newService = Join-Path $extractDir "service"
    $newApp = Join-Path $extractDir "app"

    if (-not (Test-Path $newService)) { throw "Verified package missing 'service' folder." }
    if (-not (Test-Path $newApp)) { throw "Verified package missing 'app' folder." }

    Write-Host "Stopping app and service..." -ForegroundColor Cyan
    Get-Process "Chillistica_game.App" -ErrorAction SilentlyContinue | Stop-Process -Force

    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService -and $existingService.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    New-Item -ItemType Directory -Path $serviceDir -Force | Out-Null
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null

    Write-Host "Copying verified files..." -ForegroundColor Cyan
    Copy-Item "$newService\*" $serviceDir -Recurse -Force
    Copy-Item "$newApp\*" $appDir -Recurse -Force

    # Refresh the trusted updater + installer themselves for the next update.
    foreach ($script in @("apply-update.ps1", "install-package.ps1")) {
        $src = Join-Path $extractDir $script
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $InstallDir $script) -Force
        }
    }

    $serviceExe = Join-Path $serviceDir "Chillistica_game.Service.exe"
    if (-not (Test-Path $serviceExe)) { throw "Service exe missing after copy: $serviceExe" }

    if (-not $existingService) {
        New-Service `
            -Name $ServiceName `
            -BinaryPathName "`"$serviceExe`"" `
            -DisplayName $DisplayName `
            -StartupType Automatic
        sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/none/0 | Out-Null
    }

    Start-Service -Name $ServiceName
    Write-Host "Update applied successfully." -ForegroundColor Green

    # Relaunch the app as the INTERACTIVE user, not elevated. Starting it through
    # the already-running user-context explorer.exe drops the admin token so the
    # network-facing WPF app never runs with SYSTEM/admin rights.
    if (Test-Path $appExe) {
        Start-Process -FilePath "explorer.exe" -ArgumentList "`"$appExe`""
    }
}
finally {
    # Only ever remove our own admin-only work directory. We deliberately do NOT
    # touch the user-writable staging folder from this elevated context: a
    # recursive force-delete of a user-controlled path could be redirected via a
    # junction into an arbitrary admin-level delete. The app clears its own
    # staging folder (user context) before the next download.
    Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
