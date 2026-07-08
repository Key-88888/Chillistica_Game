using System.Linq;
using System.Text.Json;

namespace Chillistica_game.App.Services;

public sealed class StrategyOrchestrator
{
    private const int MaxFallbackRounds = 4;

    private readonly NamedPipeClientService _pipeClient;
    private readonly DiagnosticsService _diagnosticsService;

    public StrategyOrchestrator(
        NamedPipeClientService pipeClient,
        DiagnosticsService diagnosticsService)
    {
        _pipeClient = pipeClient;
        _diagnosticsService = diagnosticsService;
    }

    public async Task<(bool EngineStarted, string EngineResponse, IReadOnlyList<AppProtectionResult> AppResults)> EnableAsync(
        IReadOnlyList<string> checkedAppIds,
        IDictionary<string, int> lastGoodStrategyIndex,
        CancellationToken cancellationToken = default)
    {
        var appResults = new List<AppProtectionResult>();
        var bypassAppIds = new List<string>();

        foreach (string appId in checkedAppIds)
        {
            bool alreadyDirect =
                await IsFullyReachableDirectAsync(
                    appId,
                    cancellationToken);

            if (alreadyDirect)
            {
                appResults.Add(
                    new AppProtectionResult
                    {
                        AppId = appId,
                        Outcome = AppProtectionOutcome.Skipped
                    });
            }
            else
            {
                bypassAppIds.Add(appId);
            }
        }

        if (bypassAppIds.Count == 0)
        {
            return (false, "ALL_DIRECT_NO_BYPASS_NEEDED", appResults);
        }

        var candidateCounts = new Dictionary<string, int>();

        foreach (string appId in bypassAppIds)
        {
            candidateCounts[appId] =
                await GetCandidateCountAsync(
                    appId,
                    cancellationToken);
        }

        var currentIndex =
            bypassAppIds.ToDictionary(
                appId => appId,
                appId => ClampIndex(
                    lastGoodStrategyIndex.TryGetValue(appId, out int savedIndex)
                        ? savedIndex
                        : 0,
                    candidateCounts[appId]));

        bool engineStarted = false;
        string engineResponse = string.Empty;
        List<string> stillPending = bypassAppIds;

        for (int round = 0;
             round < MaxFallbackRounds && stillPending.Count > 0;
             round++)
        {
            // Every round must resend the FULL bypass set (not just the
            // still-pending ones) — apps already confirmed Active keep their
            // fixed index, otherwise they'd drop out of the composed profile.
            IReadOnlyDictionary<string, int> selections =
                bypassAppIds.ToDictionary(
                    appId => appId,
                    appId => currentIndex[appId]);

            engineResponse =
                await _pipeClient.StartEngineWithAppsAsync(
                    selections,
                    cancellationToken);

            engineStarted =
                engineResponse.Equals("ENGINE_STARTED", StringComparison.OrdinalIgnoreCase) ||
                engineResponse.Equals("ENGINE_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase);

            if (!engineStarted)
            {
                break;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(2),
                cancellationToken);

            var nextPending = new List<string>();

            foreach (string appId in stillPending)
            {
                bool nowReachable =
                    await IsFullyReachableDirectAsync(
                        appId,
                        cancellationToken);

                if (nowReachable)
                {
                    appResults.Add(
                        new AppProtectionResult
                        {
                            AppId = appId,
                            Outcome = AppProtectionOutcome.Active,
                            StrategyIndex = currentIndex[appId],
                            StrategyCount = candidateCounts[appId]
                        });

                    lastGoodStrategyIndex[appId] = currentIndex[appId];
                }
                else
                {
                    nextPending.Add(appId);
                    currentIndex[appId] =
                        (currentIndex[appId] + 1) % Math.Max(candidateCounts[appId], 1);
                }
            }

            stillPending = nextPending;
        }

        foreach (string appId in stillPending)
        {
            appResults.Add(
                new AppProtectionResult
                {
                    AppId = appId,
                    Outcome = AppProtectionOutcome.BestEffortFailed,
                    StrategyIndex = currentIndex[appId],
                    StrategyCount = candidateCounts.GetValueOrDefault(appId, 1)
                });

            lastGoodStrategyIndex[appId] = currentIndex[appId];
        }

        return (engineStarted, engineResponse, appResults);
    }

    private async Task<bool> IsFullyReachableDirectAsync(
        string appId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DiagnosticsTarget> targets =
            DiagnosticsTargetCatalog.GetTargetsForApp(appId);

        if (targets.Count == 0)
        {
            return false;
        }

        foreach (DiagnosticsTarget target in targets)
        {
            DiagnosticsResult result =
                await _diagnosticsService.CheckTargetAsync(
                    target,
                    useSystemProxy: false,
                    cancellationToken);

            if (!result.IsSuccessful)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<int> GetCandidateCountAsync(
        string appId,
        CancellationToken cancellationToken)
    {
        string catalogJson =
            await _pipeClient.GetStrategyCatalogAsync(
                appId,
                cancellationToken);

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(catalogJson);

            if (document.RootElement.TryGetProperty(
                    "Strategies",
                    out JsonElement strategies) &&
                strategies.ValueKind == JsonValueKind.Array)
            {
                int count = strategies.GetArrayLength();

                return count > 0
                    ? count
                    : 1;
            }
        }
        catch (JsonException)
        {
            // Catalog unavailable/malformed — fall back to a single candidate.
        }

        return 1;
    }

    private static int ClampIndex(
        int index,
        int candidateCount)
    {
        if (candidateCount <= 0)
        {
            return 0;
        }

        return Math.Clamp(index, 0, candidateCount - 1);
    }
}
