namespace Chillistica_game.App.Services;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool YouTubeEnabled { get; set; } = true;

    public bool DiscordEnabled { get; set; } = true;

    public bool RobloxEnabled { get; set; } = true;

    public bool FortniteEnabled { get; set; } = true;
}
