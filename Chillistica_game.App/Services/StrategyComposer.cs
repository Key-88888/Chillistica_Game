using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chillistica_game.App.Services;

/// <summary>
/// Builds the single winws.exe command line from the selected per-app strategy
/// candidates. Ported from the old service-side ProfileComposer, but returns the
/// argument string directly (no intermediate signed JSON profile) because the
/// app now launches winws in-process.
///
/// The port / argument / path validation is kept: winws runs elevated, so a
/// tampered strategy fragment would become elevated argv. Shipped strategies
/// only use relative <c>files\...</c> paths and explicitly allowed flags.
/// </summary>
public static class StrategyComposer
{
    // The engine tree ships under Engine\winws2\ (historical name; it now holds
    // mature winws v72). winws.exe runs with this as its working directory, so
    // strategy fragments reference hostlists as relative files\... paths.
    public const string EngineRelativeRoot = "Engine\\winws2";

    private static readonly Regex PortTokenPattern =
        new(@"^\d{1,5}(-\d{1,5})?$", RegexOptions.Compiled);

    // Derived from the shipped strategies; extend deliberately when a new
    // strategy needs a new flag because every accepted token runs elevated.
    private static readonly HashSet<string> AllowedFragmentFlags =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "--dpi-desync",
        "--dpi-desync-badseq-increment",
        "--dpi-desync-fake-quic",
        "--dpi-desync-fake-tls",
        "--dpi-desync-fake-tls-mod",
        "--dpi-desync-fakedsplit-pattern",
        "--dpi-desync-fooling",
        "--dpi-desync-hostfakesplit-mod",
        "--dpi-desync-repeats",
        "--dpi-desync-split-pos",
        "--dpi-desync-split-seqovl",
        "--dpi-desync-split-seqovl-pattern",
        "--filter-l7",
        "--filter-tcp",
        "--filter-udp",
        "--hostlist",
        "--ip-id",
        "--new",
        "--wf-tcp",
        "--wf-udp"
    };

    private static readonly JsonSerializerOptions ReadOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

    public sealed record ComposedProfile(
        string Arguments,
        IReadOnlyList<string> RequiredFiles);

    /// <summary>Full absolute path of the engine working directory.</summary>
    public static string EngineDirectory =>
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, EngineRelativeRoot));

    /// <summary>Full absolute path of winws.exe.</summary>
    public static string WinwsExecutablePath =>
        Path.Combine(EngineDirectory, "bin", "winws.exe");

    public static int GetCandidateCount(string appId)
    {
        try
        {
            return Math.Max(1, LoadCatalog(appId).Strategies.Count);
        }
        catch
        {
            return 1;
        }
    }

    public static AppStrategyCatalog LoadCatalog(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new ArgumentException("AppId cannot be empty.", nameof(appId));
        }

        string catalogPath =
            Path.Combine(
                EngineDirectory,
                "strategies",
                $"{appId.Trim().ToLowerInvariant()}.json");

        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException(
                $"Strategy catalog not found for app '{appId}'.",
                catalogPath);
        }

        string json = File.ReadAllText(catalogPath);

        return JsonSerializer.Deserialize<AppStrategyCatalog>(json, ReadOptions)
            ?? throw new InvalidDataException(
                $"Strategy catalog could not be deserialized: {catalogPath}");
    }

    /// <summary>
    /// Compose the winws command line for the given (appId, strategyIndex) picks.
    /// </summary>
    public static ComposedProfile Compose(
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
        var requiredFiles = new List<string>();

        foreach ((string appId, int strategyIndex) in selections)
        {
            AppStrategyCatalog catalog = LoadCatalog(appId);

            if (catalog.Strategies.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Strategy catalog for '{appId}' has no candidates.");
            }

            int clampedIndex =
                Math.Clamp(strategyIndex, 0, catalog.Strategies.Count - 1);

            AppStrategyCandidate candidate = catalog.Strategies[clampedIndex];

            AddPorts(tcpPorts, candidate.TcpPorts);
            AddPorts(udpPorts, candidate.UdpPorts);

            ValidateArgumentsFragment(candidate.ArgumentsFragment, appId);

            fragments.Add(candidate.ArgumentsFragment);

            foreach (EngineFileHash file in candidate.FileHashes)
            {
                if (file.Required && !string.IsNullOrWhiteSpace(file.Path))
                {
                    requiredFiles.Add(file.Path);
                }
            }
        }

        var headerParts = new List<string>();

        if (tcpPorts.Count > 0)
        {
            headerParts.Add($"--wf-tcp={string.Join(",", tcpPorts)}");
        }

        if (udpPorts.Count > 0)
        {
            headerParts.Add($"--wf-udp={string.Join(",", udpPorts)}");
        }

        string arguments =
            string.Join(
                " ",
                headerParts.Concat(
                    new[] { string.Join(" --new ", fragments) }))
                .Trim();

        return new ComposedProfile(
            arguments,
            requiredFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static void AddPorts(SortedSet<string> target, string portsCsv)
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
                throw new InvalidOperationException(
                    $"Invalid port token '{port}' in strategy ports.");
            }

            target.Add(port);
        }
    }

    internal static void ValidateArgumentsFragment(string fragment, string appId)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return;
        }

        foreach (string token in fragment.Split(
                     (char[]?)null,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string normalizedToken = token.Trim('"', '\'');

            if (normalizedToken.StartsWith('@') ||
                normalizedToken.StartsWith('$'))
            {
                throw new InvalidOperationException(
                    $"Strategy '{appId}' fragment token '{token}' uses winws option-file loading.");
            }

            if (normalizedToken.StartsWith('-') &&
                !normalizedToken.StartsWith("--", StringComparison.Ordinal))
            {
                normalizedToken = $"-{normalizedToken}";
            }

            string[] tokenParts = normalizedToken.Split('=', 2);
            string flag = tokenParts[0];

            if (!flag.StartsWith("--", StringComparison.Ordinal) ||
                !AllowedFragmentFlags.Contains(flag))
            {
                throw new InvalidOperationException(
                    $"Strategy '{appId}' fragment token '{token}' is not an allowed winws flag.");
            }

            if (tokenParts.Length == 2 &&
                (tokenParts[1].Contains(":\\", StringComparison.Ordinal) ||
                 tokenParts[1].Contains("\\\\", StringComparison.Ordinal) ||
                 tokenParts[1].Contains('%', StringComparison.Ordinal) ||
                 tokenParts[1].Contains("..", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Strategy '{appId}' fragment token '{token}' references a disallowed path.");
            }
        }
    }
}
