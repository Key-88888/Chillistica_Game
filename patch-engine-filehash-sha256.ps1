$ErrorActionPreference = "Stop"

Set-Location "$env:USERPROFILE\Chillistica_game"

Get-Process "Chillistica_game.App" -ErrorAction SilentlyContinue |
    Stop-Process -Force

$backup = ".\backup-engine-filehash-sha256-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory -Path $backup -Force | Out-Null

$loaderPath = ".\Chillistica_game.Service\EngineProfileLoader.cs"
Copy-Item $loaderPath $backup -Force

$text = Get-Content $loaderPath -Raw

if ($text -notmatch 'using System\.Security\.Cryptography;') {
    $text = $text.Replace(
        'using System.Text.Json;',
        "using System.Security.Cryptography;`r`nusing System.Text.Json;")
}

$startMarker = '    private static void ValidateFileHashes('
$endMarker = '    private static void ValidateOptions('

$start = $text.IndexOf($startMarker)
$end = $text.IndexOf($endMarker)

if ($start -lt 0) {
    throw "ValidateFileHashes start marker was not found."
}

if ($end -lt 0) {
    throw "ValidateOptions end marker was not found."
}

if ($end -le $start) {
    throw "ValidateFileHashes markers are in unexpected order."
}

$newMethod = @'
    private static void ValidateFileHashes(
        IReadOnlyList<EngineFileHash> fileHashes,
        string profilePath)
    {
        foreach (EngineFileHash fileHash in fileHashes)
        {
            if (string.IsNullOrWhiteSpace(
                    fileHash.Path))
            {
                throw new InvalidDataException(
                    $"FileHashes item has empty Path in '{profilePath}'.");
            }

            if (string.IsNullOrWhiteSpace(
                    fileHash.Sha256))
            {
                throw new InvalidDataException(
                    $"FileHashes item has empty Sha256 for '{fileHash.Path}' in '{profilePath}'.");
            }

            string normalizedExpectedHash =
                fileHash.Sha256
                    .Trim()
                    .ToUpperInvariant();

            if (normalizedExpectedHash.Length != 64 ||
                normalizedExpectedHash.Any(character =>
                    !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException(
                    $"FileHashes item has invalid Sha256 for '{fileHash.Path}' in '{profilePath}'.");
            }

            string resolvedFilePath =
                ResolveEnginePath(
                    fileHash.Path);

            if (!File.Exists(resolvedFilePath))
            {
                if (fileHash.Required)
                {
                    throw new FileNotFoundException(
                        $"Required file hash target was not found: {fileHash.Path}",
                        resolvedFilePath);
                }

                continue;
            }

            string actualHash =
                ComputeSha256(
                    resolvedFilePath);

            if (!actualHash.Equals(
                    normalizedExpectedHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SHA256 mismatch for '{fileHash.Path}' in '{profilePath}'. Expected '{normalizedExpectedHash}', actual '{actualHash}'.");
            }
        }
    }

    private static string ComputeSha256(
        string filePath)
    {
        byte[] bytes =
            File.ReadAllBytes(
                filePath);

        byte[] hash =
            SHA256.HashData(
                bytes);

        return Convert.ToHexString(
            hash);
    }

