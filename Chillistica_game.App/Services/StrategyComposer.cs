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
/// The port / forbidden-flag / path validation is kept: winws runs elevated, so
/// a tampered strategy fragment would become elevated argv. Shipped strategies
/// only use relative <c>files\...</c> paths and none of the forbidden flags.
/// </summary>
public static class StrategyComposer
{
    // The engine tree ships under Engine\winws2\ (historical name; it now holds
    // mature winws v72). winws.exe runs with this as its working directory, so
    // strategy fragments reference hostlists as relative files\... paths.
    public const string EngineRelativeRoot = "Engine\\winws2";

    private static readonly Regex PortTokenPattern =
        new(@"^\d{1,5}(-\d{1,5})?$", RegexOptions.Compiled);

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

    private static void ValidateArgumentsFragment(string fragment, string appId)
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
}
