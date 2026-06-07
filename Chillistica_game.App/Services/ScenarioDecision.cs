namespace Chillistica_game.App.Services;

public sealed class ScenarioDecision
{
    public required string AppName { get; init; }

    public required string RecommendedMode { get; init; }

    public required string Reason { get; init; }

    public required string RiskLevel { get; init; }

    public required string NextAction { get; init; }

    public string ToDisplayText()
    {
        return
            $"{AppName}\n" +
            $"Сценарий: {RecommendedMode}\n" +
            $"Причина: {Reason}\n" +
            $"Риск: {RiskLevel}\n" +
            $"Следующее действие: {NextAction}";
    }
}
