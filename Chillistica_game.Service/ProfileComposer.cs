using System.Text.Json;

namespace Chillistica_game.Service;

public static class ProfileComposer
{
    private const string ComposedProfileRelativePath =
        "Engine\\winws2\\profiles\\_active-composed.json";

    private static readonly JsonSerializerOptions ReadOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

    private static readonly JsonSerializerOptions WriteOptions =
        new()
        {
            WriteIndented = true
        };

    public static string Compose(
        IReadOnlyList<(string AppId, int StrategyIndex)> selections)
    {
        if (selections is null || selections.Count == 0)
        {
            throw new ArgumentException(
                "At least one app selection is required.",
                nameof(selections));
        }

        var tcpPorts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var udpPorts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var fragments = new List<string>();
        var fileHashes = new List<EngineFileHash>();
        var appIds = new List<string>();

        foreach ((string appId, int strategyIndex) in selections)
        {
            AppStrategyCatalog catalog =
                LoadCatalog(appId);

            if (catalog.Strategies.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Strategy catalog for '{appId}' has no candidates.");
            }

            int clampedIndex =
                Math.Clamp(strategyIndex, 0, catalog.Strategies.Count - 1);

            AppStrategyCandidate candidate =
                catalog.Strategies[clampedIndex];

            AddPorts(tcpPorts, candidate.TcpPorts);
            AddPorts(udpPorts, candidate.UdpPorts);

            fragments.Add(candidate.ArgumentsFragment);
            fileHashes.AddRange(candidate.FileHashes);
            appIds.Add(appId);
        }

        fileHashes.AddRange(
            EngineTrustManifestLoader.GetTrustedBinaryHashes());

        var headerParts = new List<string>();

        if (tcpPorts.Count > 0)
        {
            headerParts.Add(
                $"--wf-tcp-out={string.Join(",", tcpPorts)}");
        }

        if (udpPorts.Count > 0)
        {
            headerParts.Add(
                $"--wf-udp-out={string.Join(",", udpPorts)}");
        }

        string arguments =
            string.Join(
                " ",
                headerParts.Concat(
                    new[] { string.Join(" --new ", fragments) }));

        var profile = new EngineProfile
        {
            SchemaVersion = EngineProfile.CurrentSchemaVersion,
            ProfileId = "composed-" + string.Join("+", appIds),
            DisplayName = "Composed: " + string.Join(", ", appIds),
            Mode = "Winws2Composed",
            ExecutablePath = "Engine\\winws2\\bin\\winws2.exe",
            Arguments = arguments.Trim(),
            WorkingDirectory = "Engine\\winws2",
            RequiresAdmin = true,
            UsesWinDivert = true,
            StopTimeoutSeconds = 3,
            KillTimeoutSeconds = 5,
            FileHashes = DeduplicateByPath(fileHashes)
        };

        string outputPath =
            ResolvePath(ComposedProfileRelativePath);

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(profile, WriteOptions));

        return outputPath;
    }

    public static AppStrategyCatalog LoadCatalog(
        string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new ArgumentException(
                "AppId cannot be empty.",
                nameof(appId));
        }

        string catalogPath =
            ResolvePath(
                $"Engine\\winws2\\strategies\\{appId.Trim().ToLowerInvariant()}.json");

        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException(
                $"Strategy catalog not found for app '{appId}'.",
                catalogPath);
        }

        string json =
            File.ReadAllText(catalogPath);

        return JsonSerializer.Deserialize<AppStrategyCatalog>(json, ReadOptions)
            ?? throw new InvalidDataException(
                $"Strategy catalog could not be deserialized: {catalogPath}");
    }

    private static void AddPorts(
        SortedSet<string> target,
        string portsCsv)
    {
        if (string.IsNullOrWhiteSpace(portsCsv))
        {
            return;
        }

        foreach (string port in portsCsv.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            target.Add(port);
        }
    }

    private static List<EngineFileHash> DeduplicateByPath(
        IEnumerable<EngineFileHash> fileHashes)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<EngineFileHash>();

        foreach (EngineFileHash fileHash in fileHashes)
        {
            if (seenPaths.Add(fileHash.Path))
            {
                result.Add(fileHash);
            }
        }

        return result;
    }

    private static string ResolvePath(
        string relativeOrAbsolutePath)
    {
        string expanded =
            Environment.ExpandEnvironmentVariables(
                relativeOrAbsolutePath.Trim());

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, expanded));
    }
}
