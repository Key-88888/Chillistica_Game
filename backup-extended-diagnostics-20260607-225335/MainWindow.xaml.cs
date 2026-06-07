using System.Text;
using System.Windows;
using System.Windows.Media;
using Chillistica_game.App.Services;

namespace Chillistica_game.App;

public partial class MainWindow : Window
{
    private readonly DiagnosticsService _diagnosticsService = new();

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

        List<(string Name, string Host)> targets =
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
            "Выполняется проверка соединений";

        EventText.Text =
            $"Диагностика: 0 из {targets.Count}";

        try
        {
            List<DiagnosticsResult> results = new();

            for (int index = 0; index < targets.Count; index++)
            {
                (string name, string host) = targets[index];

                EventText.Text =
                    $"Проверка {name}: {index + 1} из {targets.Count}";

                DiagnosticsResult result =
                    await _diagnosticsService.CheckServiceAsync(
                        name,
                        host);

                results.Add(result);
            }

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
        EventText.Text =
            "Автоматический подбор будет добавлен после подключения движка";

        MessageBox.Show(
            "Выбранные профили:\n" +
            $"YouTube: {ProfileState(YouTubeProfile.IsChecked)}\n" +
            $"Discord: {ProfileState(DiscordProfile.IsChecked)}\n" +
            $"Roblox: {ProfileState(RobloxProfile.IsChecked)}\n" +
            $"Fortnite: {ProfileState(FortniteProfile.IsChecked)}",
            "Профили приложений",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private List<(string Name, string Host)>
        BuildDiagnosticsTargets()
    {
        List<(string Name, string Host)> targets = new();

        if (YouTubeProfile.IsChecked == true)
        {
            targets.Add(("YouTube", "www.youtube.com"));
        }

        if (DiscordProfile.IsChecked == true)
        {
            targets.Add(("Discord", "discord.com"));
        }

        if (RobloxProfile.IsChecked == true)
        {
            targets.Add(("Roblox", "www.roblox.com"));
        }

        if (FortniteProfile.IsChecked == true)
        {
            targets.Add(("Fortnite / Epic", "www.epicgames.com"));
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

        StringBuilder report = new();

        foreach (DiagnosticsResult result in results)
        {
            report.AppendLine(result.ToDisplayText());
            report.AppendLine();
        }

        report.AppendLine(
            $"Итог: работает {successful}, проблем {failed}");

        MessageBox.Show(
            report.ToString().Trim(),
            "Результаты диагностики",
            MessageBoxButton.OK,
            failed == 0
                ? MessageBoxImage.Information
                : MessageBoxImage.Warning);
    }

    private static string ProfileState(bool? enabled)
    {
        return enabled == true
            ? "включён"
            : "выключен";
    }
}
