namespace Chillistica_game.Service;

public sealed class EngineFileHash
{
    public string Path { get; init; } =
        string.Empty;

    public string Sha256 { get; init; } =
        string.Empty;

    public bool Required { get; init; } =
        true;
}
