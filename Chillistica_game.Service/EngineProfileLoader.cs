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

        string profilePath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Engine",
                "test",
                "config.json");

        try
        {
            EngineProfile profile =
                LoadProfile(
                    profilePath);

            ValidateProfile(
                profile,
                profilePath);

            return ConvertToOptions(
                profile);
        }
        catch
        {
            EngineOptions fallback =
                configuration
                    .GetSection(
                        EngineOptions.SectionName)
                    .Get<EngineOptions>()
                ?? new EngineOptions();

            ValidateOptions(
                fallback);

            return fallback;
        }
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

    private static EngineOptions ConvertToOptions(
        EngineProfile profile)
    {
        var options =
            new EngineOptions
            {
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

                StopTimeoutSeconds =
                    profile.StopTimeoutSeconds,

                KillTimeoutSeconds =
                    profile.KillTimeoutSeconds
            };

        ValidateOptions(
            options);

        return options;
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
