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

    private readonly List<DiagnosticsResult> _lastDiagnosticsResults = new();
    private readonly List<ScenarioDecision> _lastScenarioDecisions = new();

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

            if (!_protectionEnabled)
            {
                await RefreshEngineStartAvailabilityAsync();
            }
        }
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
                    "Служба хранит активное состояние. Внешний сетевой движок пока не подключён";

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
                ToggleProtectionButton.IsEnabled = false;

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
                ToggleProtectionButton.IsEnabled = false;

                StatusIndicator.Fill =
                    new SolidColorBrush(
                        Color.FromRgb(140, 90, 77));

                StatusText.Text =
                    "Ошибка профиля";

                StatusDescription.Text =
                    "Активный профиль не прошёл проверку";

                EventText.Text =
                    "Служба использует fallback. Подробности доступны по F9";

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

            EventText.Text =
                $"Запущенных известных процессов: {runningApps}. Проверяем соединения";

            List<DiagnosticsTarget> targets =
                BuildDiagnosticsTargets();

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

            IReadOnlyList<ScenarioDecision> decisions =
                _scenarioPlanner.BuildDecisions(results);

            _lastScenarioDecisions.Clear();
            _lastScenarioDecisions.AddRange(decisions);

            UpdateScenarioLabels(decisions);

            int dpiCandidates =
                decisions.Count(decision =>
                    decision.RecommendedMode.Contains(
                        "DPI",
                        StringComparison.OrdinalIgnoreCase));

            int proxyCandidates =
                decisions.Count(decision =>
                    decision.RecommendedMode.Contains(
                        "Proxy",
                        StringComparison.OrdinalIgnoreCase));

            EventText.Text =
                "Передаём службе команду запуска";

            string engineResponse =
                await _pipeClient.StartEngineAsync();

            if (engineResponse.Equals(
                    "ENGINE_BLOCKED_PROFILE_REQUIRES_APPROVAL",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Профиль требует явного разрешения на запуск. Запуск заблокирован службой.");
            }

            bool engineAccepted =
                engineResponse.Equals(
                    "ENGINE_STARTED",
                    StringComparison.OrdinalIgnoreCase) ||
                engineResponse.Equals(
                    "ENGINE_ALREADY_RUNNING",
                    StringComparison.OrdinalIgnoreCase);

            if (!engineAccepted)
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
                "Сценарии рассчитаны и состояние службы включено. Внешний сетевой движок пока не подключён";

            ToggleProtectionButton.Content =
                "Выключить защиту";

            EventText.Text =
                $"Готово: сценариев {decisions.Count}, DPI-кандидатов {dpiCandidates}, proxy fallback {proxyCandidates}";

            _logger.Info(
                stage: "ProtectionAnalysis",
                result:
                    $"Completed; engineResponse={engineResponse}; scenarios={decisions.Count}; dpi={dpiCandidates}; proxy={proxyCandidates}");
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
                ? "Состояние службы включено. Внешний сетевой движок пока не подключён"
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
        AppSettings settings =
            _settingsService.Load();

        YouTubeProfile.IsChecked =
            settings.YouTubeEnabled;

        DiscordProfile.IsChecked =
            settings.DiscordEnabled;

        RobloxProfile.IsChecked =
            settings.RobloxEnabled;

        FortniteProfile.IsChecked =
            settings.FortniteEnabled;

        EventText.Text =
            "Готово к настройке";

        _logger.Info(
            stage: "Settings",
            result:
                $"Loaded; schema={settings.SchemaVersion}");
    }

    private void SaveSettings()
    {
        var settings = new AppSettings
        {
            YouTubeEnabled =
                YouTubeProfile.IsChecked == true,

            DiscordEnabled =
                DiscordProfile.IsChecked == true,

            RobloxEnabled =
                RobloxProfile.IsChecked == true,

            FortniteEnabled =
                FortniteProfile.IsChecked == true
        };

        _settingsService.Save(settings);

        _logger.Info(
            stage: "Settings",
            result:
                $"Saved; YouTube={settings.YouTubeEnabled}; Discord={settings.DiscordEnabled}; Roblox={settings.RobloxEnabled}; Fortnite={settings.FortniteEnabled}");
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

            string allowUnsafeStart =
                GetJsonValue(root, "AllowUnsafeStart");

            string stopTimeoutSeconds =
                GetJsonValue(root, "StopTimeoutSeconds");

            string killTimeoutSeconds =
                GetJsonValue(root, "KillTimeoutSeconds");

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

            report.AppendLine("AllowUnsafeStart:");
            report.AppendLine(allowUnsafeStart);
            report.AppendLine();

            report.AppendLine("StopTimeoutSeconds:");
            report.AppendLine(stopTimeoutSeconds);
            report.AppendLine();

            report.AppendLine("KillTimeoutSeconds:");
            report.AppendLine(killTimeoutSeconds);

            EventText.Text =
                "Конфигурация движка получена";

            _logger.Info(
                stage: "EngineConfig",
                result:
                    $"Loaded; profileId={profileId}; source={configurationSource}; warning={configurationWarning}; mode={mode}; executable={executable}; requiresAdmin={requiresAdmin}; usesWinDivert={usesWinDivert}; allowUnsafeStart={allowUnsafeStart}");

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























