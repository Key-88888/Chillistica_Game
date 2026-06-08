param(
    [string]$ProfilePath = ".\Engine\winws2\profiles\youtube-https.json"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedProfilePath = Join-Path $projectRoot $ProfilePath

if (-not (Test-Path $resolvedProfilePath)) {
    throw "Profile not found: $resolvedProfilePath"
}

$profile = Get-Content `
    -Path $resolvedProfilePath `
    -Raw |
    ConvertFrom-Json

$errors = New-Object System.Collections.Generic.List[string]
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Details
    )

    $checks.Add(
        [PSCustomObject]@{
            Check   = $Name
            Passed  = $Passed
            Details = $Details
        }
    )

    if (-not $Passed) {
        $errors.Add("$Name`: $Details")
    }
}

Add-Check `
    -Name "ProfileId" `
    -Passed ($profile.ProfileId -eq "winws2-youtube-https") `
    -Details "Actual=$($profile.ProfileId)"

Add-Check `
    -Name "RequiresAdmin" `
    -Passed ($profile.RequiresAdmin -eq $true) `
    -Details "Actual=$($profile.RequiresAdmin)"

Add-Check `
    -Name "UsesWinDivert" `
    -Passed ($profile.UsesWinDivert -eq $true) `
    -Details "Actual=$($profile.UsesWinDivert)"

Add-Check `
    -Name "AllowUnsafeStart" `
    -Passed ($profile.AllowUnsafeStart -eq $false) `
    -Details "Actual=$($profile.AllowUnsafeStart)"

$arguments = [string]$profile.Arguments

Add-Check `
    -Name "TCP443Only" `
    -Passed (
        $arguments -match '--wf-tcp-out=443' -and
        $arguments -match '--filter-tcp=443' -and
        $arguments -match '--filter-l7=tls'
    ) `
    -Details $arguments

Add-Check `
    -Name "NoUDP" `
    -Passed (
        $arguments -notmatch '--filter-udp' -and
        $arguments -notmatch '--wf-udp' -and
        $arguments -notmatch 'quic'
    ) `
    -Details "UDP and QUIC arguments must be absent"

Add-Check `
    -Name "NoGameOrVoiceFilters" `
    -Passed (
        $arguments -notmatch 'discord' -and
        $arguments -notmatch 'wireguard' -and
        $arguments -notmatch 'stun' -and
        $arguments -notmatch 'roblox' -and
        $arguments -notmatch 'fortnite'
    ) `
    -Details "Discord, WireGuard, STUN, Roblox and Fortnite must be absent"

foreach ($hashEntry in @($profile.FileHashes)) {
    $filePath = Join-Path $projectRoot $hashEntry.Path
    $exists = Test-Path $filePath

    Add-Check `
        -Name "FileExists:$($hashEntry.Path)" `
        -Passed $exists `
        -Details $filePath

    if ($exists) {
        $actualHash = (
            Get-FileHash `
                -Path $filePath `
                -Algorithm SHA256
        ).Hash

        Add-Check `
            -Name "SHA256:$($hashEntry.Path)" `
            -Passed ($actualHash -eq $hashEntry.Sha256) `
            -Details "Actual=$actualHash Expected=$($hashEntry.Sha256)"
    }
}

Write-Host ""
Write-Host "===== WINWS2 PROFILE VALIDATION =====" -ForegroundColor Cyan

$checks |
    Format-Table -AutoSize -Wrap

Write-Host ""
Write-Host "Checks: $($checks.Count)"
Write-Host "Failed: $($errors.Count)"

if ($errors.Count -gt 0) {
    Write-Host ""
    Write-Host "PROFILE INVALID" -ForegroundColor Red

    foreach ($errorItem in $errors) {
        Write-Host $errorItem -ForegroundColor Red
    }

    exit 1
}

Write-Host ""
Write-Host "PROFILE VALID" -ForegroundColor Green
Write-Host "No process was started."
Write-Host "WinDivert was not loaded."
