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

    // Both hosts must return a real HTTPS response (status < 500) for reachability
    // to count, so every probe host has to actually answer GET / over TLS. The
    // apex googlevideo.com does NOT: it resolves and accepts TCP but never answers
    // a bare HTTPS GET (video is served only from rrX---snXXX.googlevideo.com with
    // signed paths), so it returned no response every time — which pinned YouTube
    // to "best effort / not confirmed" even when the bypass had fully unblocked it.
    // i.ytimg.com is the YouTube thumbnail CDN: it answers over TLS and is covered
    // by the ytimg.com hostlist entry, so the desync is still exercised end to end.
    private static readonly IReadOnlyList<DiagnosticsTarget> YouTube =
        new[]
        {
            new DiagnosticsTarget { ServiceName = "YouTube Web", Host = "www.youtube.com" },
            new DiagnosticsTarget { ServiceName = "YouTube Images", Host = "i.ytimg.com" }
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
