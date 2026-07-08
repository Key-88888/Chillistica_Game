namespace Chillistica_game.App.Services;

public sealed class AppProtectionResult
{
    public required string AppId { get; init; }

    public required AppProtectionOutcome Outcome { get; init; }

    public int? StrategyIndex { get; init; }

    public int? StrategyCount { get; init; }
}
