using System.IO;
using System.Text.Json;

namespace Chillistica_game.App.Services;

public sealed class SettingsService
{
    private readonly string _settingsDirectory;
    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public SettingsService()
    {
        _settingsDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "Chillistica_game");

        _settingsPath =
            Path.Combine(
                _settingsDirectory,
                "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return CreateDefaultSettings();
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException(
                    "Settings file is empty.");
            }

            AppSettings? settings =
                JsonSerializer.Deserialize<AppSettings>(
                    json,
                    SerializerOptions);

            if (settings is null)
            {
                throw new InvalidDataException(
                    "Settings file contains no data.");
            }

            bool requiresSave = UpgradeSettings(settings);

            if (requiresSave)
            {
                TrySave(settings);
            }

            return settings;
        }
        catch (
            Exception exception)
            when (
                exception is JsonException or
                IOException or
                UnauthorizedAccessException or
                InvalidDataException)
        {
            QuarantineInvalidSettingsFile();

            AppSettings defaults =
                CreateDefaultSettings();

            TrySave(defaults);

            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.SchemaVersion =
            AppSettings.CurrentSchemaVersion;

        Directory.CreateDirectory(
            _settingsDirectory);

        string json =
            JsonSerializer.Serialize(
                settings,
                SerializerOptions);

        string temporaryPath =
            _settingsPath + ".tmp";

        try
        {
            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                _settingsPath,
                true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Временный файл будет удалён
                    // при следующем успешном сохранении.
                }
            }
        }
    }

    public string GetSettingsPath()
    {
        return _settingsPath;
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            SchemaVersion =
                AppSettings.CurrentSchemaVersion
        };
    }

    private static bool UpgradeSettings(
        AppSettings settings)
    {
        if (
            settings.SchemaVersion ==
            AppSettings.CurrentSchemaVersion)
        {
            return false;
        }

        settings.SchemaVersion =
            AppSettings.CurrentSchemaVersion;

        return true;
    }

    private void TrySave(
        AppSettings settings)
    {
        try
        {
            Save(settings);
        }
        catch
        {
            // Ошибка записи настроек не должна
            // препятствовать запуску приложения.
        }
    }

    private void QuarantineInvalidSettingsFile()
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(
                _settingsDirectory);

            string timestamp =
                DateTime.Now.ToString(
                    "yyyyMMdd-HHmmss");

            string corruptedPath =
                Path.Combine(
                    _settingsDirectory,
                    $"settings.corrupt-{timestamp}.json");

            File.Move(
                _settingsPath,
                corruptedPath,
                true);
        }
        catch
        {
            // Даже если повреждённый файл нельзя
            // переместить, приложение должно запуститься.
        }
    }
}
