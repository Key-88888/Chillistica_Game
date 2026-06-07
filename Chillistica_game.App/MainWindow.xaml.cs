using System.Windows;
using System.Windows.Media;

namespace Chillistica_game.App;

public partial class MainWindow : Window
{
    private bool _protectionEnabled;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ToggleProtectionButton_Click(object sender, RoutedEventArgs e)
    {
        _protectionEnabled = !_protectionEnabled;

        if (_protectionEnabled)
        {
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(70, 190, 120));
            StatusText.Text = "Защита включена";
            StatusDescription.Text = "Сейчас работает демонстрационный режим";
            ToggleProtectionButton.Content = "Выключить защиту";
            EventText.Text = "Демонстрационная защита включена";
        }
        else
        {
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(224, 90, 90));
            StatusText.Text = "Защита выключена";
            StatusDescription.Text = "Сетевой движок пока не запущен";
            ToggleProtectionButton.Content = "Включить защиту";
            EventText.Text = "Защита выключена";
        }
    }

    private void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        EventText.Text = "Диагностика будет подключена на следующем этапе";

        MessageBox.Show(
            "Интерфейс диагностики работает.\n\nСетевые проверки пока не подключены.",
            "Chillistica_game",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void AutoTuneButton_Click(object sender, RoutedEventArgs e)
    {
        EventText.Text = "Автоматический подбор будет добавлен после подключения движка";

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

    private static string ProfileState(bool? enabled)
    {
        return enabled == true ? "включён" : "выключен";
    }
}
