using System.Security.Cryptography;
using System.Text.Json;

namespace Chillistica_game.Service;

public static class EngineTrustManifestLoader
{
    private const string ManifestRelativePath =
        "Engine\\winws2\\trusted-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

    public static bool IsExecutableTrusted(
        string resolvedExecutablePath)
    {
        EngineTrustManifest? manifest =
            TryLoadManifest();

        if (manifest is null)
        {
            return false;
        }

        foreach (EngineFileHash trustedBinary in manifest.TrustedBinaries)
        {
            string resolvedTrustedPath =
                ResolvePath(
                    trustedBinary.Path);

            if (!string.Equals(
                    resolvedTrustedPath,
                    resolvedExecutablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!File.Exists(resolvedTrustedPath))
            {
                return false;
            }

            string actualHash =
                ComputeSha256(
                    resolvedTrustedPath);

            return actualHash.Equals(
                trustedBinary.Sha256.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static IReadOnlyList<EngineFileHash> GetTrustedBinaryHashes()
    {
        EngineTrustManifest? manifest =
            TryLoadManifest();

        return manifest?.TrustedBinaries
            ?? Array.Empty<EngineFileHash>();
    }

    private static EngineTrustManifest? TryLoadManifest()
    {
        string manifestPath =
            ResolvePath(
                ManifestRelativePath);

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            string json =
                File.ReadAllText(
                    manifestPath);

            return JsonSerializer.Deserialize<EngineTrustManifest>(
                json,
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolvePath(
        string relativeOrAbsolutePath)
    {
        string expanded =
            Environment.ExpandEnvironmentVariables(
                relativeOrAbsolutePath.Trim());

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(
                expanded);
        }

        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                expanded));
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
}
