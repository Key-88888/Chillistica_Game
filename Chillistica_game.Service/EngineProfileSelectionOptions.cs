namespace Chillistica_game.Service;

public sealed class EngineProfileSelectionOptions
{
    public const string SectionName =
        "EngineProfile";

    public string ActiveProfilePath { get; init; } =
        "Engine\\test\\config.json";
}
