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

# Deploy the trusted updater + installer into the admin-only install root so the
# app can invoke apply-update.ps1 elevated for future updates (it re-verifies the
# signed package there, closing the user-writable-staging TOCTOU).
foreach ($script in @("apply-update.ps1", "install-package.ps1")) {
    $scriptSource = Join-Path $packageRoot $script
    if (Test-Path $scriptSource) {
        Copy-Item $scriptSource (Join-Path $InstallDir $script) -Force
    }
}

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

# Create shortcuts in the ALL-USERS (machine-wide) locations. The installer runs
# elevated, possibly under a different admin account than the everyday user, so a
# per-user Desktop path would land on the wrong desktop ("can't find shortcut").
# The common Desktop + Start-menu entry are visible to whoever is logged in.
$commonDesktop = [Environment]::GetFolderPath("CommonDesktopDirectory")
$commonPrograms = [Environment]::GetFolderPath("CommonPrograms")

$shortcutTargets = @()
if ($commonDesktop) { $shortcutTargets += (Join-Path $commonDesktop "Chillistica_game.lnk") }
if ($commonPrograms) { $shortcutTargets += (Join-Path $commonPrograms "Chillistica_game.lnk") }

foreach ($shortcutPath in $shortcutTargets) {
    New-Shortcut `
        -TargetPath $appExe `
        -ShortcutPath $shortcutPath `
        -WorkingDirectory $appDir

    Write-Host "Shortcut created: $shortcutPath" -ForegroundColor Green
}

Write-Host "Service installed and PING OK." -ForegroundColor Green
Write-Host "Installed to: $InstallDir" -ForegroundColor Green
Write-Host "Start menu / desktop entry: Chillistica_game" -ForegroundColor Green

# Launch the app as the INTERACTIVE user (not elevated). Starting it through the
# already-running user-context explorer.exe drops the admin token, so the
# network-facing WPF app never runs with admin rights.
Start-Process -FilePath "explorer.exe" -ArgumentList "`"$appExe`""

Write-Host "Installation complete." -ForegroundColor Green
Write-Host "Если приложение не открылось — запустите ярлык Chillistica_game с рабочего стола или из меню Пуск." -ForegroundColor Cyan

if (-not $Silent) {
    Read-Host "Press Enter to close"
}
