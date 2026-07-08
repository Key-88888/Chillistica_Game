using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using Chillistica_game.App.Services;

namespace Chillistica_game.App;

public partial class MainWindow : Window
{
    private readonly DiagnosticsService _diagnosticsService = new();
    private readonly ScenarioPlanner _scenarioPlanner = new();
    private readonly ProcessDetectionService _processDetectionService = new();
    private readonly SettingsService _settingsService = new();
    private readonly AppLogger _logger = new();
    private readonly NamedPipeClientService _pipeClient = new();
    private readonly UpdateCheckService _updateCheckService = new();

    private UpdateCheckResult? _pendingUpdate;
    private bool _updateInProgress;

    private readonly List<DiagnosticsResult> _lastDiagnosticsResults = new();

    private AppSettings _settings = new();

    private bool _protectionEnabled;
    private bool _diagnosticsRunning;

    public MainWindow()
    {
        InitializeComponent();

        _logger.Info(
            stage: "Application",
            result: "Started");

        LoadSettings();
        AttachProfileChangeHandlers();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        await CheckServiceAvailabilityAsync();

        bool protocolCompatible =
            await CheckProtocolCompatibilityAsync();

        if (protocolCompatible)
        {
            await SynchronizeEngineStatusAsync();

            // Always re-checked, even if ENGINE_STATUS already reported
            // ENGINE_RUNNING: a config-invalid/blocked profile must never
            // be allowed to keep showing a green "protection enabled" state.
            await RefreshEngineStartAvailabilityAsync();
        }

        _ = CheckForUpdateInBackgroundAsync();
    }

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

            UpdateBannerText.Text =
                $"Доступно обновление {update.TagName}";

            UpdateBanner.Visibility =
                Visibility.Visible;

