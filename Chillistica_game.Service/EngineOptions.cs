namespace Chillistica_game.Service;

public sealed class EngineOptions
{
    public const string SectionName =
        "Engine";

    public string ProfileId { get; init; } =
        "appsettings-fallback";

    public string DisplayName { get; init; } =
        "Appsettings fallback";

    public string ConfigurationSource { get; init; } =
        "AppSettings";

    public string ConfigurationWarning { get; init; } =
        string.Empty;

    public string Mode { get; init; } =
        "Test";

    public string ExecutablePath { get; init; } =
        "%SystemRoot%\\System32\\PING.EXE";

    public string Arguments { get; init; } =
        "127.0.0.1 -t";

    public string WorkingDirectory { get; init; } =
        ".";

    public bool RequiresAdmin { get; init; }

    public bool UsesWinDivert { get; init; }

    public bool AllowUnsafeStart { get; init; }

    public int StopTimeoutSeconds { get; init; } =
        2;

    public int KillTimeoutSeconds { get; init; } =
        5;

    public IReadOnlyList<EngineFileHash> FileHashes { get; init; } =
        Array.Empty<EngineFileHash>();
}
