namespace Chillistica_game.App.Services;

/// <summary>
/// Drives the one-button flow: skip apps already reachable directly, then for the
/// rest launch winws and auto-cycle through each app's candidate strategies until
/// every app becomes reachable (remembering the last-good index). Talks to the
/// in-process <see cref="WinwsEngine"/> rather than the old LocalSystem service.
/// </summary>
public sealed class StrategyOrchestrator
{
    private const int MaxFallbackRounds = 4;

    private readonly WinwsEngine _engine;
    private readonly DiagnosticsService _diagnosticsService;

    public StrategyOrchestrator(
        WinwsEngine engine,
        DiagnosticsService diagnosticsService)
    {
        _engine = engine;
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
                await IsFullyReachableDirectAsync(appId, cancellationToken);

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

        var candidateCounts =
            bypassAppIds.ToDictionary(
                appId => appId,
                StrategyComposer.GetCandidateCount);

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

        // State of the MOST RECENT probe only — never accumulated across rounds.
        var probedIndex = new Dictionary<string, int>(currentIndex);
        var reachable = bypassAppIds.ToDictionary(appId => appId, _ => false);

        for (int round = 0; round < MaxFallbackRounds; round++)
        {
            // Every round resends the FULL bypass set, so apps that already work
            // keep their index instead of dropping out of the composed argv.
            var selections =
                bypassAppIds.ToDictionary(
                    appId => appId,
                    appId => currentIndex[appId]);

            engineResponse =
                await _engine.StartWithAppsAsync(selections, cancellationToken);

            engineStarted =
                engineResponse.Equals("ENGINE_STARTED", StringComparison.OrdinalIgnoreCase) ||
                engineResponse.Equals("ENGINE_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase);

            if (!engineStarted)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            // Re-probe the FULL set, not just the apps still failing. The composed
            // command line changes between rounds, so a later round can regress an
            // app an earlier round had working (e.g. an unscoped --filter-tcp
            // fragment shadowing a hostlist-scoped one). Carrying forward an
            // earlier "Active" would report a bypass that is no longer live.
            probedIndex = new Dictionary<string, int>(selections);

            var failing = new List<string>();

            foreach (string appId in bypassAppIds)
            {
                bool nowReachable =
                    await IsFullyReachableDirectAsync(appId, cancellationToken);

                reachable[appId] = nowReachable;

                if (!nowReachable)
                {
                    failing.Add(appId);
                }
            }

            if (failing.Count == 0)
            {
                break;
            }

            foreach (string appId in failing)
            {
                currentIndex[appId] =
                    (currentIndex[appId] + 1) % Math.Max(candidateCounts[appId], 1);
            }
        }

        foreach (string appId in bypassAppIds)
        {
            int usedIndex =
                probedIndex.TryGetValue(appId, out int idx) ? idx : currentIndex[appId];

            if (reachable[appId])
            {
                appResults.Add(
                    new AppProtectionResult
                    {
                        AppId = appId,
                        Outcome = AppProtectionOutcome.Active,
                        StrategyIndex = usedIndex,
                        StrategyCount = candidateCounts[appId]
                    });

                // Only a strategy that actually worked is worth remembering.
                lastGoodStrategyIndex[appId] = usedIndex;
            }
            else
            {
                appResults.Add(
                    new AppProtectionResult
                    {
                        AppId = appId,
                        Outcome = AppProtectionOutcome.BestEffortFailed,
                        StrategyIndex = usedIndex,
                        StrategyCount = candidateCounts.GetValueOrDefault(appId, 1)
                    });

                // Deliberately NOT persisted: saving a failed index poisons the
                // next run, which would then start from a strategy already known
                // not to work for this user.
            }
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

    private static int ClampIndex(int index, int candidateCount)
    {
        if (candidateCount <= 0)
        {
            return 0;
        }

        return Math.Clamp(index, 0, candidateCount - 1);
    }
}
