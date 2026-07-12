# Produces a detached RSA-SHA256 signature (<FilePath>.sig) for a release zip.
# Requires PowerShell 7+ (pwsh). Used by build-release.ps1 in CI and locally.
#
#   pwsh ./scripts/sign-release.ps1 -FilePath artifacts/release/....zip -PrivateKeyPath ./chillistica-signing-private.pem
param(
    [Parameter(Mandatory = $true)][string]$FilePath,
    [string]$PrivateKeyPem,
    [string]$PrivateKeyPath,
    [string]$OutPath
)

$ErrorActionPreference = "Stop"

if (-not $PrivateKeyPem -and $PrivateKeyPath) {
    $PrivateKeyPem = Get-Content -Path $PrivateKeyPath -Raw
}

if ([string]::IsNullOrWhiteSpace($PrivateKeyPem)) {
    throw "Provide -PrivateKeyPem or -PrivateKeyPath."
}

if (-not (Test-Path $FilePath)) {
    throw "File to sign not found: $FilePath"
}

if (-not $OutPath) {
    $OutPath = "$FilePath.sig"
}

$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem($PrivateKeyPem)

$data = [System.IO.File]::ReadAllBytes($FilePath)
$signature = $rsa.SignData(
    $data,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

[System.IO.File]::WriteAllBytes($OutPath, $signature)

Write-Host "Signature written: $OutPath" -ForegroundColor Green
