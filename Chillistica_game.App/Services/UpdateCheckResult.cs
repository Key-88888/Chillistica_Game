namespace Chillistica_game.App.Services;

public sealed class UpdateCheckResult
{
    public required Version LatestVersion { get; init; }

    public required string TagName { get; init; }

    public required string DownloadUrl { get; init; }

    public required string SignatureUrl { get; init; }
}
