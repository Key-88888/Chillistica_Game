using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Chillistica_game.Service;

public static class EngineProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

    public static EngineOptions LoadAndValidateExplicit(
        string profilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            profilePath);

        EngineProfile profile =
            LoadProfile(
                profilePath);

        ValidateProfile(
            profile,
            profilePath);

        return ConvertProfileToOptions(
            profile);
    }

    public static EngineOptions LoadOrFallback(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(
            configuration);

        EngineProfileSelectionOptions selection =
            configuration
                .GetSection(
                    EngineProfileSelectionOptions.SectionName)
                .Get<EngineProfileSelectionOptions>()
            ?? new EngineProfileSelectionOptions();

        string profilePath =
            ResolveProfilePath(
                selection.ActiveProfilePath);

        try
        {
            EngineProfile profile =
                LoadProfile(
                    profilePath);

            ValidateProfile(
                profile,
                profilePath);

            return ConvertProfileToOptions(
                profile);
        }
        catch (Exception exception)
        {
            EngineOptions configured =
                configuration
                    .GetSection(
                        EngineOptions.SectionName)
                    .Get<EngineOptions>()
                ?? new EngineOptions();

            EngineOptions fallback =
                ConvertFallbackToOptions(
                    configured,
                    exception);

            ValidateOptions(
                fallback);

            return fallback;
        }
    }

    private static string ResolveEnginePath(
        string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(
                configuredPath))
        {
            throw new InvalidOperationException(
                "Engine path cannot be empty.");
        }

        string expanded =
            Environment.ExpandEnvironmentVariables(
                configuredPath.Trim());

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

    private static string ResolveProfilePath(
        string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(
                configuredPath))
        {
            throw new InvalidOperationException(
                "EngineProfile:ActiveProfilePath cannot be empty.");
        }

        string expanded =
            Environment.ExpandEnvironmentVariables(
                configuredPath.Trim());

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

    private static EngineProfile LoadProfile(
        string profilePath)
    {
        if (!File.Exists(profilePath))
        {
            throw new FileNotFoundException(
                "Engine profile was not found.",
                profilePath);
        }

        string json =
            File.ReadAllText(
                profilePath);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException(
                $"Engine profile is empty: {profilePath}");
        }

        EngineProfile? profile =
            JsonSerializer.Deserialize<EngineProfile>(
                json,
                JsonOptions);

        return profile
            ?? throw new InvalidDataException(
                $"Engine profile could not be deserialized: {profilePath}");
    }

    private static EngineOptions ConvertProfileToOptions(
        EngineProfile profile)
    {
        var options =
            new EngineOptions
            {
                ProfileId =
                    profile.ProfileId.Trim(),

                DisplayName =
                    profile.DisplayName.Trim(),

                ConfigurationSource =
                    "Profile",

                Mode =
                    profile.Mode.Trim(),

                ExecutablePath =
                    profile.ExecutablePath.Trim(),

                Arguments =
                    profile.Arguments,

                WorkingDirectory =
                    string.IsNullOrWhiteSpace(
                        profile.WorkingDirectory)
                        ? "."
                        : profile.WorkingDirectory.Trim(),

                RequiresAdmin =
                    profile.RequiresAdmin,

                UsesWinDivert =
                    profile.UsesWinDivert,

                StopTimeoutSeconds =
                    profile.StopTimeoutSeconds,

                KillTimeoutSeconds =
                    profile.KillTimeoutSeconds,

                FileHashes =
                    profile.FileHashes
            };

        ValidateOptions(
            options);

        return options;
    }

    private static EngineOptions ConvertFallbackToOptions(
        EngineOptions configured,
        Exception exception)
    {
        return new EngineOptions
        {
            ProfileId =
                "appsettings-fallback",

            DisplayName =
                "Appsettings fallback",

            ConfigurationSource =
                "AppSettings",

            ConfigurationWarning =
                $"ProfileFallback: {exception.GetType().Name}: {exception.Message}",

            Mode =
                configured.Mode,

            ExecutablePath =
                configured.ExecutablePath,

            Arguments =
                configured.Arguments,

            WorkingDirectory =
                configured.WorkingDirectory,

            RequiresAdmin =
                configured.RequiresAdmin,

            UsesWinDivert =
                configured.UsesWinDivert,

            StopTimeoutSeconds =
                configured.StopTimeoutSeconds,

            KillTimeoutSeconds =
                configured.KillTimeoutSeconds,

            FileHashes =
                configured.FileHashes
        };
    }

    private static void ValidateProfile(
        EngineProfile profile,
        string profilePath)
    {
        if (profile.SchemaVersion !=
            EngineProfile.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported engine profile schema version '{profile.SchemaVersion}' in '{profilePath}'. Expected '{EngineProfile.CurrentSchemaVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(
                profile.ProfileId))
        {
            throw new InvalidDataException(
                $"ProfileId cannot be empty in '{profilePath}'.");
        }

        if (string.IsNullOrWhiteSpace(
                profile.DisplayName))
        {
            throw new InvalidDataException(
                $"DisplayName cannot be empty in '{profilePath}'.");
        }

        if (string.IsNullOrWhiteSpace(
                profile.Mode))
        {
            throw new InvalidDataException(
                $"Mode cannot be empty in '{profilePath}'.");
        }

        if (string.IsNullOrWhiteSpace(
                profile.ExecutablePath))
        {
            throw new InvalidDataException(
                $"ExecutablePath cannot be empty in '{profilePath}'.");
        }

        if (profile.StopTimeoutSeconds < 1 ||
            profile.StopTimeoutSeconds > 60)
        {
            throw new InvalidDataException(
                $"StopTimeoutSeconds must be between 1 and 60 in '{profilePath}'.");
        }

        if (profile.KillTimeoutSeconds < 1 ||
            profile.KillTimeoutSeconds > 60)
        {
            throw new InvalidDataException(
                $"KillTimeoutSeconds must be between 1 and 60 in '{profilePath}'.");
        }

        ValidateFileHashes(
            profile.FileHashes,
            profilePath);

        string executablePath =
            ResolveEnginePath(
                profile.ExecutablePath);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Engine executable from profile was not found: {profile.ExecutablePath}",
                executablePath);
        }

        // The executable-trust gate is enforced for EVERY profile, not only for
        // profiles that self-declare RequiresAdmin/UsesWinDivert. A profile must
        // never be able to opt out of validation by lying about its flags and
        // then run an arbitrary (or UNC) executable as LocalSystem.
        string trustedBinDirectory =
            ResolveEnginePath(
                "Engine\\winws2\\bin");

        bool isUnderTrustedDirectory =
            executablePath.StartsWith(
                trustedBinDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        if (!isUnderTrustedDirectory)
        {
            throw new InvalidDataException(
                $"ExecutablePath is outside the trusted Engine\\winws2\\bin directory in '{profilePath}': {profile.ExecutablePath}");
        }

        if (!EngineTrustManifestLoader.IsExecutableTrusted(
                executablePath))
        {
            throw new InvalidDataException(
                $"ExecutablePath is not listed in the trusted engine binaries manifest in '{profilePath}': {profile.ExecutablePath}");
        }

        string workingDirectory =
            string.IsNullOrWhiteSpace(
                profile.WorkingDirectory)
                ? "."
                : profile.WorkingDirectory.Trim();

        string resolvedWorkingDirectory =
            ResolveEnginePath(
                workingDirectory);

        // WorkingDirectory must stay inside the install tree (BaseDirectory).
        // Blocks absolute/UNC working dirs (e.g. \\attacker\share) that could
        // change winws's relative-path resolution for a SYSTEM process.
        string installRoot =
            Path.GetFullPath(
                AppContext.BaseDirectory);

        bool workingDirUnderInstall =
            resolvedWorkingDirectory.Equals(
                installRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) ||
            resolvedWorkingDirectory.StartsWith(
                installRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? installRoot
                    : installRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        if (!workingDirUnderInstall)
        {
            throw new InvalidDataException(
                $"WorkingDirectory is outside the install directory in '{profilePath}': {profile.WorkingDirectory}");
        }

        if (!Directory.Exists(resolvedWorkingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Engine working directory from profile was not found: {workingDirectory}; resolved: {resolvedWorkingDirectory}");
        }
    }

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

    private static void ValidateOptions(
        EngineOptions options)
    {
        if (string.IsNullOrWhiteSpace(
                options.ProfileId))
        {
            throw new InvalidOperationException(
                "Engine ProfileId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(
                options.DisplayName))
        {
            throw new InvalidOperationException(
                "Engine DisplayName cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(
                options.ConfigurationSource))
        {
            throw new InvalidOperationException(
                "Engine ConfigurationSource cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(
                options.Mode))
        {
            throw new InvalidOperationException(
                "Engine:Mode cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(
                options.ExecutablePath))
        {
            throw new InvalidOperationException(
                "Engine:ExecutablePath cannot be empty.");
        }

        if (options.StopTimeoutSeconds < 1 ||
            options.StopTimeoutSeconds > 60)
        {
            throw new InvalidOperationException(
                "Engine:StopTimeoutSeconds must be between 1 and 60.");
        }

        if (options.KillTimeoutSeconds < 1 ||
            options.KillTimeoutSeconds > 60)
        {
            throw new InvalidOperationException(
                "Engine:KillTimeoutSeconds must be between 1 and 60.");
        }
    }
}
