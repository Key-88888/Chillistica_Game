# Generates an offline RSA signing keypair for the update channel.
# Run ONCE, locally, on PowerShell 7+ (pwsh). Keep the PRIVATE key OFFLINE.
#
#   pwsh ./scripts/new-signing-key.ps1 -OutDir .
#
# Then:
#   1. Paste the printed PUBLIC key into
#      Chillistica_game.App/Services/UpdateSignatureVerifier.cs (PublicKeyPem).
#   2. Store the PRIVATE key PEM as the GitHub Actions secret
#      CHILLISTICA_SIGNING_KEY_PEM (Settings > Secrets > Actions).
#   3. NEVER commit the private key.
param(
    [string]$OutDir = "."
)

$ErrorActionPreference = "Stop"

function Format-Pem {
    param([string]$Label, [byte[]]$Der)
    $b64 = [Convert]::ToBase64String($Der)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("-----BEGIN $Label-----")
    for ($i = 0; $i -lt $b64.Length; $i += 64) {
        $len = [Math]::Min(64, $b64.Length - $i)
        [void]$sb.AppendLine($b64.Substring($i, $len))
    }
    [void]$sb.AppendLine("-----END $Label-----")
    return $sb.ToString()
}

$rsa = [System.Security.Cryptography.RSA]::Create(3072)

$publicPem = Format-Pem -Label "PUBLIC KEY" -Der $rsa.ExportSubjectPublicKeyInfo()
$privatePem = Format-Pem -Label "PRIVATE KEY" -Der $rsa.ExportPkcs8PrivateKey()

$publicPath = Join-Path $OutDir "chillistica-signing-public.pem"
$privatePath = Join-Path $OutDir "chillistica-signing-private.pem"

Set-Content -Path $publicPath -Value $publicPem -Encoding ascii -NoNewline
Set-Content -Path $privatePath -Value $privatePem -Encoding ascii -NoNewline

Write-Host "Private key written to: $privatePath  (KEEP OFFLINE, do NOT commit)" -ForegroundColor Yellow
Write-Host "Public key written to : $publicPath" -ForegroundColor Green
Write-Host ""
Write-Host "Paste this PUBLIC key into UpdateSignatureVerifier.PublicKeyPem:" -ForegroundColor Cyan
Write-Host $publicPem
