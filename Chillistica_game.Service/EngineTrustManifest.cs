namespace Chillistica_game.Service;

public sealed class EngineTrustManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } =
        CurrentSchemaVersion;

    public IReadOnlyList<EngineFileHash> TrustedBinaries { get; init; } =
        Array.Empty<EngineFileHash>();
}
