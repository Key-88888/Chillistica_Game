param(
    [string]$ServiceName = "Chillistica_game.Service",
    [switch]$RemoveFiles
)

$ErrorActionPreference = "Stop"

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script from PowerShell as Administrator."
    }
}

function Get-ServiceExePath {
    param([string]$Name)

    $svc = Get-CimInstance Win32_Service -Filter "Name='$Name'"
    if (-not $svc) {
        return $null
    }

    $path = $svc.PathName.Trim()

    if ($path.StartsWith('"')) {
        $path = $path.Substring(1)
        $path = $path.Substring(0, $path.IndexOf('"'))
    }
    else {
        $path = $path.Split(" ")[0]
    }

    return $path
}

Assert-Admin

$exePath = Get-ServiceExePath -Name $ServiceName
$installDir = $null

if ($exePath) {
    $installDir = Split-Path $exePath -Parent
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($svc) {
    if ($svc.Status -ne "Stopped") {
        Write-Host "Stopping service..." -ForegroundColor Cyan
        Stop-Service $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    Write-Host "Deleting service..." -ForegroundColor Cyan
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}
else {
    Write-Host "Service '$ServiceName' is not installed." -ForegroundColor Yellow
}

if ($RemoveFiles -and $installDir -and (Test-Path $installDir)) {
    Write-Host "Removing service files: $installDir" -ForegroundColor Cyan
    Remove-Item $installDir -Recurse -Force
}
else {
    Write-Host "Service files were preserved." -ForegroundColor Yellow
    if ($installDir) {
        Write-Host $installDir
    }
}

Write-Host "Settings and logs were not removed." -ForegroundColor Green
Write-Host "Uninstall completed." -ForegroundColor Green
