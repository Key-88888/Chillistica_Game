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
        catch
        {
            EngineOptions configured =
                configuration
                    .GetSection(
                        EngineOptions.SectionName)
                    .Get<EngineOptions>()
                ?? new EngineOptions();

            EngineOptions fallback =
                ConvertFallbackToOptions(
                    configured);

            ValidateOptions(
                fallback);

            return fallback;
        }
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
                    profile.KillTimeoutSeconds
            };

        ValidateOptions(
            options);

        return options;
    }

    private static EngineOptions ConvertFallbackToOptions(
        EngineOptions configured)
    {
        return new EngineOptions
        {
            ProfileId =
                "appsettings-fallback",

            DisplayName =
                "Appsettings fallback",

            ConfigurationSource =
                "AppSettings",

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
                configured.KillTimeoutSeconds
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

