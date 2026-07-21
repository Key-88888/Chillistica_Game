using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chillistica_game.App.Services;

namespace Chillistica_game.App;

public partial class MainWindow : Window
{
    private readonly DiagnosticsService _diagnosticsService = new();
    private readonly ScenarioPlanner _scenarioPlanner = new();
    private readonly ProcessDetectionService _processDetectionService = new();
    private readonly SettingsService _settingsService = new();
    private readonly AppLogger _logger = new();
    private readonly UpdateCheckService _updateCheckService = new();
    private readonly WinwsEngine _engine;

    private const string ReleasesPageUrl =
        "https://github.com/Key-88888/Chillistica_Game/releases/latest";

    private static readonly Color ActiveColor = Color.FromRgb(59, 106, 82);
    private static readonly Color InactiveColor = Color.FromRgb(140, 90, 77);

    private UpdateCheckResult? _pendingUpdate;
    private readonly List<DiagnosticsResult> _lastDiagnosticsResults = new();

    private AppSettings _settings = new();

    private bool _protectionEnabled;
    private bool _busy;

    // Cancels an in-flight enable flow when the window closes.
    private CancellationTokenSource? _enableCts;

    public MainWindow()
    {
        InitializeComponent();

        _engine = new WinwsEngine(
            (stage, result) => _logger.Info(stage: stage, result: result));

        _engine.EngineExitedUnexpectedly += OnEngineExitedUnexpectedly;

        _logger.Info(stage: "Application", result: "Started");

        LoadSettings();
        AttachProfileChangeHandlers();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // No service to probe anymore — the app owns the engine directly. A fresh
        // launch always starts with the engine stopped (any previous winws was
        // killed when the last session exited).
        SetStatus(
            active: false,
            title: "Защита выключена",
            description: "Нажмите «Включить защиту» — приложения проверятся автоматически");

        ToggleProtectionButton.IsEnabled = true;
        EventText.Text = "Готово к работе";

        _ = CheckForUpdateInBackgroundAsync();
    }

    // ---- One-button flow -------------------------------------------------

