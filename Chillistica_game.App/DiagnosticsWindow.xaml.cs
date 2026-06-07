using System.Windows;
using Chillistica_game.App.Services;

namespace Chillistica_game.App;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(
        IReadOnlyCollection<DiagnosticsResult> results)
    {
        InitializeComponent();

        List<DiagnosticsRow> rows =
            results
                .Select(DiagnosticsRow.FromResult)
                .ToList();

        ResultsGrid.ItemsSource = rows;

        int successful =
            results.Count(result => result.IsSuccessful);

        int failed =
            results.Count - successful;

        SummaryText.Text =
            $"Проверено соединений: {results.Count}";

        SummaryBadge.Text =
            failed == 0
                ? $"Работают все: {successful}"
                : $"Работают: {successful} · Проблемы: {failed}";

        RecommendationText.Text =
            BuildRecommendation(results);
    }

    private static string BuildRecommendation(
        IReadOnlyCollection<DiagnosticsResult> results)
    {
        List<string> lines = new();

        var appGroups =
            results
                .GroupBy(result => GetAppName(result.ServiceName))
                .OrderBy(group => group.Key);

        foreach (var appGroup in appGroups)
        {
            var directResults =
                appGroup
                    .Where(result => result.Mode == "Direct")
                    .ToList();

            var proxyResults =
                appGroup
                    .Where(result => result.Mode == "System Proxy")
                    .ToList();

            int directTotal = directResults.Count;
            int proxyTotal = proxyResults.Count;

            int directOk =
                directResults.Count(result => result.IsSuccessful);

            int proxyOk =
                proxyResults.Count(result => result.IsSuccessful);

            string directState =
                GetStateText(directOk, directTotal);

            string proxyState =
                GetStateText(proxyOk, proxyTotal);

            string recommendation =
                GetRecommendation(
                    appGroup.Key,
                    directOk,
                    directTotal,
                    proxyOk,
                    proxyTotal,
                    appGroup.ToList());

            lines.Add(
                $"{appGroup.Key}: Direct — {directState}; System Proxy — {proxyState}. {recommendation}");
        }

        if (lines.Count == 0)
        {
            return "Недостаточно данных для рекомендации.";
        }

        return string.Join("\n", lines);
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

    private static string GetStateText(
        int successful,
        int total)
    {
        if (total == 0)
        {
            return "нет данных";
        }

        if (successful == total)
        {
            return $"работает {successful}/{total}";
        }

        if (successful == 0)
        {
            return $"не работает 0/{total}";
        }

        return $"частично {successful}/{total}";
    }

    private static string GetRecommendation(
        string appName,
        int directOk,
        int directTotal,
        int proxyOk,
        int proxyTotal,
        IReadOnlyCollection<DiagnosticsResult> appResults)
    {
        bool directWorks =
            directTotal > 0 &&
            directOk == directTotal;

        bool proxyWorks =
            proxyTotal > 0 &&
            proxyOk == proxyTotal;

        bool directBroken =
            directTotal > 0 &&
            directOk == 0;

        bool proxyBroken =
            proxyTotal > 0 &&
            proxyOk == 0;

        bool proxyBetterThanDirect =
            proxyOk > directOk;

        bool hasEpicAccountProblem =
            appResults.Any(result =>
                result.ServiceName.Contains("Epic Account") &&
                !result.IsSuccessful);

        bool hasXmppProblem =
            appResults.Any(result =>
                result.ServiceName.Contains("XMPP") &&
                !result.IsSuccessful);

        bool hasGoogleVideoProblem =
            appResults.Any(result =>
                result.ServiceName.Contains("Google Video") &&
                !result.IsSuccessful);

        if (directWorks && proxyWorks)
        {
            return "Обход не нужен.";
        }

        if (proxyBetterThanDirect)
        {
            if (appName == "Fortnite / Epic")
            {
                if (hasEpicAccountProblem && hasXmppProblem)
                {
                    return "Нужен отдельный игровой профиль: Epic Account и XMPP/5222 требуют отдельного сценария.";
                }

                if (hasXmppProblem)
                {
                    return "Основные сервисы частично доступны, но XMPP/5222 проблемный. Это может влиять на чат, presence и голос.";
                }

                if (hasEpicAccountProblem)
                {
                    return "Проблема в Epic Account. Это может ломать авторизацию и запуск игр.";
                }

                return "Нужен мягкий DPI bypass или proxy fallback для проблемных Epic-сервисов.";
            }

            if (appName == "YouTube" && hasGoogleVideoProblem)
            {
                return "Web через прокси работает, но video/CDN проблемный. Нужен отдельный сценарий для Google Video.";
            }

            return "Кандидат для DPI bypass или proxy fallback.";
        }

        if (directBroken && proxyBroken)
        {
            return "Не работает ни напрямую, ни через системный прокси. Нужна отдельная проверка DNS, маршрута или конкретного домена.";
        }

        if (directWorks && proxyBroken)
        {
            return "Напрямую работает, но системный прокси мешает. Нужно проверить настройки v2rayN/Happ.";
        }

        if (directOk > 0 && directOk < directTotal)
        {
            return "Работает частично. Нужен точечный сценарий только для проблемных endpoint.";
        }

        return "Нужна дополнительная диагностика.";
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}

public sealed class DiagnosticsRow
{
    public required string ServiceName { get; init; }

    public required string Mode { get; init; }

    public required string Endpoint { get; init; }

    public required string DnsText { get; init; }

    public required string TcpText { get; init; }

    public required string HttpsText { get; init; }

    public required string StatusText { get; init; }

    public required string ErrorText { get; init; }

    public static DiagnosticsRow FromResult(
        DiagnosticsResult result)
    {
        string endpoint =
            result.Port == 443
                ? result.Host
                : $"{result.Host}:{result.Port}";

        string tcpText =
            result.TcpSuccess
                ? $"{result.TcpLatencyMs} мс"
                : "нет";

        string httpsText =
            result.HttpsSkipped
                ? "не проверялся"
                : result.HttpsSuccess
                    ? $"{result.HttpsLatencyMs} мс"
                    : "нет";

        return new DiagnosticsRow
        {
            ServiceName = result.ServiceName,
            Mode = result.Mode,
            Endpoint = endpoint,
            DnsText = result.DnsSuccess ? "да" : "нет",
            TcpText = tcpText,
            HttpsText = httpsText,
            StatusText =
                result.IsSuccessful
                    ? "работает"
                    : "проблема",
            ErrorText =
                result.Error ?? string.Empty
        };
    }
}
