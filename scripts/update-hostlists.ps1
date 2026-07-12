# Re-vendors the per-app winws hostlists from a maintained upstream domain-list
# project and re-pins their SHA256 in the strategy/profile JSONs, so the change
# rides the existing SIGNED release channel (the client only ever accepts pinned,
# signed lists). Hostlists are DATA ONLY -- they select which hosts get desynced,
# never what winws does -- so unlike strategy arguments they are safe to automate.
#
# Deterministic output (normalized + sorted + LF) so an unchanged upstream never
# produces a spurious release.
#
#   pwsh ./scripts/update-hostlists.ps1                # update in place
#   pwsh ./scripts/update-hostlists.ps1 -WhatIfOnly    # report, do not write
#
# Source: itdoginfo/allow-domains (Services/*.lst). Fortnite/Epic has no upstream
# list there, so list-fortnite.txt is intentionally left as-is (hand-maintained).
param(
    [string]$BaseUrl = "https://raw.githubusercontent.com/itdoginfo/allow-domains/main/Services",
    [switch]$WhatIfOnly
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoRoot   = Split-Path -Parent $PSScriptRoot
$filesDir   = Join-Path $repoRoot "Engine\winws2\files"
$strategies = Join-Path $repoRoot "Engine\winws2\strategies"
$profiles   = Join-Path $repoRoot "Engine\winws2\profiles"

# service (upstream .lst) -> local hostlist filename
$mappings = @(
    @{ Service = "youtube"; Local = "list-youtube.txt" },
    @{ Service = "discord"; Local = "list-discord.txt" },
    @{ Service = "roblox";  Local = "list-roblox.txt"  }
)

$domainRegex = '^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$'

function Get-NormalizedDomains {
    param([string]$RawText, [string]$Service)

    if ($RawText -match '<html' -or $RawText -match '<!DOCTYPE') {
        throw "Upstream returned HTML (not a domain list) for '$Service'. Refusing."
    }

    $domains = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($line in ($RawText -split "`n")) {
        $d = $line.Trim().ToLowerInvariant()
        if ($d.Length -eq 0 -or $d.StartsWith("#")) { continue }
        if ($d -notmatch $domainRegex) {
            throw "Upstream line for '$Service' is not a bare domain: '$d'. Refusing."
        }
        [void]$domains.Add($d)
    }

    if ($domains.Count -lt 1)      { throw "Upstream list for '$Service' is empty. Refusing." }
    if ($domains.Count -gt 200000) { throw "Upstream list for '$Service' is implausibly large ($($domains.Count)). Refusing." }

    # Ordinal sort for byte-stable output on any OS / PowerShell version.
    $sorted = [System.Collections.Generic.List[string]]::new($domains)
    $sorted.Sort([System.StringComparer]::Ordinal)
    return $sorted
}

function Get-Sha256Hex {
    param([string]$Path)
    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

# NewHashes: filename -> uppercase sha256; used to re-pin JSON FileHashes.
function Update-PinnedHashes {
    param([hashtable]$NewHashes, [string]$JsonPath)

    $lines = [System.IO.File]::ReadAllLines($JsonPath)
    $pending = $null
    $changed = $false

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        if ($line -match '"Path"\s*:\s*".*\\([^\\"]+)"') {
            $fname = $Matches[1]
            if ($NewHashes.ContainsKey($fname)) { $pending = $NewHashes[$fname] } else { $pending = $null }
            continue
        }

        if ($pending -and $line -match '("Sha256"\s*:\s*")([0-9A-Fa-f]{64})(")') {
            $newLine = $line -replace '("Sha256"\s*:\s*")([0-9A-Fa-f]{64})(")', ('${1}' + $pending + '${3}')
            if ($newLine -ne $line) { $lines[$i] = $newLine; $changed = $true }
            $pending = $null
        }
    }

    if ($changed -and -not $WhatIfOnly) {
        [System.IO.File]::WriteAllLines($JsonPath, $lines)
    }
    return $changed
}

$newHashes = @{}
$anyListChanged = $false

foreach ($m in $mappings) {
    $url   = "$BaseUrl/$($m.Service).lst"
    $local = Join-Path $filesDir $m.Local

    Write-Host "Fetching $url ..." -ForegroundColor Cyan
    $raw = (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30).Content

    $domains = Get-NormalizedDomains -RawText $raw -Service $m.Service
    # LF line endings + trailing newline -> byte-stable hash on any OS.
    $content = ($domains -join "`n") + "`n"

    $oldContent = if (Test-Path $local) { [System.IO.File]::ReadAllText($local) } else { "" }
    $oldContentLf = $oldContent -replace "`r`n", "`n"

    if ($oldContentLf -ne $content) {
        Write-Host "  CHANGED $($m.Local) ($($domains.Count) domains)" -ForegroundColor Yellow
        $anyListChanged = $true
        if (-not $WhatIfOnly) {
            [System.IO.File]::WriteAllText($local, $content, (New-Object System.Text.UTF8Encoding($false)))
        }
    } else {
        Write-Host "  unchanged $($m.Local) ($($domains.Count) domains)"
    }

    # Hash of the (new or existing) content for re-pinning.
    if ($WhatIfOnly) {
        $stream = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($content))
        $sha = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToUpperInvariant()
    } else {
        $sha = Get-Sha256Hex -Path $local
    }
    $newHashes[$m.Local] = $sha
}

Write-Host ""
Write-Host "Re-pinning hashes in strategy/profile JSONs..." -ForegroundColor Cyan

$jsonChanged = $false
foreach ($json in @(
        Get-ChildItem $strategies -Filter *.json -ErrorAction SilentlyContinue
        Get-ChildItem $profiles   -Filter *.json -ErrorAction SilentlyContinue)) {
    if (Update-PinnedHashes -NewHashes $newHashes -JsonPath $json.FullName) {
        Write-Host "  re-pinned $($json.Name)"
        $jsonChanged = $true
    }
}

$changed = $anyListChanged -or $jsonChanged
Write-Host ""
Write-Host ("RESULT changed={0}" -f $changed.ToString().ToLowerInvariant())

# Emit a GitHub Actions output when running in CI.
if ($env:GITHUB_OUTPUT) {
    "changed=$($changed.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
