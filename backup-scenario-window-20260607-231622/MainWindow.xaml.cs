using System.Text;
using System.Windows;
using System.Windows.Media;
using Chillistica_game.App.Services;

namespace Chillistica_game.App;

public partial class MainWindow : Window
{
    private readonly DiagnosticsService _diagnosticsService = new();
    private readonly ScenarioPlanner _scenarioPlanner = new();

    private readonly List<DiagnosticsResult> _lastDiagnosticsResults = new();

    private bool _protectionEnabled;
    private bool _diagnosticsRunning;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ToggleProtectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _protectionEnabled = !_protectionEnabled;

        if (_protectionEnabled)
        {
            StatusIndicator.Fill =
                new SolidColorBrush(
                    Color.FromRgb(59, 106, 82));

            StatusText.Text = "Защита включена";

            StatusDescription.Text =
                "Сейчас работает демонстрационный режим";

            ToggleProtectionButton.Content =
                "Выключить защиту";

            EventText.Text =
                "Демонстрационная защита включена";
        }
        else
        {
            StatusIndicator.Fill =
                new SolidColorBrush(
                    Color.FromRgb(140, 90, 77));

            StatusText.Text = "Защита выключена";

            StatusDescription.Text =
                "Сетевой движок пока не запущен";

            ToggleProtectionButton.Content =
                "Включить защиту";

            EventText.Text =
                "Защита выключена";
        }
    }

    private async void DiagnosticsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_diagnosticsRunning)
        {
            return;
        }

        List<DiagnosticsTarget> targets =
            BuildDiagnosticsTargets();

        if (targets.Count == 0)
        {
            MessageBox.Show(
                "Выбери хотя бы один профиль приложения.",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _diagnosticsRunning = true;

        StatusDescription.Text =
            "Выполняется углублённая проверка соединений";

        EventText.Text =
            $"Диагностика: 0 из {targets.Count}";

        try
        {
            List<DiagnosticsResult> results = new();

            for (int index = 0; index < targets.Count; index++)
            {
                DiagnosticsTarget target = targets[index];

                EventText.Text =
                    $"Проверка {target.ServiceName}: {index + 1} из {targets.Count}";

                DiagnosticsResult directResult =
                    await _diagnosticsService.CheckTargetAsync(
                        target,
                        useSystemProxy: false);

                results.Add(directResult);

                DiagnosticsResult proxyResult =
                    await _diagnosticsService.CheckTargetAsync(
                        target,
                        useSystemProxy: true);

                results.Add(proxyResult);
            }

            _lastDiagnosticsResults.Clear();
            _lastDiagnosticsResults.AddRange(results);

            ShowDiagnosticsResults(results);
        }
        catch (Exception ex)
        {
            EventText.Text =
                "Диагностика завершилась с ошибкой";

            MessageBox.Show(
                $"Не удалось завершить диагностику.\n\n{ex.Message}",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _diagnosticsRunning = false;

            StatusDescription.Text = _protectionEnabled
                ? "Сейчас работает демонстрационный режим"
                : "Сетевой движок пока не запущен";
        }
    }

    private void AutoTuneButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_lastDiagnosticsResults.Count == 0)
        {
            MessageBox.Show(
                "Сначала запусти диагностику.\n\nАвтоподбор использует последние результаты проверок.",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        IReadOnlyList<ScenarioDecision> decisions =
            _scenarioPlanner.BuildDecisions(
                _lastDiagnosticsResults);

        StringBuilder report = new();

        foreach (ScenarioDecision decision in decisions)
        {
            report.AppendLine(decision.ToDisplayText());
            report.AppendLine();
        }

        EventText.Text =
            $"Автоподбор: создано сценариев {decisions.Count}";

        MessageBox.Show(
            report.ToString().Trim(),
            "Автоматический подбор сценария",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private List<DiagnosticsTarget> BuildDiagnosticsTargets()
    {
        List<DiagnosticsTarget> targets = new();

        if (YouTubeProfile.IsChecked == true)
        {
            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "YouTube Web",
                Host = "www.youtube.com"
            });

            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Google Video",
                Host = "googlevideo.com"
            });
        }

        if (DiscordProfile.IsChecked == true)
        {
            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Discord Web",
                Host = "discord.com"
            });

            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Discord Gateway",
                Host = "gateway.discord.gg"
            });

            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Discord CDN",
                Host = "cdn.discordapp.com"
            });
        }

        if (RobloxProfile.IsChecked == true)
        {
            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Roblox Web",
                Host = "www.roblox.com"
            });

            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Roblox API",
                Host = "games.roblox.com"
            });

            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Roblox Presence",
                Host = "presence.roblox.com"
            });
        }

        if (FortniteProfile.IsChecked == true)
        {
            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Epic Web",
                Host = "www.epicgames.com"
            });

            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Epic Account",
                Host = "account-public-service-prod.ol.epicgames.com"
            });

            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Epic Lightswitch",
                Host = "lightswitch-public-service-prod.ol.epicgames.com"
            });

            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Fortnite Public Service",
                Host = "fortnite-public-service-prod11.ol.epicgames.com"
            });

            targets.Add(new DiagnosticsTarget
            {
                ServiceName = "Epic XMPP",
                Host = "xmpp-service-prod.ol.epicgames.com",
                Port = 5222,
                CheckHttps = false
            });
        }

        return targets;
    }

    private void ShowDiagnosticsResults(
        IReadOnlyCollection<DiagnosticsResult> results)
    {
        int successful =
            results.Count(result => result.IsSuccessful);

        int failed =
            results.Count - successful;

        EventText.Text =
            failed == 0
                ? $"Диагностика завершена: {successful} из {results.Count} работают"
                : $"Обнаружены проблемы: {failed} из {results.Count}";

        var diagnosticsWindow =
            new DiagnosticsWindow(results)
            {
                Owner = this
            };

        diagnosticsWindow.ShowDialog();
    }

    private static string ProfileState(bool? enabled)
    {
        return enabled == true
            ? "включён"
            : "выключен";
    }
}
