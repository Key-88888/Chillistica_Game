namespace Chillistica_game.App.Services;

public sealed class AppProcessStatus
{
    public required string AppName { get; init; }

    public required string[] ProcessNames { get; init; }

    public bool IsRunning { get; init; }

    public required string RunningProcessesText { get; init; }

    public string StatusText =>
        IsRunning
            ? "запущено"
            : "не запущено";
}