            _logger.Info(
                stage: "UpdateCheck",
                result: $"UpdateAvailable; tag={update.TagName}");
        }
        catch (Exception exception)
        {
            _logger.Error(
                stage: "UpdateCheck",
                exception: exception);
        }
    }

    private async void UpdateNowButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_updateInProgress ||
            _pendingUpdate is null)
        {
            return;
        }

        _updateInProgress = true;
        UpdateNowButton.IsEnabled = false;
        UpdateBannerText.Text =
            $"Скачиваем обновление {_pendingUpdate.TagName}...";

        try
        {
            string stagingFolder =
                await _updateCheckService.DownloadAndStageUpdateAsync(
                    _pendingUpdate.DownloadUrl);

            _logger.Info(
                stage: "Update",
                result: $"Downloaded; tag={_pendingUpdate.TagName}; staging={stagingFolder}");

            _updateCheckService.LaunchElevatedApplyUpdate(
                stagingFolder);

            _logger.Info(
                stage: "Update",
                result: "ElevatedApplyLaunched");

            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            _logger.Error(
                stage: "Update",
                exception: exception);

            UpdateBannerText.Text =
                "Не удалось скачать обновление";

            MessageBox.Show(
                $"Не удалось применить обновление.\n\n{exception.Message}",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            _updateInProgress = false;
            UpdateNowButton.IsEnabled = true;
        }
    }

    private void DismissUpdateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateBanner.Visibility =
            Visibility.Collapsed;
    }

    private async Task CheckServiceAvailabilityAsync()
    {
        bool serviceAvailable =
            await _pipeClient.IsServiceAvailableAsync();

        if (serviceAvailable)
        {
            EventText.Text =
                "Служба управления доступна";

            _logger.Info(
                stage: "ServiceConnection",
                result: "Available");
        }
        else
        {
            EventText.Text =
                "Служба управления пока не запущена";

            _logger.Info(
                stage: "ServiceConnection",
                result: "Unavailable");
        }
    }

    private async Task<bool> CheckProtocolCompatibilityAsync()
    {
        string protocolVersion =
            await _pipeClient.GetProtocolVersionAsync();

        bool compatible =
            protocolVersion.Equals(
                NamedPipeClientService.SupportedProtocolVersion,
                StringComparison.OrdinalIgnoreCase);

        if (compatible)
        {
            _logger.Info(
                stage: "ProtocolVersion",
                result: $"Compatible; version={protocolVersion}");

            return true;
        }

        _protectionEnabled = false;

        StatusIndicator.Fill =
            new SolidColorBrush(
                Color.FromRgb(140, 90, 77));

        StatusText.Text =
            "Требуется обновление";

        StatusDescription.Text =
            "Версии приложения и службы несовместимы";

        ToggleProtectionButton.Content =
            "Включить защиту";

        ToggleProtectionButton.IsEnabled = false;

        EventText.Text =
            $"Версия IPC службы: {protocolVersion}; ожидается: {NamedPipeClientService.SupportedProtocolVersion}";

        _logger.Info(
            stage: "ProtocolVersion",
            result:
                $"Incompatible; service={protocolVersion}; expected={NamedPipeClientService.SupportedProtocolVersion}");

        return false;
    }

    private async Task SynchronizeEngineStatusAsync()
    {
        string engineStatus =
            await _pipeClient.GetEngineStatusAsync();

        switch (engineStatus.ToUpperInvariant())
        {
            case "ENGINE_RUNNING":
                _protectionEnabled = true;

                StatusIndicator.Fill =
                    new SolidColorBrush(
                        Color.FromRgb(59, 106, 82));

                StatusText.Text =
                    "Управление включено";

                StatusDescription.Text =
                    "Обход активен для выбранных приложений";

                ToggleProtectionButton.Content =
                    "Выключить защиту";

                EventText.Text =
                    "Состояние службы синхронизировано";

                break;

            case "ENGINE_STOPPED":
                _protectionEnabled = false;

                StatusIndicator.Fill =
                    new SolidColorBrush(
                        Color.FromRgb(140, 90, 77));

                StatusText.Text =
                    "Защита выключена";

                StatusDescription.Text =
                    "Сетевой движок пока не запущен";

                ToggleProtectionButton.Content =
                    "Включить защиту";

                break;

            default:
                _protectionEnabled = false;

                EventText.Text =
                    $"Не удалось получить состояние движка: {engineStatus}";

                break;
        }

        _logger.Info(
            stage: "EngineStatus",
            result: engineStatus);
    }

    private async Task<bool> RefreshEngineStartAvailabilityAsync()
    {
        string response =
            await _pipeClient.GetEngineCanStartAsync();

        switch (response.ToUpperInvariant())
        {
            case "ENGINE_CAN_START":
                ToggleProtectionButton.IsEnabled = true;

                _logger.Info(
                    stage: "EngineCanStart",
                    result: response);

                return true;

            case "ENGINE_BLOCKED_PROFILE_REQUIRES_APPROVAL":
                _protectionEnabled = false;

                ToggleProtectionButton.IsEnabled = false;

                ToggleProtectionButton.Content =
                    "Включить защиту";

                StatusIndicator.Fill =
                    new SolidColorBrush(
                        Color.FromRgb(140, 90, 77));

                StatusText.Text =
                    "Запуск заблокирован";

                StatusDescription.Text =
                    "Активный профиль требует явного разрешения";

                EventText.Text =
                    "Профиль использует административные возможности или WinDivert";

                _logger.Info(
                    stage: "EngineCanStart",
                    result: response);

                return false;

            case "ENGINE_CONFIG_INVALID":
                _protectionEnabled = false;

                ToggleProtectionButton.IsEnabled = false;

                ToggleProtectionButton.Content =
                    "Включить защиту";

                StatusIndicator.Fill =
                    new SolidColorBrush(
                        Color.FromRgb(140, 90, 77));

                StatusText.Text =
                    "Профиль недействителен";

                StatusDescription.Text =
                    "Движок не запускался. Активный профиль не прошёл проверку";

                EventText.Text =
                    "Проверка START_ENGINE будет отклонена службой. Подробности доступны по F9";

                _logger.Info(
                    stage: "EngineCanStart",
                    result: response);

                return false;

            default:
                ToggleProtectionButton.IsEnabled = false;

                StatusIndicator.Fill =
                    new SolidColorBrush(
                        Color.FromRgb(140, 90, 77));

                StatusText.Text =
                    "Служба недоступна";

                StatusDescription.Text =
                    "Не удалось проверить возможность запуска";

                EventText.Text =
                    $"ENGINE_CAN_START: {response}";

                _logger.Info(
                    stage: "EngineCanStart",
                    result: response);

                return false;
        }
    }
    private async Task CheckServiceStatusAsync()
    {
        string status =
            await _pipeClient.GetServiceStatusAsync();

        if (status.Equals(
                "SERVICE_UNAVAILABLE",
                StringComparison.OrdinalIgnoreCase))
        {
            EventText.Text =
                "Служба управления недоступна";

            _logger.Info(
                stage: "ServiceStatus",
                result: "Unavailable");

            return;
        }

        EventText.Text =
            $"Статус службы: {status}";

        _logger.Info(
            stage: "ServiceStatus",
            result: status);
    }

    private async void ToggleProtectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_protectionEnabled)
        {
            await DisableProtectionAsync();
            return;
        }

        ToggleProtectionButton.IsEnabled = false;

        EventText.Text =
            "Проверяем службу управления";

        try
        {
            string serviceStatus =
                await _pipeClient.GetServiceStatusAsync();

            if (!serviceStatus.Equals(
                    "SERVICE_RUNNING",
                    StringComparison.OrdinalIgnoreCase))
            {
                StatusIndicator.Fill =
                    new SolidColorBrush(
                        Color.FromRgb(140, 90, 77));

                StatusText.Text =
                    "Служба недоступна";

                StatusDescription.Text =
                    "Сначала должна быть запущена служба управления";

                EventText.Text =
                    "Не удалось подключиться к службе Chillistica_game";

                _logger.Info(
                    stage: "ProtectionRequest",
                    result:
                        $"Rejected; serviceStatus={serviceStatus}");

                return;
            }

            bool engineCanStart =
                await RefreshEngineStartAvailabilityAsync();

            if (!engineCanStart)
            {
                _logger.Info(
                    stage: "ProtectionRequest",
                    result: "Rejected; ENGINE_CAN_START denied");

                return;
            }

            _logger.Info(
                stage: "ProtectionRequest",
                result: "Accepted; serviceStatus=SERVICE_RUNNING; engineCanStart=true");

            await EnableProtectionAnalysisAsync();
        }
        catch (Exception ex)
        {
            StatusIndicator.Fill =
                new SolidColorBrush(
                    Color.FromRgb(140, 90, 77));

            StatusText.Text =
                "Ошибка подключения";

            StatusDescription.Text =
                "Не удалось проверить службу управления";

            EventText.Text =
                $"Ошибка службы: {ex.Message}";

            _logger.Error(
                stage: "ProtectionRequest",
                exception: ex);
        }
        finally
        {
            if (!_diagnosticsRunning)
            {
                ToggleProtectionButton.IsEnabled = true;
            }
        }
    }

    private async Task EnableProtectionAnalysisAsync()
    {
        if (_diagnosticsRunning)
        {
            return;
        }

        _diagnosticsRunning = true;

        _logger.Info(
            stage: "ProtectionAnalysis",
            result: "Started");

        ToggleProtectionButton.IsEnabled = false;

        StatusIndicator.Fill =
            new SolidColorBrush(
                Color.FromRgb(59, 106, 82));

        StatusText.Text = "Идёт настройка";

        StatusDescription.Text =
            "Проверяем приложения, соединение и подходящий сценарий";

        ToggleProtectionButton.Content =
            "Настройка...";

        try
        {
            EventText.Text =
                "Проверяем запущенные приложения";

            IReadOnlyList<AppProcessStatus> processStatuses =
                _processDetectionService.GetStatuses();

            int runningApps =
                processStatuses.Count(status => status.IsRunning);

            List<string> checkedAppIds =
                GetCheckedAppIds();

            if (checkedAppIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Выбери хотя бы один профиль приложения.");
            }

            EventText.Text =
                $"Запущенных известных процессов: {runningApps}. Проверяем соединения и включаем обход";

            var orchestrator =
                new StrategyOrchestrator(
                    _pipeClient,
                    _diagnosticsService);

            (bool engineStarted, string engineResponse, IReadOnlyList<AppProtectionResult> appResults) =
                await orchestrator.EnableAsync(
                    checkedAppIds,
                    _settings.LastGoodStrategyIndex);

            _settingsService.Save(
                _settings);

            UpdateScenarioLabelsFromProtectionResults(
                appResults);

            if (engineResponse.Equals(
                    "ALL_DIRECT_NO_BYPASS_NEEDED",
                    StringComparison.OrdinalIgnoreCase))
            {
                _protectionEnabled = false;

                StatusText.Text =
                    "Защита не нужна";

                StatusDescription.Text =
                    "Все выбранные сервисы уже доступны напрямую";

                ToggleProtectionButton.Content =
                    "Включить защиту";

                EventText.Text =
                    "Обход не применялся: прямое соединение уже работает";

                _logger.Info(
                    stage: "ProtectionAnalysis",
                    result: "Completed; allDirectNoBypassNeeded=true");

                return;
            }

            if (!engineStarted)
            {
                throw new InvalidOperationException(
                    $"Служба отклонила запуск: {engineResponse}");
            }

            string startHealth =
                await _pipeClient.GetEngineHealthAsync();

            _logger.Info(
                stage: "EngineHealth",
                result: $"AfterStart; health={startHealth}");

            if (!EngineHealthConfirmsRunning(startHealth))
            {
                string rollbackResponse =
                    await _pipeClient.StopEngineAsync();

                string rollbackHealth =
                    await _pipeClient.GetEngineHealthAsync();

                _logger.Error(
                    stage: "EngineHealth",
                    exception: new InvalidOperationException(
                        $"Start verification failed; health={startHealth}; rollbackResponse={rollbackResponse}; rollbackHealth={rollbackHealth}"));

                throw new InvalidOperationException(
                    "Движок не подтвердил запуск через ENGINE_HEALTH. " +
                    $"Ответ: {startHealth}. " +
                    $"Откат: {rollbackResponse}; {rollbackHealth}");
            }

            _protectionEnabled = true;

            StatusText.Text =
                "Управление включено";

            StatusDescription.Text =
                "Обход активен для выбранных приложений";

            ToggleProtectionButton.Content =
                "Выключить защиту";

            int activeCount =
                appResults.Count(result => result.Outcome == AppProtectionOutcome.Active);

            int skippedCount =
                appResults.Count(result => result.Outcome == AppProtectionOutcome.Skipped);

            int bestEffortCount =
                appResults.Count(result => result.Outcome == AppProtectionOutcome.BestEffortFailed);

            EventText.Text =
                $"Готово: активно {activeCount}, уже доступно напрямую {skippedCount}, best-effort {bestEffortCount}";

            _logger.Info(
                stage: "ProtectionAnalysis",
                result:
                    $"Completed; engineResponse={engineResponse}; active={activeCount}; skipped={skippedCount}; bestEffort={bestEffortCount}");
        }
        catch (Exception ex)
        {
            _logger.Error(
                stage: "ProtectionAnalysis",
                exception: ex);

            _protectionEnabled = false;

            StatusIndicator.Fill =
                new SolidColorBrush(
                    Color.FromRgb(140, 90, 77));

            StatusText.Text =
                "Ошибка настройки";

            StatusDescription.Text =
                "Автоматическая проверка не завершилась";

            ToggleProtectionButton.Content =
                "Включить защиту";

            EventText.Text =
                $"Ошибка: {ex.Message}";

            MessageBox.Show(
                $"Не удалось выполнить автоматическую настройку.\n\n{ex.Message}",
                "Chillistica_game",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _diagnosticsRunning = false;
            ToggleProtectionButton.IsEnabled = true;
        }
    }

    private async Task DisableProtectionAsync()
    {
        ToggleProtectionButton.IsEnabled = false;

        EventText.Text =
            "Останавливаем состояние службы";

        try
        {
            string engineResponse =
                await _pipeClient.StopEngineAsync();

            bool engineStopped =
                engineResponse.Equals(
                    "ENGINE_STOPPED",
                    StringComparison.OrdinalIgnoreCase) ||
                engineResponse.Equals(
                    "ENGINE_ALREADY_STOPPED",
                    StringComparison.OrdinalIgnoreCase);

            if (!engineStopped)
            {
                throw new InvalidOperationException(
                    $"Служба отклонила остановку: {engineResponse}");
            }

            string stopHealth =
                await _pipeClient.GetEngineHealthAsync();

            _logger.Info(
                stage: "EngineHealth",
                result: $"AfterStop; health={stopHealth}");

            if (!EngineHealthConfirmsStopped(stopHealth))
            {
                string retryStopResponse =
                    await _pipeClient.StopEngineAsync();

                string retryStopHealth =
                    await _pipeClient.GetEngineHealthAsync();

                _logger.Error(
                    stage: "EngineHealth",
                    exception: new InvalidOperationException(
                        $"Stop verification failed; health={stopHealth}; retryResponse={retryStopResponse}; retryHealth={retryStopHealth}"));

                if (!EngineHealthConfirmsStopped(retryStopHealth))
                {
                    throw new InvalidOperationException(
                        "Движок не подтвердил остановку через ENGINE_HEALTH. " +
                        $"Первый ответ: {stopHealth}. " +
                        $"Повторная остановка: {retryStopResponse}; {retryStopHealth}");
                }
            }

            _protectionEnabled = false;

            StatusIndicator.Fill =
                new SolidColorBrush(
                    Color.FromRgb(140, 90, 77));

            StatusText.Text =
                "Защита выключена";

            StatusDescription.Text =
                "Сетевой движок пока не запущен";

            ToggleProtectionButton.Content =
                "Включить защиту";

            EventText.Text =
                "Состояние службы выключено";

            ResetScenarioLabels();

            _logger.Info(
                stage: "ProtectionStop",
                result: engineResponse);
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Ошибка выключения";

            StatusDescription.Text =
                "Не удалось подтвердить остановку через службу";

            EventText.Text =
                $"Ошибка службы: {ex.Message}";

            _logger.Error(
                stage: "ProtectionStop",
                exception: ex);
        }
        finally
        {
            ToggleProtectionButton.IsEnabled = true;
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

        _logger.Info(
            stage: "Diagnostics",
            result: $"Started; targets={targets.Count}");

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

            int successfulResults =
                results.Count(result =>
                    result.IsSuccessful);

            int failedResults =
                results.Count -
                successfulResults;

            _logger.Info(
                stage: "Diagnostics",
                result:
                    $"Completed; successful={successfulResults}; failed={failedResults}");

            ShowDiagnosticsResults(results);
        }
        catch (Exception ex)
        {
            _logger.Error(
                stage: "Diagnostics",
                exception: ex);

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
                ? "Обход активен для выбранных приложений"
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

        EventText.Text =
            $"Автоподбор: создано сценариев {decisions.Count}";

        var scenarioWindow =
            new ScenarioWindow(decisions)
            {
                Owner = this
            };

        scenarioWindow.ShowDialog();
    }

    private void DetectProcessesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        IReadOnlyList<AppProcessStatus> statuses =
            _processDetectionService.GetStatuses();

        StringBuilder report = new();

        foreach (AppProcessStatus status in statuses)
        {
            report.AppendLine(
                $"{status.AppName}: {status.StatusText}");

            report.AppendLine(
                $"Процессы: {status.RunningProcessesText}");

            report.AppendLine();
        }

        EventText.Text =
            "Проверка запущенных приложений завершена";

        MessageBox.Show(
            report.ToString().Trim(),
            "Запущенные приложения",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void AttachProfileChangeHandlers()
    {
        YouTubeProfile.Checked +=
            ProfileSelection_Changed;

        YouTubeProfile.Unchecked +=
            ProfileSelection_Changed;

        DiscordProfile.Checked +=
            ProfileSelection_Changed;

        DiscordProfile.Unchecked +=
            ProfileSelection_Changed;

        RobloxProfile.Checked +=
            ProfileSelection_Changed;

        RobloxProfile.Unchecked +=
            ProfileSelection_Changed;

        FortniteProfile.Checked +=
            ProfileSelection_Changed;

        FortniteProfile.Unchecked +=
            ProfileSelection_Changed;
    }

    private void ProfileSelection_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (
            sender is not
            System.Windows.Controls.CheckBox profile)
        {
            return;
        }

        string appName =
            profile.Name switch
            {
                "YouTubeProfile" =>
                    "YouTube",

                "DiscordProfile" =>
                    "Discord",

                "RobloxProfile" =>
                    "Roblox",

                "FortniteProfile" =>
                    "Fortnite",

                _ =>
                    profile.Name
            };

        string state =
            profile.IsChecked == true
                ? "Enabled"
                : "Disabled";

        _logger.Info(
            stage: "ProfileChanged",
            app: appName,
            result: state);
    }

    private void LoadSettings()
    {
        _settings =
            _settingsService.Load();

        YouTubeProfile.IsChecked =
            _settings.YouTubeEnabled;

        DiscordProfile.IsChecked =
            _settings.DiscordEnabled;

        RobloxProfile.IsChecked =
            _settings.RobloxEnabled;

        FortniteProfile.IsChecked =
            _settings.FortniteEnabled;

        EventText.Text =
            "Готово к настройке";

        _logger.Info(
            stage: "Settings",
            result:
                $"Loaded; schema={_settings.SchemaVersion}");
    }

    private void SaveSettings()
    {
        _settings.YouTubeEnabled =
            YouTubeProfile.IsChecked == true;

        _settings.DiscordEnabled =
            DiscordProfile.IsChecked == true;

        _settings.RobloxEnabled =
            RobloxProfile.IsChecked == true;

        _settings.FortniteEnabled =
            FortniteProfile.IsChecked == true;

        _settingsService.Save(_settings);

        _logger.Info(
            stage: "Settings",
            result:
                $"Saved; YouTube={_settings.YouTubeEnabled}; Discord={_settings.DiscordEnabled}; Roblox={_settings.RobloxEnabled}; Fortnite={_settings.FortniteEnabled}");
    }

    private void MainWindow_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            SaveSettings();

            _logger.Info(
                stage: "Application",
                result: "Stopped");
        }
        catch (Exception ex)
        {
            _logger.Error(
                stage: "ApplicationClosing",
                exception: ex);
        }
    }

    private void UpdateScenarioLabels(
        IReadOnlyCollection<ScenarioDecision> decisions)
    {
        YouTubeScenarioText.Text =
            FindScenarioText(decisions, "YouTube");

        DiscordScenarioText.Text =
            FindScenarioText(decisions, "Discord");

        RobloxScenarioText.Text =
            FindScenarioText(decisions, "Roblox");

        FortniteScenarioText.Text =
            FindScenarioText(decisions, "Fortnite / Epic");
    }

    private static string FindScenarioText(
        IReadOnlyCollection<ScenarioDecision> decisions,
        string appName)
    {
        ScenarioDecision? decision =
            decisions.FirstOrDefault(item =>
                item.AppName == appName);

        if (decision is null)
        {
            return "Нет данных";
        }

        string mode = decision.RecommendedMode;

        if (mode.Equals(
                "Direct",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Напрямую · обход не требуется";
        }

        if (mode.Contains(
                "DPI",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Кандидат на DPI bypass";
        }

        if (mode.Contains(
                "Proxy",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Резервный маршрут через прокси";
        }

        if (mode.Contains(
                "Mixed",
                StringComparison.OrdinalIgnoreCase) ||
            mode.Contains(
                "Game profile",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Выборочный профиль";
        }

        return "Требуется дополнительная проверка";
    }

    private void UpdateScenarioLabelsFromProtectionResults(
        IReadOnlyList<AppProtectionResult> results)
    {
        YouTubeScenarioText.Text =
            FindProtectionText(results, "youtube");

        DiscordScenarioText.Text =
            FindProtectionText(results, "discord");

        RobloxScenarioText.Text =
            FindProtectionText(results, "roblox");

        FortniteScenarioText.Text =
            FindProtectionText(results, "fortnite");
    }

    private static string FindProtectionText(
        IReadOnlyList<AppProtectionResult> results,
        string appId)
    {
        AppProtectionResult? result =
            results.FirstOrDefault(item =>
                item.AppId == appId);

        if (result is null)
        {
            return "Не выбрано";
        }

        return result.Outcome switch
        {
            AppProtectionOutcome.Skipped =>
                "Уже доступно напрямую",

            AppProtectionOutcome.Active =>
                $"Активно · стратегия {result.StrategyIndex + 1}/{result.StrategyCount}",

            AppProtectionOutcome.BestEffortFailed =>
                "Best effort · не подтверждено",

            _ =>
                "Неизвестно"
        };
    }

    private void ResetScenarioLabels()
    {
        const string defaultText =
            "Будет определён автоматически";

        YouTubeScenarioText.Text = defaultText;
        DiscordScenarioText.Text = defaultText;
        RobloxScenarioText.Text = defaultText;
        FortniteScenarioText.Text = defaultText;
    }

    private async Task ShowEngineConfigAsync()
    {
        string configJson =
            await _pipeClient.GetEngineConfigJsonAsync();

        if (configJson.Equals(
                "ENGINE_CONFIG_JSON_UNAVAILABLE",
                StringComparison.OrdinalIgnoreCase))
        {
            EventText.Text =
                "Конфигурация движка недоступна";

            _logger.Info(
                stage: "EngineConfig",
                result: "Unavailable");

            MessageBox.Show(
                "Не удалось получить конфигурацию движка.\n\nСлужба управления недоступна или не ответила на ENGINE_CONFIG_JSON.",
                "Конфигурация движка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(configJson);

            JsonElement root =
                document.RootElement;

            string profileId =
                GetJsonString(root, "ProfileId");

            string displayName =
                GetJsonString(root, "DisplayName");

            string configurationSource =
                GetJsonString(root, "ConfigurationSource");

            string configurationWarning =
                GetJsonString(root, "ConfigurationWarning");

            string mode =
                GetJsonString(root, "Mode");

            string executable =
                GetJsonString(root, "Executable");

            string arguments =
                GetJsonString(root, "Arguments");

            string workingDirectory =
                GetJsonString(root, "WorkingDirectory");

            string requiresAdmin =
                GetJsonValue(root, "RequiresAdmin");

            string usesWinDivert =
                GetJsonValue(root, "UsesWinDivert");

            string engineTrusted =
                GetJsonValue(root, "EngineTrusted");

            string stopTimeoutSeconds =
                GetJsonValue(root, "StopTimeoutSeconds");

            string killTimeoutSeconds =
                GetJsonValue(root, "KillTimeoutSeconds");

            string fileHashesCount =
                GetJsonValue(root, "FileHashesCount");

            StringBuilder report = new();

            report.AppendLine("ProfileId:");
            report.AppendLine(profileId);
            report.AppendLine();

            report.AppendLine("DisplayName:");
            report.AppendLine(displayName);
            report.AppendLine();

            report.AppendLine("ConfigurationSource:");
            report.AppendLine(configurationSource);
            report.AppendLine();

            report.AppendLine("ConfigurationWarning:");
            report.AppendLine(string.IsNullOrWhiteSpace(configurationWarning) ? "<none>" : configurationWarning);
            report.AppendLine();

            report.AppendLine("Mode:");
            report.AppendLine(mode);
            report.AppendLine();

            report.AppendLine("Executable:");
            report.AppendLine(executable);
            report.AppendLine();

            report.AppendLine("Arguments:");
            report.AppendLine(arguments);
            report.AppendLine();

            report.AppendLine("WorkingDirectory:");
            report.AppendLine(workingDirectory);
            report.AppendLine();

            report.AppendLine("RequiresAdmin:");
            report.AppendLine(requiresAdmin);
            report.AppendLine();

            report.AppendLine("UsesWinDivert:");
            report.AppendLine(usesWinDivert);
            report.AppendLine();

            report.AppendLine("EngineTrusted:");
            report.AppendLine(engineTrusted);
            report.AppendLine();

            report.AppendLine("StopTimeoutSeconds:");
            report.AppendLine(stopTimeoutSeconds);
            report.AppendLine();

            report.AppendLine("KillTimeoutSeconds:");
            report.AppendLine(killTimeoutSeconds);
            report.AppendLine();

            report.AppendLine("FileHashesCount:");
            report.AppendLine(fileHashesCount);

            EventText.Text =
                "Конфигурация движка получена";

            _logger.Info(
                stage: "EngineConfig",
                result:
                    $"Loaded; profileId={profileId}; source={configurationSource}; warning={configurationWarning}; mode={mode}; executable={executable}; requiresAdmin={requiresAdmin}; usesWinDivert={usesWinDivert}; engineTrusted={engineTrusted}; fileHashesCount={fileHashesCount}");

            MessageBox.Show(
                report.ToString(),
                "Конфигурация движка",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (JsonException ex)
        {
            EventText.Text =
                "Служба вернула некорректный JSON конфигурации";

            _logger.Error(
                stage: "EngineConfig",
                exception: ex);

            MessageBox.Show(
                $"Служба вернула некорректный JSON конфигурации.\n\nОтвет:\n{configJson}\n\nОшибка:\n{ex.Message}",
                "Конфигурация движка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ShowEngineHealthAsync()
    {
        try
        {
            string health =
                await _pipeClient.GetEngineHealthAsync();

            EventText.Text =
                $"ENGINE_HEALTH: {health}";

            _logger.Info(
                stage: "EngineHealth",
                result: health);

            MessageBox.Show(
                health,
                "ENGINE_HEALTH",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            EventText.Text =
                $"ENGINE_HEALTH error: {ex.Message}";

            _logger.Error(
                stage: "EngineHealth",
                exception: ex);

            MessageBox.Show(
                "Не удалось получить ENGINE_HEALTH:" +
                Environment.NewLine +
                Environment.NewLine +
                ex.Message,
                "ENGINE_HEALTH",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    private async Task ShowEngineHashStatusAsync()
    {
        try
        {
            string hashStatus =
                await _pipeClient.GetEngineHashStatusAsync();

            EventText.Text =
                $"ENGINE_HASH_STATUS: {hashStatus}";

            _logger.Info(
                stage: "EngineHashStatus",
                result: hashStatus);

            MessageBoxImage icon =
                hashStatus.Contains(
                    "ENGINE_HASH_STATUS OK",
                    StringComparison.OrdinalIgnoreCase)
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning;

            MessageBox.Show(
                hashStatus,
                "ENGINE_HASH_STATUS",
                MessageBoxButton.OK,
                icon);
        }
        catch (Exception ex)
        {
            EventText.Text =
                $"ENGINE_HASH_STATUS error: {ex.Message}";

            _logger.Error(
                stage: "EngineHashStatus",
                exception: ex);

            MessageBox.Show(
                "Не удалось получить ENGINE_HASH_STATUS:" +
                Environment.NewLine +
                Environment.NewLine +
                ex.Message,
                "ENGINE_HASH_STATUS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    private static bool EngineHealthConfirmsRunning(string health)
    {
        return
            health.Contains(
                "ENGINE_HEALTH RUNNING",
                StringComparison.OrdinalIgnoreCase) &&
            TryGetEngineHealthPid(
                health,
                out int pid) &&
            pid > 0;
    }

    private static bool EngineHealthConfirmsStopped(string health)
    {
        return
            health.Contains(
                "ENGINE_HEALTH STOPPED",
                StringComparison.OrdinalIgnoreCase) &&
            TryGetEngineHealthPid(
                health,
                out int pid) &&
            pid == 0;
    }

    private static bool TryGetEngineHealthPid(
        string health,
        out int pid)
    {
        pid = 0;

        string[] parts =
            health.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        foreach (string part in parts)
        {
            if (!part.StartsWith(
                    "PID=",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value =
                part.Substring("PID=".Length);

            return int.TryParse(
                value,
                out pid);
        }

        return false;
    }
    private static string GetJsonString(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return "<missing>";
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return "<null>";
        }

        return value.GetString() ?? string.Empty;
    }

    private static string GetJsonValue(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return "<missing>";
        }

        return value.ToString();
    }

    private async void Window_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            DiagnosticsButton_Click(
                sender,
                new RoutedEventArgs());

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6)
        {
            AutoTuneButton_Click(
                sender,
                new RoutedEventArgs());

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F7)
        {
            DetectProcessesButton_Click(
                sender,
                new RoutedEventArgs());

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F8)
        {
            await CheckServiceStatusAsync();

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F9)
        {
            await ShowEngineConfigAsync();

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F10 || e.SystemKey == Key.F10)
        {
            await ShowEngineHealthAsync();

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F11 || e.SystemKey == Key.F11)
        {
            await ShowEngineHashStatusAsync();

            e.Handled = true;
            return;
        }
    }

    private List<string> GetCheckedAppIds()
    {
        List<string> appIds = new();

        if (YouTubeProfile.IsChecked == true)
        {
            appIds.Add("youtube");
        }

        if (DiscordProfile.IsChecked == true)
        {
            appIds.Add("discord");
        }

        if (RobloxProfile.IsChecked == true)
        {
            appIds.Add("roblox");
        }

        if (FortniteProfile.IsChecked == true)
        {
            appIds.Add("fortnite");
        }

        return appIds;
    }

    private List<DiagnosticsTarget> BuildDiagnosticsTargets()
    {
        List<DiagnosticsTarget> targets = new();

        foreach (string appId in GetCheckedAppIds())
        {
            targets.AddRange(
                DiagnosticsTargetCatalog.GetTargetsForApp(appId));
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


























