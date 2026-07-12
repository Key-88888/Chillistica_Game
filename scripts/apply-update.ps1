# Trusted, elevated update applier.
#
# SECURITY MODEL: this script is installed into the admin-only install directory
# (%ProgramFiles%\Chillistica_game\apply-update.ps1) and is the ONLY script the
# app launches elevated for updates. It re-verifies the downloaded package's
# detached signature against the pinned public key BEFORE touching anything, by
# delegating to the installed, code-signed App binary (--verify-update). A
# standard user cannot tamper this script or that binary (admin-only ACL), so a
# user-writable-staging swap of the downloaded payload cannot escalate to admin.
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

# --- Re-verify the signature using the pinned public key inside the installed,
#     admin-only .NET 8 App binary. Fail closed on anything other than exit 0. ---
Write-Host "Verifying update signature..." -ForegroundColor Cyan
& $appExe --verify-update $ZipPath $SignaturePath
if ($LASTEXITCODE -ne 0) {
    throw "Update signature verification FAILED (exit $LASTEXITCODE). Aborting update."
}
Write-Host "Signature OK." -ForegroundColor Green

# --- Extract the verified package fresh into an admin-only temp directory (never
#     trust any pre-extracted, user-writable content). ---
$extractDir = Join-Path $env:TEMP ("Chillistica_verified_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

try {
    Expand-Archive -Path $ZipPath -DestinationPath $extractDir -Force

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

    if (Test-Path $appExe) {
        Start-Process -FilePath $appExe
    }
}
finally {
    Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
    # Clean up the (now-consumed) downloaded staging payload.
    Remove-Item (Split-Path -Parent $ZipPath) -Recurse -Force -ErrorAction SilentlyContinue
}
