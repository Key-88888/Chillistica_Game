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

        var groups =
            results
                .GroupBy(result => result.ServiceName)
                .OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            DiagnosticsResult? direct =
                group.FirstOrDefault(result =>
                    result.Mode == "Direct");

            DiagnosticsResult? proxy =
                group.FirstOrDefault(result =>
                    result.Mode == "System Proxy");

            if (direct is null || proxy is null)
            {
                continue;
            }

            if (!direct.IsSuccessful && proxy.IsSuccessful)
            {
                lines.Add(
                    $"{group.Key}: напрямую не работает, через системный прокси работает. Нужен DPI bypass или proxy fallback.");
            }
            else if (direct.IsSuccessful && proxy.IsSuccessful)
            {
                lines.Add(
                    $"{group.Key}: работает напрямую. Обход не нужен.");
            }
            else if (direct.IsSuccessful && !proxy.IsSuccessful)
            {
                lines.Add(
                    $"{group.Key}: напрямую работает, но системный прокси ломает соединение. Нужно проверить v2rayN/Happ.");
            }
            else
            {
                lines.Add(
                    $"{group.Key}: не работает ни напрямую, ни через системный прокси. Нужна отдельная проверка DNS, блокировки или маршрута.");
            }
        }

        if (lines.Count == 0)
        {
            return "Недостаточно данных для рекомендации.";
        }

        return string.Join("\n", lines);
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
