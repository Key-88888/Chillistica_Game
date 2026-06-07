using System.Windows;
using Chillistica_game.App.Services;

namespace Chillistica_game.App;

public partial class ScenarioWindow : Window
{
    public ScenarioWindow(
        IReadOnlyCollection<ScenarioDecision> decisions)
    {
        InitializeComponent();

        ScenarioGrid.ItemsSource = decisions;

        SummaryText.Text =
            $"Подготовлено сценариев: {decisions.Count}";
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
