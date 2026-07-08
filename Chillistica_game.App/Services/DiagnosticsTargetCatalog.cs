namespace Chillistica_game.App.Services;

public static class DiagnosticsTargetCatalog
{
    public static IReadOnlyList<string> AllAppIds { get; } =
        new[] { "youtube", "discord", "roblox", "fortnite" };

    public static IReadOnlyList<DiagnosticsTarget> GetTargetsForApp(
        string appId)
    {
        return appId.Trim().ToLowerInvariant() switch
        {
            "youtube" => YouTube,
            "discord" => Discord,
            "roblox" => Roblox,
            "fortnite" => Fortnite,
            _ => Array.Empty<DiagnosticsTarget>()
        };
    }

    private static readonly IReadOnlyList<DiagnosticsTarget> YouTube =
        new[]
        {
            new DiagnosticsTarget { ServiceName = "YouTube Web", Host = "www.youtube.com" },
            new DiagnosticsTarget { ServiceName = "Google Video", Host = "googlevideo.com" }
        };

    private static readonly IReadOnlyList<DiagnosticsTarget> Discord =
        new[]
        {
            new DiagnosticsTarget { ServiceName = "Discord Web", Host = "discord.com" },
            new DiagnosticsTarget { ServiceName = "Discord Gateway", Host = "gateway.discord.gg" },
            new DiagnosticsTarget { ServiceName = "Discord CDN", Host = "cdn.discordapp.com" }
        };

    private static readonly IReadOnlyList<DiagnosticsTarget> Roblox =
        new[]
        {
            new DiagnosticsTarget { ServiceName = "Roblox Web", Host = "www.roblox.com" },
            new DiagnosticsTarget { ServiceName = "Roblox API", Host = "games.roblox.com" },
            new DiagnosticsTarget { ServiceName = "Roblox Presence", Host = "presence.roblox.com" }
        };

    private static readonly IReadOnlyList<DiagnosticsTarget> Fortnite =
        new[]
        {
            new DiagnosticsTarget { ServiceName = "Epic Web", Host = "www.epicgames.com" },
            new DiagnosticsTarget { ServiceName = "Epic Account", Host = "account-public-service-prod.ol.epicgames.com" },
            new DiagnosticsTarget { ServiceName = "Epic Lightswitch", Host = "lightswitch-public-service-prod.ol.epicgames.com" },
            new DiagnosticsTarget { ServiceName = "Fortnite Public Service", Host = "fortnite-public-service-prod11.ol.epicgames.com" },
            new DiagnosticsTarget { ServiceName = "Epic XMPP", Host = "xmpp-service-prod.ol.epicgames.com", Port = 5222, CheckHttps = false }
        };
}
