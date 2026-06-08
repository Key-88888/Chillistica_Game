param(
    [Parameter(Mandatory = $true)]
    [string]$ProfilePath
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$appsettingsPath = Join-Path `
    $projectRoot `
    "Chillistica_game.Service\appsettings.json"

$resolvedProfilePath = Join-Path `
    $projectRoot `
    $ProfilePath

Write-Host ""
Write-Host "===== SET ENGINE PROFILE =====" -ForegroundColor Cyan
Write-Host "Requested profile: $ProfilePath"

if (-not (Test-Path $appsettingsPath)) {
    throw "appsettings.json not found: $appsettingsPath"
}

if (-not (Test-Path $resolvedProfilePath)) {
    throw "Profile file not found: $resolvedProfilePath"
}

$profile = Get-Content `
    -Path $resolvedProfilePath `
    -Raw |
    ConvertFrom-Json

$errors = New-Object System.Collections.Generic.List[string]

function Add-Error {
    param([string]$Message)

    $errors.Add($Message)
}

if ($profile.SchemaVersion -ne 1) {
    Add-Error "Unsupported SchemaVersion: $($profile.SchemaVersion)"
}

if ([string]::IsNullOrWhiteSpace($profile.ProfileId)) {
    Add-Error "ProfileId is empty"
}

if ([string]::IsNullOrWhiteSpace($profile.ExecutablePath)) {
    Add-Error "ExecutablePath is empty"
}

if ([string]::IsNullOrWhiteSpace($profile.WorkingDirectory)) {
    Add-Error "WorkingDirectory is empty"
}

if ($profile.StopTimeoutSeconds -lt 1 -or $profile.StopTimeoutSeconds -gt 60) {
    Add-Error "StopTimeoutSeconds must be between 1 and 60"
}

if ($profile.KillTimeoutSeconds -lt 1 -or $profile.KillTimeoutSeconds -gt 60) {
    Add-Error "KillTimeoutSeconds must be between 1 and 60"
}

if ($profile.AllowUnsafeStart -eq $true) {
    Add-Error "AllowUnsafeStart=true is forbidden by this script"
}

$hashEntries = @(
    $profile.FileHashes |
    Where-Object {
        $_ -ne $null -and (
            -not [string]::IsNullOrWhiteSpace($_.Path) -or
            -not [string]::IsNullOrWhiteSpace($_.Sha256)
        )
    }
)

foreach ($hashEntry in $hashEntries) {
    if ([string]::IsNullOrWhiteSpace($hashEntry.Path)) {
        Add-Error "FileHashes contains empty Path"
        continue
    }

    if ([string]::IsNullOrWhiteSpace($hashEntry.Sha256)) {
        Add-Error "FileHashes contains empty Sha256 for $($hashEntry.Path)"
        continue
    }

    $hashFilePath = Join-Path `
        $projectRoot `
        $hashEntry.Path

    if (-not (Test-Path $hashFilePath)) {
        Add-Error "Hash file not found: $($hashEntry.Path)"
        continue
    }

    $actualHash = (
        Get-FileHash `
            -Path $hashFilePath `
            -Algorithm SHA256
    ).Hash

    if ($actualHash -ne $hashEntry.Sha256) {
        Add-Error "SHA256 mismatch for $($hashEntry.Path)"
    }
}

Write-Host ""
Write-Host "===== PROFILE SAFETY =====" -ForegroundColor Cyan

[PSCustomObject]@{
    ProfileId        = $profile.ProfileId
    RequiresAdmin    = $profile.RequiresAdmin
    UsesWinDivert     = $profile.UsesWinDivert
    AllowUnsafeStart = $profile.AllowUnsafeStart
    FileHashesCount  = $hashEntries.Count
} | Format-List

if ($errors.Count -gt 0) {
    Write-Host ""
    Write-Host "PROFILE SWITCH BLOCKED" -ForegroundColor Red

    foreach ($errorItem in $errors) {
        Write-Host $errorItem -ForegroundColor Red
    }

    exit 1
}

$appsettings = Get-Content `
    -Path $appsettingsPath `
    -Raw |
    ConvertFrom-Json

$oldProfilePath = $appsettings.EngineProfile.ActiveProfilePath

$backupPath = "$appsettingsPath.backup-set-engine-profile-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

Copy-Item `
    -Path $appsettingsPath `
    -Destination $backupPath `
    -Force

$appsettings.EngineProfile.ActiveProfilePath = $ProfilePath

$appsettings |
    ConvertTo-Json -Depth 20 |
    Set-Content `
        -Path $appsettingsPath `
        -Encoding UTF8

Write-Host ""
Write-Host "PROFILE SWITCHED" -ForegroundColor Green
Write-Host "Old: $oldProfilePath"
Write-Host "New: $ProfilePath"
Write-Host "Backup: $backupPath"
Write-Host ""
Write-Host "No process was started."
Write-Host "WinDivert was not loaded."