    private async void ToggleProtectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_protectionEnabled || _engine.IsRunning)
        {
            await DisableProtectionAsync();
            return;
        }

        await EnableProtectionAsync();
    }

    private async Task EnableProtectionAsync()
    {
        List<string> checkedAppIds = GetCheckedAppIds();

        if (checkedAppIds.Count == 0)
        {
            MessageBox.Show(
                "Выберите хотя бы одно приложение.",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _busy = true;
        ToggleProtectionButton.IsEnabled = false;
        ToggleProtectionButton.Content = "Настройка...";

        SetStatus(
            active: true,
            title: "Идёт настройка",
            description: "Проверяем приложения, соединение и подбираем сценарий");

        EventText.Text = "Проверяем доступность и включаем обход";

        _logger.Info(stage: "ProtectionAnalysis", result: "Started");

        // Closing the window mid-flow must abort the fallback loop: it can sit in
        // multi-second probes, and a round that starts after teardown began would
        // launch an engine the job object no longer guards.
        _enableCts?.Dispose();
        _enableCts = new CancellationTokenSource();

        try
        {
            var orchestrator =
                new StrategyOrchestrator(_engine, _diagnosticsService);

            (bool engineStarted, string engineResponse, IReadOnlyList<AppProtectionResult> appResults) =
                await orchestrator.EnableAsync(
                    checkedAppIds,
                    _settings.LastGoodStrategyIndex,
                    _enableCts.Token);

            _settingsService.Save(_settings);
            UpdateScenarioLabelsFromProtectionResults(appResults);

            if (engineResponse.Equals(
                    "ALL_DIRECT_NO_BYPASS_NEEDED",
                    StringComparison.OrdinalIgnoreCase))
            {
                _protectionEnabled = false;

                SetStatus(
                    active: false,
                    title: "Обход не требуется",
                    description: "Все выбранные сервисы уже доступны напрямую");

                ToggleProtectionButton.Content = "Включить защиту";
                EventText.Text = "Прямое соединение уже работает — движок не запускался";

                _logger.Info(
                    stage: "ProtectionAnalysis",
                    result: "Completed; allDirectNoBypassNeeded=true");

                return;
            }

            if (!engineStarted || !_engine.IsRunning)
            {
                await _engine.StopAsync();

                throw new InvalidOperationException(
                    $"Движок не запустился: {engineResponse}. {_engine.RecentOutput}".Trim());
            }

            _protectionEnabled = true;

            int active = appResults.Count(r => r.Outcome == AppProtectionOutcome.Active);
            int skipped = appResults.Count(r => r.Outcome == AppProtectionOutcome.Skipped);
            int bestEffort = appResults.Count(r => r.Outcome == AppProtectionOutcome.BestEffortFailed);

            SetStatus(
                active: true,
                title: "Защита включена",
                description: "Обход активен для выбранных приложений");

            ToggleProtectionButton.Content = "Выключить защиту";
            EventText.Text =
                $"Готово: активно {active}, уже доступно напрямую {skipped}, best-effort {bestEffort}";

            _logger.Info(
                stage: "ProtectionAnalysis",
                result: $"Completed; active={active}; skipped={skipped}; bestEffort={bestEffort}");
        }
        catch (OperationCanceledException)
        {
            // The window is closing; MainWindow_Closing already tore the engine
            // down. Do not pop an error dialog during shutdown.
            return;
        }
        catch (Exception ex)
        {
            _protectionEnabled = false;

            SetStatus(
                active: false,
                title: "Ошибка настройки",
                description: "Автоматическая проверка не завершилась");

            ToggleProtectionButton.Content = "Включить защиту";
            EventText.Text = $"Ошибка: {ex.Message}";

            _logger.Error(stage: "ProtectionAnalysis", exception: ex);

            MessageBox.Show(
                $"Не удалось включить защиту.\n\n{ex.Message}",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            ToggleProtectionButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// The engine died on its own after a successful start. Raised on a
    /// threadpool thread, so marshal to the dispatcher before touching the UI.
    /// Without this the window keeps showing "Защита включена" for an engine
    /// that is no longer running.
    /// </summary>
    private void OnEngineExitedUnexpectedly(string exitCode)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_protectionEnabled)
            {
                return;
            }

            _protectionEnabled = false;

            SetStatus(
                active: false,
                title: "Движок остановился",
                description: "Обход прекращён — движок завершился сам");

            ToggleProtectionButton.Content = "Включить защиту";
            ToggleProtectionButton.IsEnabled = true;

            EventText.Text =
                $"Движок неожиданно завершился (код {exitCode}). Нажмите «Включить защиту», чтобы запустить заново.";

            ResetScenarioLabels();
        });
    }

    private async Task DisableProtectionAsync()
    {
        _busy = true;
        ToggleProtectionButton.IsEnabled = false;
        EventText.Text = "Останавливаем обход";

        try
        {
            string response = await _engine.StopAsync();

            _protectionEnabled = false;

            SetStatus(
                active: false,
                title: "Защита выключена",
                description: "Сетевой движок остановлен");

            ToggleProtectionButton.Content = "Включить защиту";
            EventText.Text = "Обход выключен";
            ResetScenarioLabels();

            _logger.Info(stage: "ProtectionStop", result: response);
        }
        catch (Exception ex)
        {
            EventText.Text = $"Ошибка остановки: {ex.Message}";
            _logger.Error(stage: "ProtectionStop", exception: ex);
        }
        finally
        {
            _busy = false;
            ToggleProtectionButton.IsEnabled = true;
        }
    }

    // ---- Updates (check + open release page) -----------------------------

    private async Task CheckForUpdateInBackgroundAsync()
    {
        try
        {
            UpdateCheckResult? update =
                await _updateCheckService.CheckForUpdateAsync();

            if (update is null)
            {
                return;
            }

            _pendingUpdate = update;

            UpdateBannerText.Text = $"Доступно обновление {update.TagName}";
            UpdateBanner.Visibility = Visibility.Visible;

            _logger.Info(stage: "UpdateCheck", result: $"UpdateAvailable; tag={update.TagName}");
        }
        catch (Exception ex)
        {
            _logger.Error(stage: "UpdateCheck", exception: ex);
        }
    }

    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null)
        {
            return;
        }

        UpdateNowButton.IsEnabled = false;
        UpdateBannerText.Text = $"Скачиваем {_pendingUpdate.TagName} и проверяем подпись...";

        try
        {
            // Download the package AND its detached signature, and verify against
            // the key pinned into this build BEFORE the user is pointed at the
            // file. Just opening the releases page would mean the signature the
            // release publishes is never checked by anything.
            string staging =
                await _updateCheckService.DownloadAndStageUpdateAsync(
                    _pendingUpdate.DownloadUrl,
                    _pendingUpdate.SignatureUrl);

            UpdateCheckService.RevealVerifiedPackage(staging);

            UpdateBannerText.Text =
                $"{_pendingUpdate.TagName}: подпись проверена, архив открыт в проводнике";

            _logger.Info(
                stage: "Update",
                result: $"VerifiedAndRevealed; tag={_pendingUpdate.TagName}");

            MessageBox.Show(
                "Обновление скачано, и его подпись проверена встроенным ключом.\n\n" +
                "Закройте программу, распакуйте архив поверх текущей папки и запустите заново.",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(stage: "Update", exception: ex);

            UpdateBannerText.Text = "Не удалось проверить обновление";

            // Fall back to the releases page, but say plainly that this copy was
            // NOT signature-checked, so the user can decide.
            MessageBox.Show(
                $"Не удалось скачать или проверить обновление.\n\n{ex.Message}\n\n" +
                $"Можно скачать вручную (подпись при этом не проверена):\n{ReleasesPageUrl}",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ReleasesPageUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // nothing more we can do
            }
        }
        finally
        {
            UpdateNowButton.IsEnabled = true;
        }
    }

    private void DismissUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    // ---- Diagnostics hotkeys (F5 check / F6 auto-tune / F7 processes) -----

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F5:
                await RunDiagnosticsAsync();
                e.Handled = true;
                break;

            case Key.F6:
                ShowAutoTune();
                e.Handled = true;
                break;

            case Key.F7:
                ShowDetectedProcesses();
                e.Handled = true;
                break;
        }
    }

    private async Task RunDiagnosticsAsync()
    {
        if (_busy)
        {
            return;
        }

        List<DiagnosticsTarget> targets = BuildDiagnosticsTargets();

        if (targets.Count == 0)
        {
            MessageBox.Show(
                "Выберите хотя бы одно приложение.",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _busy = true;
        EventText.Text = $"Диагностика: 0 из {targets.Count}";

        try
        {
            var results = new List<DiagnosticsResult>();

            for (int i = 0; i < targets.Count; i++)
            {
                DiagnosticsTarget target = targets[i];
                EventText.Text = $"Проверка {target.ServiceName}: {i + 1} из {targets.Count}";

                results.Add(await _diagnosticsService.CheckTargetAsync(target, useSystemProxy: false));
                results.Add(await _diagnosticsService.CheckTargetAsync(target, useSystemProxy: true));
            }

            _lastDiagnosticsResults.Clear();
            _lastDiagnosticsResults.AddRange(results);

            int ok = results.Count(r => r.IsSuccessful);
            EventText.Text = $"Диагностика завершена: {ok} из {results.Count} работают";

            new DiagnosticsWindow(results) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            EventText.Text = "Диагностика завершилась с ошибкой";
            _logger.Error(stage: "Diagnostics", exception: ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowAutoTune()
    {
        if (_lastDiagnosticsResults.Count == 0)
        {
            MessageBox.Show(
                "Сначала запустите диагностику (F5).",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        IReadOnlyList<ScenarioDecision> decisions =
            _scenarioPlanner.BuildDecisions(_lastDiagnosticsResults);

        new ScenarioWindow(decisions) { Owner = this }.ShowDialog();
    }

    private void ShowDetectedProcesses()
    {
        var report = new StringBuilder();

        foreach (AppProcessStatus status in _processDetectionService.GetStatuses())
        {
            report.AppendLine($"{status.AppName}: {status.StatusText}");
            report.AppendLine($"Процессы: {status.RunningProcessesText}");
            report.AppendLine();
        }

        MessageBox.Show(
            report.ToString().Trim(),
            "Запущенные приложения",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ---- Settings & profile checkboxes -----------------------------------

    private void AttachProfileChangeHandlers()
    {
        foreach (var box in new[] { YouTubeProfile, DiscordProfile, RobloxProfile, FortniteProfile })
        {
            box.Checked += ProfileSelection_Changed;
            box.Unchecked += ProfileSelection_Changed;
        }
    }

    private void ProfileSelection_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox box)
        {
            _logger.Info(
                stage: "ProfileChanged",
                app: box.Name,
                result: box.IsChecked == true ? "Enabled" : "Disabled");
        }
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load();

        YouTubeProfile.IsChecked = _settings.YouTubeEnabled;
        DiscordProfile.IsChecked = _settings.DiscordEnabled;
        RobloxProfile.IsChecked = _settings.RobloxEnabled;
        FortniteProfile.IsChecked = _settings.FortniteEnabled;

        _logger.Info(stage: "Settings", result: $"Loaded; schema={_settings.SchemaVersion}");
    }

    private void SaveSettings()
    {
        _settings.YouTubeEnabled = YouTubeProfile.IsChecked == true;
        _settings.DiscordEnabled = DiscordProfile.IsChecked == true;
        _settings.RobloxEnabled = RobloxProfile.IsChecked == true;
        _settings.FortniteEnabled = FortniteProfile.IsChecked == true;

        _settingsService.Save(_settings);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            SaveSettings();

            // Abort any in-flight enable flow FIRST: StopImmediate closes the
            // job handle, so a fallback round that started afterwards would run
            // an elevated engine with no kill-on-job-close guarantee.
            _enableCts?.Cancel();

            // Synchronous teardown ONLY. This handler runs on the WPF dispatcher
            // thread; blocking it on DisposeAsync deadlocks, because the awaited
            // continuation is posted to the very message queue we would be
            // blocking. That hung the app on close and left an elevated winws
            // filtering traffic with no way to stop it.
            _engine.StopImmediate();

            _logger.Info(stage: "Application", result: "Stopped");
        }
        catch (Exception ex)
        {
            _logger.Error(stage: "ApplicationClosing", exception: ex);
        }
    }

    // ---- UI helpers ------------------------------------------------------

    private void SetStatus(bool active, string title, string description)
    {
        StatusIndicator.Fill = new SolidColorBrush(active ? ActiveColor : InactiveColor);
        StatusText.Text = title;
        StatusDescription.Text = description;
    }

    private void UpdateScenarioLabelsFromProtectionResults(
        IReadOnlyList<AppProtectionResult> results)
    {
        YouTubeScenarioText.Text = FindProtectionText(results, "youtube");
        DiscordScenarioText.Text = FindProtectionText(results, "discord");
        RobloxScenarioText.Text = FindProtectionText(results, "roblox");
        FortniteScenarioText.Text = FindProtectionText(results, "fortnite");
    }

    private static string FindProtectionText(
        IReadOnlyList<AppProtectionResult> results,
        string appId)
    {
        AppProtectionResult? result =
            results.FirstOrDefault(item => item.AppId == appId);

        return result?.Outcome switch
        {
            AppProtectionOutcome.Skipped => "Уже доступно напрямую",
            AppProtectionOutcome.Active =>
                $"Активно · стратегия {result.StrategyIndex + 1}/{result.StrategyCount}",
            AppProtectionOutcome.BestEffortFailed => "Best effort · не подтверждено",
            _ => "Не выбрано"
        };
    }

    private void ResetScenarioLabels()
    {
        const string defaultText = "Будет определён автоматически";

        YouTubeScenarioText.Text = defaultText;
        DiscordScenarioText.Text = defaultText;
        RobloxScenarioText.Text = defaultText;
        FortniteScenarioText.Text = defaultText;
    }

    private List<string> GetCheckedAppIds()
    {
        var appIds = new List<string>();

        if (YouTubeProfile.IsChecked == true) appIds.Add("youtube");
        if (DiscordProfile.IsChecked == true) appIds.Add("discord");
        if (RobloxProfile.IsChecked == true) appIds.Add("roblox");
        if (FortniteProfile.IsChecked == true) appIds.Add("fortnite");

        return appIds;
    }

    private List<DiagnosticsTarget> BuildDiagnosticsTargets()
    {
        var targets = new List<DiagnosticsTarget>();

        foreach (string appId in GetCheckedAppIds())
        {
            targets.AddRange(DiagnosticsTargetCatalog.GetTargetsForApp(appId));
        }

        return targets;
    }
}
