namespace Chillistica_game.App.Services;

// Kept so the shipped strategy JSON (which still carries FileHashes arrays)
// deserializes cleanly. In the service-less model we no longer verify SHA256 at
// launch — the app ships the binaries and runs elevated as the user who
// launched it — but we still use Path/Required for a light "does the referenced
// hostlist/quic file exist" pre-flight so a missing file gives a clear message.
public sealed class EngineFileHash
{
    public string Path { get; init; } =
        string.Empty;

    public string Sha256 { get; init; } =
        string.Empty;

    public bool Required { get; init; } =
        true;
}
