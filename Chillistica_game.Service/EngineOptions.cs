namespace Chillistica_game.Service;

public sealed class EngineOptions
{
    public const string SectionName =
        "Engine";

    public string Mode { get; init; } =
        "Test";

    public string ExecutablePath { get; init; } =
        "%SystemRoot%\\System32\\PING.EXE";

    public string Arguments { get; init; } =
        "127.0.0.1 -t";

    public string WorkingDirectory { get; init; } =
        ".";

    public int StopTimeoutSeconds { get; init; } =
        2;

    public int KillTimeoutSeconds { get; init; } =
        5;
}
