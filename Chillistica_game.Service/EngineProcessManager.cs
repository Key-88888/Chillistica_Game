using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Chillistica_game.Service;

public sealed class EngineProcessManager :
    IAsyncDisposable
{
    private readonly ServiceLogger _logger;
    private readonly EngineOptions _options;

    private readonly SemaphoreSlim _sync =
        new(initialCount: 1, maxCount: 1);

    private Process? _process;
    private bool _disposed;

    public EngineProcessManager(
        ServiceLogger logger,
        IOptions<EngineOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        ValidateOptions(
            _options);
    }

    public async Task<string> StartAsync(
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(
            cancellationToken);

        try
        {
            ThrowIfDisposed();
            CleanupExitedProcessUnsafe();

            if (IsProcessRunningUnsafe())
            {
                return "ENGINE_ALREADY_RUNNING";
            }

            string executablePath =
                ResolveExecutablePath(
                    _options.ExecutablePath);

            string workingDirectory =
                ResolveWorkingDirectory(
                    _options.WorkingDirectory);

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    "Engine executable was not found.",
                    executablePath);
            }

            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Engine working directory was not found: {workingDirectory}");
            }

            var startInfo =
                new ProcessStartInfo
                {
                    FileName =
                        executablePath,

                    Arguments =
                        _options.Arguments,

                    UseShellExecute =
                        false,

                    CreateNoWindow =
                        true,

                    RedirectStandardOutput =
                        true,

                    RedirectStandardError =
                        true,

                    WorkingDirectory =
                        workingDirectory
                };

            var process =
                new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

            process.OutputDataReceived +=
                Process_OutputDataReceived;

            process.ErrorDataReceived +=
                Process_ErrorDataReceived;

            process.Exited +=
                Process_Exited;

            if (!process.Start())
            {
                process.Dispose();

                throw new InvalidOperationException(
                    "Engine process did not start.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;

            _logger.Info(
                stage: "EngineProcess",
                result:
                    $"Started; mode={_options.Mode}; pid={process.Id}; file={executablePath}; arguments={_options.Arguments}; workingDirectory={workingDirectory}");

            return "ENGINE_STARTED";
        }
        catch (Exception exception)
        {
            _logger.Error(
                stage: "EngineProcessStart",
                exception: exception);

            throw;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<string> StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(
            cancellationToken);

        try
        {
            ThrowIfDisposed();
            CleanupExitedProcessUnsafe();

            if (!IsProcessRunningUnsafe())
            {
                return "ENGINE_ALREADY_STOPPED";
            }

            Process process =
                _process!;

            int processId =
                process.Id;

            _logger.Info(
                stage: "EngineProcess",
                result:
                    $"StopRequested; pid={processId}");

            bool exitedGracefully = false;

            try
            {
                if (process.CloseMainWindow())
                {
                    exitedGracefully =
                        await WaitForExitAsync(
                            process,
                            timeout:
                                TimeSpan.FromSeconds(
                                    _options.StopTimeoutSeconds),
                            cancellationToken);
                }
            }
            catch (InvalidOperationException)
            {
                exitedGracefully = true;
            }

            if (!exitedGracefully &&
                !process.HasExited)
            {
                _logger.Info(
                    stage: "EngineProcess",
                    result:
                        $"ForceKill; pid={processId}");

                process.Kill(
                    entireProcessTree: true);

                bool exitedAfterKill =
                    await WaitForExitAsync(
                        process,
                        timeout:
                            TimeSpan.FromSeconds(
                                _options.KillTimeoutSeconds),
                        cancellationToken);

                if (!exitedAfterKill &&
                    !process.HasExited)
                {
                    throw new TimeoutException(
                        $"Engine process {processId} did not exit after Kill.");
                }
            }

            CleanupProcessUnsafe();

            _logger.Info(
                stage: "EngineProcess",
                result:
                    $"Stopped; pid={processId}");

            return "ENGINE_STOPPED";
        }
        catch (Exception exception)
        {
            _logger.Error(
                stage: "EngineProcessStop",
                exception: exception);

            throw;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<string> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(
            cancellationToken);

        try
        {
            ThrowIfDisposed();
            CleanupExitedProcessUnsafe();

            return IsProcessRunningUnsafe()
                ? "ENGINE_RUNNING"
                : "ENGINE_STOPPED";
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<string> GetDetailsAsync(
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(
            cancellationToken);

        try
        {
            ThrowIfDisposed();
            CleanupExitedProcessUnsafe();

            if (!IsProcessRunningUnsafe())
            {
                return
                    $"ENGINE_STOPPED PID=0 MODE={_options.Mode}";
            }

            return
                $"ENGINE_RUNNING PID={_process!.Id} MODE={_options.Mode}";
        }
        finally
        {
            _sync.Release();
        }
    }

    public string GetConfig()
    {
        string executableName =
            Path.GetFileName(
                Environment.ExpandEnvironmentVariables(
                    _options.ExecutablePath));

        string workingDirectory =
            string.IsNullOrWhiteSpace(
                _options.WorkingDirectory)
                ? "."
                : _options.WorkingDirectory.Trim();

        return
            $"PROFILE_ID={_options.ProfileId}; " +
            $"DISPLAY_NAME={_options.DisplayName}; " +
            $"SOURCE={_options.ConfigurationSource}; " +
            $"WARNING={_options.ConfigurationWarning}; " +
            $"MODE={_options.Mode}; " +
            $"EXECUTABLE={executableName}; " +
            $"ARGUMENTS={_options.Arguments}; " +
            $"WORKDIR={workingDirectory}; " +
            $"REQUIRES_ADMIN={_options.RequiresAdmin}; " +
            $"USES_WINDIVERT={_options.UsesWinDivert}; " +
            $"STOP_TIMEOUT={_options.StopTimeoutSeconds}; " +
            $"KILL_TIMEOUT={_options.KillTimeoutSeconds}";
    }

    public string GetConfigJson()
    {
        string executableName =
            Path.GetFileName(
                Environment.ExpandEnvironmentVariables(
                    _options.ExecutablePath));

        string workingDirectory =
            string.IsNullOrWhiteSpace(
                _options.WorkingDirectory)
                ? "."
                : _options.WorkingDirectory.Trim();

        var config =
            new EngineConfigResponse
            {
                ProfileId =
                    _options.ProfileId,

                DisplayName =
                    _options.DisplayName,

                ConfigurationSource =
                    _options.ConfigurationSource,

                ConfigurationWarning =
                    _options.ConfigurationWarning,

                Mode =
                    _options.Mode,

                Executable =
                    executableName,

                Arguments =
                    _options.Arguments,

                WorkingDirectory =
                    workingDirectory,

                RequiresAdmin =
                    _options.RequiresAdmin,

                UsesWinDivert =
                    _options.UsesWinDivert,

                StopTimeoutSeconds =
                    _options.StopTimeoutSeconds,

                KillTimeoutSeconds =
                    _options.KillTimeoutSeconds
            };

        return JsonSerializer.Serialize(
            config);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await StopAsync();
        }
        catch (Exception exception)
        {
            _logger.Error(
                stage: "EngineProcessDispose",
                exception: exception);
        }

        await _sync.WaitAsync();

        try
        {
            CleanupProcessUnsafe();
            _disposed = true;
        }
        finally
        {
            _sync.Release();
            _sync.Dispose();
        }
    }

    private void Process_Exited(
        object? sender,
        EventArgs e)
    {
        if (sender is not Process process)
        {
            return;
        }

        int processId;

        try
        {
            processId = process.Id;
        }
        catch
        {
            processId = 0;
        }

        int? exitCode = null;

        try
        {
            exitCode = process.ExitCode;
        }
        catch
        {
            // Код завершения может быть уже недоступен.
        }

        _logger.Info(
            stage: "EngineProcess",
            result:
                $"Exited; pid={processId}; exitCode={exitCode?.ToString() ?? "unknown"}");
    }

    private void Process_OutputDataReceived(
        object sender,
        DataReceivedEventArgs e)
    {
        // Поток читается, чтобы буфер процесса не переполнился.
    }

    private void Process_ErrorDataReceived(
        object sender,
        DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        _logger.Info(
            stage: "EngineProcessStderr",
            result: e.Data);
    }

    private bool IsProcessRunningUnsafe()
    {
        return
            _process is not null &&
            !_process.HasExited;
    }

    private void CleanupExitedProcessUnsafe()
    {
        if (_process is null)
        {
            return;
        }

        if (!_process.HasExited)
        {
            return;
        }

        CleanupProcessUnsafe();
    }

    private void CleanupProcessUnsafe()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            _process.OutputDataReceived -=
                Process_OutputDataReceived;

            _process.ErrorDataReceived -=
                Process_ErrorDataReceived;

            _process.Exited -=
                Process_Exited;

            _process.Dispose();
        }
        finally
        {
            _process = null;
        }
    }

    private static string ResolveExecutablePath(
        string configuredPath)
    {
        string expanded =
            Environment.ExpandEnvironmentVariables(
                configuredPath.Trim());

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(
                expanded);
        }

        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                expanded));
    }

    private static string ResolveWorkingDirectory(
        string configuredPath)
    {
        string expanded =
            Environment.ExpandEnvironmentVariables(
                configuredPath.Trim());

        if (string.IsNullOrWhiteSpace(expanded) ||
            expanded == ".")
        {
            return AppContext.BaseDirectory;
        }

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(
                expanded);
        }

        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                expanded));
    }

    private static void ValidateOptions(
        EngineOptions options)
    {
        if (string.IsNullOrWhiteSpace(
                options.Mode))
        {
            throw new InvalidOperationException(
                "Engine:Mode cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(
                options.ExecutablePath))
        {
            throw new InvalidOperationException(
                "Engine:ExecutablePath cannot be empty.");
        }

        if (options.StopTimeoutSeconds < 1 ||
            options.StopTimeoutSeconds > 60)
        {
            throw new InvalidOperationException(
                "Engine:StopTimeoutSeconds must be between 1 and 60.");
        }

        if (options.KillTimeoutSeconds < 1 ||
            options.KillTimeoutSeconds > 60)
        {
            throw new InvalidOperationException(
                "Engine:KillTimeoutSeconds must be between 1 and 60.");
        }
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return true;
        }

        using CancellationTokenSource timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCts.CancelAfter(
            timeout);

        try
        {
            await process.WaitForExitAsync(
                timeoutCts.Token);

            return true;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return process.HasExited;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private sealed class EngineConfigResponse
    {
        public string ProfileId { get; init; } =
            string.Empty;

        public string DisplayName { get; init; } =
            string.Empty;

        public string ConfigurationSource { get; init; } =
            string.Empty;

        public string ConfigurationWarning { get; init; } =
            string.Empty;

        public string Mode { get; init; } =
            string.Empty;

        public string Executable { get; init; } =
            string.Empty;

        public string Arguments { get; init; } =
            string.Empty;

        public string WorkingDirectory { get; init; } =
            string.Empty;

        public bool RequiresAdmin { get; init; }

        public bool UsesWinDivert { get; init; }

        public int StopTimeoutSeconds { get; init; }

        public int KillTimeoutSeconds { get; init; }
    }
}




