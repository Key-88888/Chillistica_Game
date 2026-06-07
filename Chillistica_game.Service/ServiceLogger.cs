using System.Text.Json;

namespace Chillistica_game.Service;

public sealed class ServiceLogger
{
    private readonly string _logDirectory;
    private readonly object _syncRoot = new();

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = false
        };

    public ServiceLogger()
    {
        _logDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "Chillistica_game",
                "Logs");
    }

    public void Info(
        string stage,
        string result)
    {
        Write(
            level: "Information",
            stage: stage,
            result: result,
            error: null);
    }

    public void Error(
        string stage,
        Exception exception)
    {
        Write(
            level: "Error",
            stage: stage,
            result: "Failed",
            error: exception.Message);
    }

    public string GetCurrentLogPath()
    {
        return Path.Combine(
            _logDirectory,
            $"service-{DateTime.Now:yyyy-MM-dd}.jsonl");
    }

    private void Write(
        string level,
        string stage,
        string result,
        string? error)
    {
        try
        {
            Directory.CreateDirectory(
                _logDirectory);

            var entry = new ServiceLogEntry
            {
                TimestampUtc =
                    DateTimeOffset.UtcNow,

                Level =
                    level,

                Stage =
                    stage,

                Result =
                    result,

                Error =
                    error
            };

            string json =
                JsonSerializer.Serialize(
                    entry,
                    SerializerOptions);

            lock (_syncRoot)
            {
                File.AppendAllText(
                    GetCurrentLogPath(),
                    json + Environment.NewLine);
            }
        }
        catch
        {
            // Ошибка журнала не должна завершать службу.
        }
    }

    private sealed class ServiceLogEntry
    {
        public DateTimeOffset TimestampUtc { get; init; }

        public string Level { get; init; } =
            string.Empty;

        public string Stage { get; init; } =
            string.Empty;

        public string Result { get; init; } =
            string.Empty;

        public string? Error { get; init; }
    }
}
