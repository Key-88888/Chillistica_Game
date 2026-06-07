namespace Chillistica_game.App.Services;

public sealed class ScenarioPlanner
{
    public IReadOnlyList<ScenarioDecision> BuildDecisions(
        IReadOnlyCollection<DiagnosticsResult> results)
    {
        List<ScenarioDecision> decisions = new();

        var appGroups =
            results
                .GroupBy(result => GetAppName(result.ServiceName))
                .OrderBy(group => group.Key);

        foreach (var appGroup in appGroups)
        {
            List<DiagnosticsResult> appResults =
                appGroup.ToList();

            List<DiagnosticsResult> directResults =
                appResults
                    .Where(result => result.Mode == "Direct")
                    .ToList();

            List<DiagnosticsResult> proxyResults =
                appResults
                    .Where(result => result.Mode == "System Proxy")
                    .ToList();

            int directOk =
                directResults.Count(result => result.IsSuccessful);

            int proxyOk =
                proxyResults.Count(result => result.IsSuccessful);

            int directTotal =
                directResults.Count;

            int proxyTotal =
                proxyResults.Count;

            decisions.Add(
                BuildDecision(
                    appGroup.Key,
                    appResults,
                    directOk,
                    directTotal,
                    proxyOk,
                    proxyTotal));
        }

        return decisions;
    }

    private static ScenarioDecision BuildDecision(
        string appName,
        IReadOnlyCollection<DiagnosticsResult> appResults,
        int directOk,
        int directTotal,
        int proxyOk,
        int proxyTotal)
    {
        bool directAllOk =
            directTotal > 0 &&
            directOk == directTotal;

        bool proxyAllOk =
            proxyTotal > 0 &&
            proxyOk == proxyTotal;

        bool directAllBroken =
            directTotal > 0 &&
            directOk == 0;

        bool proxyAllBroken =
            proxyTotal > 0 &&
            proxyOk == 0;

        bool proxyBetter =
            proxyOk > directOk;

        bool hasTcpOpenHttpsBroken =
            appResults.Any(result =>
                result.DnsSuccess &&
                result.TcpSuccess &&
                !result.HttpsSuccess &&
                !result.HttpsSkipped);

        bool hasXmppProblem =
            appResults.Any(result =>
                result.ServiceName.Contains("XMPP") &&
                !result.IsSuccessful);

        bool hasEpicAccountProblem =
            appResults.Any(result =>
                result.ServiceName.Contains("Epic Account") &&
                !result.IsSuccessful);

        bool hasGoogleVideoProblem =
            appResults.Any(result =>
                result.ServiceName.Contains("Google Video") &&
                !result.IsSuccessful);

        if (directAllOk)
        {
            return new ScenarioDecision
            {
                AppName = appName,
                RecommendedMode = "Direct",
                Reason = "Все проверенные соединения работают напрямую.",
                RiskLevel = "низкий",
                NextAction = "Не применять обход для этого приложения."
            };
        }

        if (proxyBetter && hasTcpOpenHttpsBroken)
        {
            if (appName == "Fortnite / Epic")
            {
                return new ScenarioDecision
                {
                    AppName = appName,
                    RecommendedMode = "Game profile: Proxy fallback + selective DPI",
                    Reason = BuildFortniteReason(
                        hasEpicAccountProblem,
                        hasXmppProblem),
                    RiskLevel = "средний",
                    NextAction = "Сначала чинить Epic Account и XMPP/5222. Игровой UDP пока не трогать."
                };
            }

            if (appName == "YouTube" && hasGoogleVideoProblem)
            {
                return new ScenarioDecision
                {
                    AppName = appName,
                    RecommendedMode = "DPI Bypass candidate",
                    Reason = "Web-часть и video/CDN ведут себя по-разному. Нужен отдельный сценарий для Google Video.",
                    RiskLevel = "низкий",
                    NextAction = "Проверить несколько мягких TLS/HTTPS desync-стратегий."
                };
            }

            return new ScenarioDecision
            {
                AppName = appName,
                RecommendedMode = "DPI Bypass candidate",
                Reason = "TCP 443 доступен, но HTTPS напрямую ломается. Через системный прокси работает лучше.",
                RiskLevel = "низкий",
                NextAction = "Проверить мягкие стратегии desync без вмешательства в UDP."
            };
        }

        if (proxyBetter)
        {
            return new ScenarioDecision
            {
                AppName = appName,
                RecommendedMode = "Proxy fallback candidate",
                Reason = "Через системный прокси работает лучше, чем напрямую.",
                RiskLevel = "низкий",
                NextAction = "Для этого приложения можно использовать fallback через прокси."
            };
        }

        if (directAllBroken && proxyAllBroken)
        {
            return new ScenarioDecision
            {
                AppName = appName,
                RecommendedMode = "Needs manual check",
                Reason = "Не работает ни напрямую, ни через системный прокси.",
                RiskLevel = "неизвестный",
                NextAction = "Проверить DNS, домены, блокировку маршрута или актуальность endpoint."
            };
        }

        if (!directAllOk && !proxyAllOk)
        {
            return new ScenarioDecision
            {
                AppName = appName,
                RecommendedMode = "Mixed / selective profile",
                Reason = "Часть endpoint работает, часть нет.",
                RiskLevel = "средний",
                NextAction = "Создать точечный профиль только для проблемных соединений."
            };
        }

        return new ScenarioDecision
        {
            AppName = appName,
            RecommendedMode = "Needs more data",
            Reason = "Недостаточно данных для уверенного выбора.",
            RiskLevel = "неизвестный",
            NextAction = "Запустить диагностику повторно с включённым и выключенным системным прокси."
        };
    }

    private static string BuildFortniteReason(
        bool hasEpicAccountProblem,
        bool hasXmppProblem)
    {
        if (hasEpicAccountProblem && hasXmppProblem)
        {
            return "Проблемны Epic Account и XMPP/5222. Это может влиять на авторизацию, запуск игр, social/presence и голос.";
        }

        if (hasEpicAccountProblem)
        {
            return "Проблемен Epic Account. Это может влиять на авторизацию и запуск игр.";
        }

        if (hasXmppProblem)
        {
            return "Проблемен XMPP/5222. Это может влиять на social/presence, чат и голос.";
        }

        return "Часть Epic/Fortnite endpoint работает, часть нет. Нужен выборочный игровой профиль.";
    }

    private static string GetAppName(string serviceName)
    {
        if (serviceName.StartsWith("YouTube") ||
            serviceName.StartsWith("Google Video"))
        {
            return "YouTube";
        }

        if (serviceName.StartsWith("Discord"))
        {
            return "Discord";
        }

        if (serviceName.StartsWith("Roblox"))
        {
            return "Roblox";
        }

        if (serviceName.StartsWith("Epic") ||
            serviceName.StartsWith("Fortnite"))
        {
            return "Fortnite / Epic";
        }

        return serviceName;
    }
}
