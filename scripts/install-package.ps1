param(
    [string]$InstallDir = "$env:ProgramFiles\Chillistica_game",
    [string]$ServiceName = "Chillistica_game.Service",
    [string]$DisplayName = "Chillistica_game Service",
    [switch]$Silent
)

$ErrorActionPreference = "Stop"

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Restart-AsAdmin {
    if (Test-Admin) {
        return
    }

    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-InstallDir", "`"$InstallDir`""
    )

    if ($Silent) {
        $args += "-Silent"
    }

    Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList $args `
        -Verb RunAs

    exit 0
}

function Wait-ServiceRunning {
    param(
        [string]$Name,
        [int]$Seconds = 20
    )

    for ($i = 1; $i -le $Seconds; $i++) {
        $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue

        if ($svc -and $svc.Status -eq "Running") {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Service did not reach Running state: $Name"
}

function Test-PipePing {
    param(
        [int]$TimeoutMs = 3000
    )

    $pipe = $null
    $reader = $null
    $writer = $null

    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
            ".",
            "Chillistica_game.Control",
            [System.IO.Pipes.PipeDirection]::InOut
        )

        $pipe.Connect($TimeoutMs)

        $reader = [System.IO.StreamReader]::new(
            $pipe,
            [System.Text.Encoding]::UTF8,
            $false,
            1024,
            $true
        )

        $writer = [System.IO.StreamWriter]::new(
            $pipe,
            [System.Text.UTF8Encoding]::new($false),
            1024,
            $true
        )

        $writer.AutoFlush = $true
        $writer.WriteLine("PING")

        $response = $reader.ReadLine()

        return $response -eq "PONG"
    }
    catch {
        return $false
    }
    finally {
        if ($reader) { $reader.Dispose() }
        if ($writer) { $writer.Dispose() }
        if ($pipe) { $pipe.Dispose() }
    }
}

function New-Shortcut {
    param(
        [string]$TargetPath,
        [string]$ShortcutPath,
        [string]$WorkingDirectory
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $TargetPath
    $shortcut.Save()
}

Restart-AsAdmin

$packageRoot = Split-Path -Parent $PSCommandPath

$serviceSource = Join-Path $packageRoot "service"
$appSource = Join-Path $packageRoot "app"

if (-not (Test-Path $serviceSource)) {
    throw "Package folder not found: service"
}

if (-not (Test-Path $appSource)) {
    throw "Package folder not found: app"
}

$serviceDir = Join-Path $InstallDir "Service"
$appDir = Join-Path $InstallDir "App"

Write-Host "Installing Chillistica_game..." -ForegroundColor Cyan

Get-Process "Chillistica_game.App" -ErrorAction SilentlyContinue |
    Stop-Process -Force

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($existingService -and $existingService.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
}

New-Item -ItemType Directory -Path $serviceDir -Force | Out-Null
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

Copy-Item "$serviceSource\*" $serviceDir -Recurse -Force
Copy-Item "$appSource\*" $appDir -Recurse -Force

$serviceExe = Join-Path $serviceDir "Chillistica_game.Service.exe"
$appExe = Join-Path $appDir "Chillistica_game.App.exe"

if (-not (Test-Path $serviceExe)) {
    throw "Service exe not found after copy: $serviceExe"
}

if (-not (Test-Path $appExe)) {
    throw "App exe not found after copy: $appExe"
}

if (-not $existingService) {
    New-Service `
        -Name $ServiceName `
        -BinaryPathName "`"$serviceExe`"" `
        -DisplayName $DisplayName `
        -StartupType Automatic

    sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/none/0 | Out-Null
}

Start-Service -Name $ServiceName
Wait-ServiceRunning -Name $ServiceName

$pipeOk = $false

for ($i = 1; $i -le 15; $i++) {
    if (Test-PipePing) {
        $pipeOk = $true
        break
    }

    Start-Sleep -Seconds 1
}

if (-not $pipeOk) {
    throw "Service is running but Named Pipe PING failed."
}

$desktop = [Environment]::GetFolderPath("DesktopDirectory")
$shortcutPath = Join-Path $desktop "Chillistica_game.lnk"

New-Shortcut `
    -TargetPath $appExe `
    -ShortcutPath $shortcutPath `
    -WorkingDirectory $appDir

Write-Host "Service installed and PING OK." -ForegroundColor Green
Write-Host "Shortcut created: $shortcutPath" -ForegroundColor Green

Start-Process -FilePath $appExe

Write-Host "Installation complete." -ForegroundColor Green

if (-not $Silent) {
    Read-Host "Press Enter to close"
}
