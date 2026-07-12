using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chillistica_game.Service;

public static class ProfileComposer
{
    private const string ComposedProfileDirectoryRelativePath =
        "Engine\\winws2\\profiles";

    private const string ComposedProfilePrefix =
        "_active-composed-";

    // A port token is a single port or an inclusive range; nothing else.
    private static readonly Regex PortTokenPattern =
        new(@"^\d{1,5}(-\d{1,5})?$", RegexOptions.Compiled);

    // winws flags that read or write arbitrary filesystem paths, or daemonize.
    // None are used by the shipped strategies; their presence in a fragment
    // means the fragment is untrusted/tampered and must be rejected so it can
    // never turn the SYSTEM winws process into an arbitrary file read/write.
    private static readonly string[] ForbiddenFragmentFlags =
    {
        "--debug",
        "--wf-save",
        "--pidfile",
        "--daemon",
        "--hostlist-auto-debug",
        "--wf-raw"
    };

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

            ValidateArgumentsFragment(
                candidate.ArgumentsFragment,
                appId);

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
                $"--wf-tcp={string.Join(",", tcpPorts)}");
        }

        if (udpPorts.Count > 0)
        {
            headerParts.Add(
                $"--wf-udp={string.Join(",", udpPorts)}");
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
            Mode = "Winws1Composed",
            ExecutablePath = "Engine\\winws2\\bin\\winws.exe",
            Arguments = arguments.Trim(),
            WorkingDirectory = "Engine\\winws2",
            RequiresAdmin = true,
            UsesWinDivert = true,
            StopTimeoutSeconds = 3,
            KillTimeoutSeconds = 5,
            FileHashes = DeduplicateByPath(fileHashes)
        };

        string composedDirectory =
            ResolvePath(ComposedProfileDirectoryRelativePath);

        Directory.CreateDirectory(composedDirectory);

        CleanupStaleComposedProfiles(composedDirectory);

        // Per-request unique filename: two concurrent composes never share a
        // file, so one caller can never load another caller's composed args.
        string outputPath =
            Path.Combine(
                composedDirectory,
                $"{ComposedProfilePrefix}{Guid.NewGuid():N}.json");

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(profile, WriteOptions));

        return outputPath;
    }

    private static void CleanupStaleComposedProfiles(
        string composedDirectory)
    {
        try
        {
            DateTime cutoff =
                DateTime.UtcNow.AddMinutes(-1);

            foreach (string file in Directory.EnumerateFiles(
                         composedDirectory,
                         ComposedProfilePrefix + "*.json"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best-effort housekeeping; never fail a compose over cleanup.
        }
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
            if (!PortTokenPattern.IsMatch(port))
            {
                // A non-numeric token (e.g. an embedded space + flag) would be
                // split into extra winws arguments and run as SYSTEM. Reject.
                throw new InvalidOperationException(
                    $"Invalid port token '{port}' in strategy ports.");
            }

            target.Add(port);
        }
    }

    private static void ValidateArgumentsFragment(
        string fragment,
        string appId)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return;
        }

        foreach (string token in fragment.Split(
                     (char[]?)null,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string flag =
                token.StartsWith("--", StringComparison.Ordinal)
                    ? token.Split('=', 2)[0].ToLowerInvariant()
                    : string.Empty;

            if (flag.Length > 0 &&
                ForbiddenFragmentFlags.Any(forbidden =>
                    string.Equals(flag, forbidden, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Strategy '{appId}' uses a forbidden winws flag '{flag}'.");
            }

            // No absolute (drive-rooted), UNC, or environment-variable paths in
            // any fragment token. Shipped strategies only reference relative
            // 'files\...' paths; anything else is treated as tampering.
            if (token.Contains(":\\", StringComparison.Ordinal) ||
                token.Contains("\\\\", StringComparison.Ordinal) ||
                token.Contains('%', StringComparison.Ordinal) ||
                token.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Strategy '{appId}' fragment token '{token}' references a disallowed path.");
            }
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
