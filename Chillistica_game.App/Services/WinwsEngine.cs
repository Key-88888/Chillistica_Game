using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Chillistica_game.App.Services;

/// <summary>
/// In-process manager for the winws.exe DPI-bypass engine. Replaces the old
/// Windows Service + named-pipe EngineProcessManager: the WPF app runs elevated
/// (app.manifest requireAdministrator) and launches winws directly as a child,
/// exactly like zapret's "run winws from an admin console" model.
/// </summary>
public sealed class WinwsEngine : IAsyncDisposable
{
    private readonly Action<string, string>? _log;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly ConcurrentQueue<string> _recentOutput = new();

    private Process? _process;
    private bool _disposed;

    public WinwsEngine(Action<string, string>? log = null)
    {
        _log = log;

        // Last line of defence: if the app is killed without a clean shutdown,
        // still take winws (and the WinDivert capture) down with it.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillQuietly();
    }

    public bool IsRunning =>
        _process is { HasExited: false };

    public string RecentOutput =>
        string.Join(Environment.NewLine, _recentOutput);

    /// <summary>
    /// Compose the winws command line for the selected apps/strategies and start
    /// the engine. Any previously running instance is stopped first, so each
    /// fallback round launches cleanly with the new arguments.
    /// </summary>
    public async Task<string> StartWithAppsAsync(
        IReadOnlyDictionary<string, int> selections,
        CancellationToken cancellationToken = default)
    {
        if (selections is null || selections.Count == 0)
        {
            return "ENGINE_NO_SELECTION";
        }

        StrategyComposer.ComposedProfile profile;

        try
        {
            profile = StrategyComposer.Compose(
                selections.Select(kv => (kv.Key, kv.Value)).ToList());
        }
        catch (Exception ex)
        {
            _log?.Invoke("EngineCompose", $"Failed; {ex.Message}");
            return $"ENGINE_COMPOSE_FAILED: {ex.Message}";
        }

        return await StartAsync(profile, cancellationToken);
    }

    private async Task<string> StartAsync(
        StrategyComposer.ComposedProfile profile,
        CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);

        try
        {
            ThrowIfDisposed();

            // Restart cleanly if something is already running.
            await StopUnsafeAsync();

            string exePath = StrategyComposer.WinwsExecutablePath;
            string workingDirectory = StrategyComposer.EngineDirectory;

            if (!File.Exists(exePath))
            {
                return $"ENGINE_EXE_MISSING: {exePath}";
            }

            foreach (string relativeFile in profile.RequiredFiles)
            {
                // Strategy FileHash paths are app-root relative (Engine\winws2\
                // files\...), whereas winws's own --hostlist arg is working-dir
                // relative (files\...). Resolve the pre-flight check against the
                // app base directory, not the engine working directory.
                string full = Path.Combine(AppContext.BaseDirectory, relativeFile);

                if (!File.Exists(full))
                {
                    return $"ENGINE_FILE_MISSING: {relativeFile}";
                }
            }

            _recentOutput.Clear();

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = profile.Arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory
            };

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnOutput;

            try
            {
                if (!process.Start())
                {
                    process.Dispose();
                    return "ENGINE_START_FAILED";
                }
            }
            catch (Exception ex)
            {
                process.Dispose();
                _log?.Invoke("EngineStart", $"Exception; {ex.Message}");
                return $"ENGINE_START_EXCEPTION: {ex.Message}";
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;

            // winws crashes (e.g. WinDivert mismatch → 0xC0000005) surface as an
            // immediate exit. Give it a moment and confirm it is still alive so a
            // dead engine is never reported as "protection on".
            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);

            process.Refresh();

            if (process.HasExited)
            {
                int code = SafeExitCode(process);

                _log?.Invoke(
                    "EngineStart",
                    $"ExitedImmediately; code=0x{code:X8}; output={RecentOutput}");

                CleanupUnsafe();

                return $"ENGINE_EXITED_IMMEDIATELY: 0x{code:X8}";
            }

            _log?.Invoke(
                "EngineStart",
                $"Started; pid={process.Id}; args={profile.Arguments}");

            return "ENGINE_STARTED";
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);

        try
        {
            ThrowIfDisposed();

            if (_process is null || _process.HasExited)
            {
                CleanupUnsafe();
                return "ENGINE_ALREADY_STOPPED";
            }

            await StopUnsafeAsync();
            return "ENGINE_STOPPED";
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task StopUnsafeAsync()
    {
        if (_process is null)
        {
            return;
        }

        int pid = SafeId(_process);

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                try
                {
                    await _process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // fall through to cleanup
                }
            }

            _log?.Invoke("EngineStop", $"Stopped; pid={pid}");
        }
        catch (Exception ex)
        {
            _log?.Invoke("EngineStop", $"Exception; pid={pid}; {ex.Message}");
        }
        finally
        {
            CleanupUnsafe();
        }
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        // Keep a short rolling tail for diagnostics.
        _recentOutput.Enqueue(e.Data);

        while (_recentOutput.Count > 40)
        {
            _recentOutput.TryDequeue(out _);
        }

        _log?.Invoke("EngineOutput", Truncate(e.Data, 500));
    }

    private void CleanupUnsafe()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            _process.OutputDataReceived -= OnOutput;
            _process.ErrorDataReceived -= OnOutput;
            _process.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _process = null;
        }
    }

    private void KillQuietly()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort on shutdown
        }
    }

    private static int SafeId(Process p)
    {
        try { return p.Id; } catch { return 0; }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return 0; }
    }

    private static string Truncate(string line, int max) =>
        line.Length <= max ? line : line[..max] + "...<truncated>";

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _sync.WaitAsync();

        try
        {
            await StopUnsafeAsync();
            _disposed = true;
        }
        finally
        {
            _sync.Release();
            _sync.Dispose();
        }
    }
}
