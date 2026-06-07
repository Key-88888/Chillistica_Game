using System.IO;
using System.Text.Json;

namespace Chillistica_game.App.Services;

public sealed class AppLogger
{
    private readonly string _logDirectory;
    private readonly object _syncRoot = new();

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = false
        };

    public AppLogger()
    {
        _logDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Chillistica_game",
                "Logs");
    }

    public void Info(
        string stage,
        string? app = null,
        string? endpoint = null,
        string? mode = null,
        string result = "OK",
        string? error = null)
    {
        Write(
            level: "Information",
            stage: stage,
            app: app,
            endpoint: endpoint,
            mode: mode,
            result: result,
            error: error);
    }

    public void Error(
        string stage,
        Exception exception,
        string? app = null,
        string? endpoint = null,
        string? mode = null,
        string result = "Failed")
    {
        Write(
            level: "Error",
            stage: stage,
            app: app,
            endpoint: endpoint,
            mode: mode,
            result: result,
            error: exception.Message);
    }

    public string GetCurrentLogPath()
    {
        return Path.Combine(
            _logDirectory,
            $"app-{DateTime.Now:yyyy-MM-dd}.jsonl");
    }

    private void Write(
        string level,
        string stage,
        string? app,
        string? endpoint,
        string? mode,
        string result,
        string? error)
    {
        try
        {
            Directory.CreateDirectory(
                _logDirectory);

            var entry = new AppLogEntry
            {
                TimestampUtc =
                    DateTimeOffset.UtcNow,

                Level =
                    level,

                Stage =
                    stage,

                App =
                    app,

                Endpoint =
                    endpoint,

                Mode =
                    mode,

                Result =
                    result,

                Error =
                    error
            };

            string json =
                JsonSerializer.Serialize(
                    entry,
                    SerializerOptions);

            string logPath =
                GetCurrentLogPath();

            lock (_syncRoot)
            {
                File.AppendAllText(
                    logPath,
                    json + Environment.NewLine);
            }
        }
        catch
        {
            // Ошибка журнала не должна влиять
            // на работу приложения.
        }
    }

    private sealed class AppLogEntry
    {
        public DateTimeOffset TimestampUtc { get; init; }

        public string Level { get; init; } =
            string.Empty;

        public string Stage { get; init; } =
            string.Empty;

        public string? App { get; init; }

        public string? Endpoint { get; init; }

        public string? Mode { get; init; }

        public string Result { get; init; } =
            string.Empty;

        public string? Error { get; init; }
    }
}
