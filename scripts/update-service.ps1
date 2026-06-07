param(
    [string]$ProjectRoot = "$env:USERPROFILE\Chillistica_game",
    [string]$ServiceName = "Chillistica_game.Service"
)

$ErrorActionPreference = "Stop"

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script from PowerShell as Administrator."
    }
}

function Test-PipePing {
    param(
        [int]$TimeoutMs = 3000
    )

    $pipe = $null
    $writer = $null
    $reader = $null

    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
            ".",
            "Chillistica_game.Control",
            [System.IO.Pipes.PipeDirection]::InOut
        )

        $pipe.Connect($TimeoutMs)

        $writer = [System.IO.StreamWriter]::new(
            $pipe,
            [System.Text.Encoding]::UTF8,
            1024,
            $true
        )
        $writer.AutoFlush = $true

        $reader = [System.IO.StreamReader]::new(
            $pipe,
            [System.Text.Encoding]::UTF8,
            $true,
            1024,
            $true
        )

        $writer.WriteLine("PING")
        $response = $reader.ReadLine()

        return ($response -eq "PONG")
    }
    catch {
        Write-Host `
            "Named Pipe check failed: $($_.Exception.Message)" `
            -ForegroundColor DarkYellow

        return $false
    }
    finally {
        if ($reader) {
            $reader.Dispose()
        }

        if ($writer) {
            $writer.Dispose()
        }

        if ($pipe) {
            $pipe.Dispose()
        }
    }
}

function Get-ServiceExePath {
    param([string]$Name)

    $svc = Get-CimInstance Win32_Service -Filter "Name='$Name'"
    if (-not $svc) {
        throw "Service '$Name' not found."
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

$serviceProject = Join-Path $ProjectRoot "Chillistica_game.Service\Chillistica_game.Service.csproj"
$publishTemp = Join-Path $ProjectRoot "artifacts\Service-update-temp"

if (-not (Test-Path $serviceProject)) {
    throw "Service project not found: $serviceProject"
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    throw "Service '$ServiceName' is not installed. Use scripts\install-service.ps1 first."
}

$exePath = Get-ServiceExePath -Name $ServiceName
$installDir = Split-Path $exePath -Parent

if (-not (Test-Path $installDir)) {
    throw "Installed service directory not found: $installDir"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = Join-Path $ProjectRoot "backup-service-update-$timestamp"

Write-Host "Publishing service update..." -ForegroundColor Cyan

if (Test-Path $publishTemp) {
    Remove-Item $publishTemp -Recurse -Force
}

dotnet publish `
    $serviceProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishTemp

if (-not (Test-Path (Join-Path $publishTemp "Chillistica_game.Service.exe"))) {
    throw "Publish failed: service exe was not created."
}

Write-Host "Backing up current service files..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
Copy-Item "$installDir\*" $backupDir -Recurse -Force

try {
    Write-Host "Stopping service..." -ForegroundColor Cyan
    Stop-Service $ServiceName -Force
    Start-Sleep -Seconds 2

    Write-Host "Replacing service files..." -ForegroundColor Cyan
    Copy-Item "$publishTemp\*" $installDir -Recurse -Force

    Write-Host "Starting service..." -ForegroundColor Cyan
    Start-Service $ServiceName

    $serviceRunning = $false

    for ($attempt = 1; $attempt -le 15; $attempt++) {
        Start-Sleep -Seconds 1

        $state = (
            Get-CimInstance Win32_Service `
                -Filter "Name='$ServiceName'"
        ).State

        Write-Host `
            "Service readiness check $attempt/15: state=$state" `
            -ForegroundColor DarkCyan

        if ($state -eq "Running") {
            $serviceRunning = $true

            if (Test-PipePing -TimeoutMs 2000) {
                Write-Host `
                    "Named Pipe PING succeeded on attempt $attempt." `
                    -ForegroundColor Green

                break
            }
        }
    }

    if (-not $serviceRunning) {
        throw "Service did not reach Running state after update."
    }

    if (-not (Test-PipePing -TimeoutMs 3000)) {
        throw "Service is running but Named Pipe PING did not become ready within 15 seconds."
    }

    Write-Host "Service updated and PING OK." -ForegroundColor Green
    Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" |
        Select-Object Name, State, StartMode, ProcessId, PathName
}
catch {
    Write-Host "Update failed. Rolling back..." -ForegroundColor Yellow

    try {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Copy-Item "$backupDir\*" $installDir -Recurse -Force
        Start-Service $ServiceName
        Start-Sleep -Seconds 2
    }
    catch {
        Write-Host "Rollback also failed. Manual check required." -ForegroundColor Red
    }

    throw
}


