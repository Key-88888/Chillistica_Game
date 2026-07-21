namespace Chillistica_game.App.Services;

public sealed class AppStrategyCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } =
        CurrentSchemaVersion;

    public string AppId { get; init; } =
        string.Empty;

    public string DisplayName { get; init; } =
        string.Empty;

    public IReadOnlyList<AppStrategyCandidate> Strategies { get; init; } =
        Array.Empty<AppStrategyCandidate>();
}
