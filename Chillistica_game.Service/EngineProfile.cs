namespace Chillistica_game.Service;

public sealed class EngineProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } =
        CurrentSchemaVersion;

    public string ProfileId { get; init; } =
        string.Empty;

    public string DisplayName { get; init; } =
        string.Empty;

    public string Mode { get; init; } =
        string.Empty;

    public string ExecutablePath { get; init; } =
        string.Empty;

    public string Arguments { get; init; } =
        string.Empty;

    public string WorkingDirectory { get; init; } =
        ".";

    public bool RequiresAdmin { get; init; }

    public bool UsesWinDivert { get; init; }

    public bool AllowUnsafeStart { get; init; }

    public int StopTimeoutSeconds { get; init; } =
        2;

    public int KillTimeoutSeconds { get; init; } =
        5;
}
