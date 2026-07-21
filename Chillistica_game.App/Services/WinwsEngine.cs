using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Chillistica_game.App.Services;

/// <summary>
/// In-process manager for the winws.exe DPI-bypass engine. Replaces the old
/// Windows Service + named-pipe EngineProcessManager: the WPF app runs elevated
/// (app.manifest requireAdministrator) and launches winws directly as a child,
/// exactly like zapret's "run winws from an admin console" model.
///
/// Two hard rules this class exists to enforce:
///  1. winws must NEVER outlive the app. It holds a WinDivert filter over all
///     matched traffic, so an orphan silently keeps mangling the user's network
///     with no UI to stop it. A Job Object with KILL_ON_JOB_CLOSE makes the OS
///     guarantee this even on a Task Manager kill or a crash.
///  2. Nothing in here may capture the UI SynchronizationContext. MainWindow
///     tears the engine down on the dispatcher thread, so every await below is
///     ConfigureAwait(false); otherwise a continuation is posted to a dispatcher
///     queue that the blocked UI thread can never pump (classic sync-over-async
///     deadlock that hangs the app on close and orphans winws).
/// </summary>
public sealed class WinwsEngine : IAsyncDisposable
{
    private readonly Action<string, string>? _log;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly ConcurrentQueue<string> _recentOutput = new();
    // Not readonly: closed exactly once via Interlocked.Exchange, because both
    // StopImmediate (Closing + ProcessExit) and DisposeAsync can reach it.
    private IntPtr _job;

    private Process? _process;
    private bool _disposed;

    // Set around every stop/restart WE initiate, so an intentional kill is not
    // reported to the UI as an engine crash.
    private volatile bool _intentionalStop;

    /// <summary>
    /// Raised off the UI thread when a RUNNING engine dies on its own (crash,
    /// external kill, WinDivert driver unload). Without it the UI keeps claiming
    /// "protection on" for a process that no longer exists — the 900 ms start
    /// probe only catches an immediate exit, not a death five minutes later.
    /// </summary>
    public event Action<string>? EngineExitedUnexpectedly;

    public WinwsEngine(Action<string, string>? log = null)
    {
        _log = log;
        _job = CreateKillOnCloseJob();

        // A previous run that was force-killed (or that hit the old close-time
        // deadlock) can leave an elevated winws behind still filtering traffic.
        // Reap those before we ever start a new one.
        ReapOrphanedEngines();

        // Secondary backstop for a clean shutdown; the Job Object is the one
        // that actually holds under abnormal termination.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopImmediate();
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

        return await StartAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> StartAsync(
        StrategyComposer.ComposedProfile profile,
        CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();

            // Restart cleanly if something is already running.
            await StopUnsafeAsync().ConfigureAwait(false);

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

            // Fail closed if any pinned engine binary was tampered with. No file
            // locks are held across the launch — see EngineIntegrity for why
            // locking would risk breaking winws's own DLL/driver loading.
            try
            {
                EngineIntegrity.VerifyOrThrow();
            }
            catch (Exception ex)
            {
                _log?.Invoke("EngineIntegrity", $"Blocked; {ex.Message}");
                return $"ENGINE_INTEGRITY_FAILED: {ex.Message}";
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
            process.Exited += OnProcessExited;

            _intentionalStop = false;

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

            // Bind to the job BEFORE anything else can go wrong, so the OS owns
            // the guarantee that winws dies with us.
            AssignToJob(process);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;

            // winws crashes (e.g. WinDivert mismatch → 0xC0000005) surface as an
            // immediate exit. Give it a moment and confirm it is still alive so a
            // dead engine is never reported as "protection on".
            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken)
                .ConfigureAwait(false);

            Process started = _process!;
            started.Refresh();

            if (started.HasExited)
            {
                int code = SafeExitCode(started);

                _log?.Invoke(
                    "EngineStart",
                    $"ExitedImmediately; code=0x{code:X8}; output={RecentOutput}");

                CleanupUnsafe();

                return $"ENGINE_EXITED_IMMEDIATELY: 0x{code:X8}";
            }

            _log?.Invoke(
                "EngineStart",
                $"Started; pid={started.Id}; strategies={profile.RequiredFiles.Count}");

            return "ENGINE_STARTED";
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();

            if (_process is null || _process.HasExited)
            {
                CleanupUnsafe();
                return "ENGINE_ALREADY_STOPPED";
            }

            await StopUnsafeAsync().ConfigureAwait(false);
            return "ENGINE_STOPPED";
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <summary>
    /// Synchronous, best-effort teardown for shutdown paths (window closing,
    /// ProcessExit). Deliberately does NOT touch the semaphore and never awaits:
    /// it is called from the UI thread, where blocking on an async operation
    /// deadlocks the dispatcher.
    /// </summary>
    public void StopImmediate()
    {
        _intentionalStop = true;

        Process? process = _process;

        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // best effort — the job object below is the real guarantee
        }

        CloseJobOnce();
    }

    /// <summary>
    /// Closing the job handle kills anything still assigned to it. Exchange to
    /// zero first so the handle is closed exactly once: StopImmediate runs from
    /// both the window-close handler and the ProcessExit hook, and DisposeAsync
    /// closes it too — a double CloseHandle could tear down an unrelated handle
    /// that happened to reuse the value.
    /// </summary>
    private void CloseJobOnce()
    {
        IntPtr job = Interlocked.Exchange(ref _job, IntPtr.Zero);

        if (job != IntPtr.Zero)
        {
            try { CloseHandle(job); } catch { /* best effort */ }
        }
    }

    private async Task StopUnsafeAsync()
    {
        if (_process is null)
        {
            return;
        }

        _intentionalStop = true;

        int pid = SafeId(_process);

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                try
                {
                    await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
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

        // Keep a short rolling tail for diagnostics. NOT written to the on-disk
        // log: winws stdout echoes the hostlists and desync details, which would
        // persist proof of bypass usage on the user's machine.
        _recentOutput.Enqueue(e.Data);

        while (_recentOutput.Count > 40)
        {
            _recentOutput.TryDequeue(out _);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process exited)
        {
            return;
        }

        // Ignore exits we caused (stop, or the restart between fallback rounds)
        // and exits of a process we have already replaced.
        if (_intentionalStop || !ReferenceEquals(exited, _process))
        {
            return;
        }

        string code = $"0x{SafeExitCode(exited):X8}";
        _log?.Invoke("EngineExit", $"Unexpected; code={code}");

        EngineExitedUnexpectedly?.Invoke(code);
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
            _process.Exited -= OnProcessExited;
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

    /// <summary>
    /// Kills any winws.exe left over from a previous run of THIS install (matched
    /// by executable path, so a user's own separate zapret is never touched).
    /// </summary>
    private void ReapOrphanedEngines()
    {
        string ourExe;

        try
        {
            ourExe = Path.GetFullPath(StrategyComposer.WinwsExecutablePath);
        }
        catch
        {
            return;
        }

        foreach (Process stray in Process.GetProcessesByName("winws"))
        {
            try
            {
                string? path = stray.MainModule?.FileName;

                if (path is not null &&
                    Path.GetFullPath(path).Equals(ourExe, StringComparison.OrdinalIgnoreCase))
                {
                    stray.Kill(entireProcessTree: true);
                    stray.WaitForExit(3000);
                    _log?.Invoke("EngineReap", $"KilledOrphan; pid={SafeId(stray)}");
                }
            }
            catch
            {
                // A process we cannot open is not ours to kill.
            }
            finally
            {
                stray.Dispose();
            }
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

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _sync.WaitAsync().ConfigureAwait(false);

        try
        {
            await StopUnsafeAsync().ConfigureAwait(false);
            _disposed = true;
        }
        finally
        {
            _sync.Release();
            _sync.Dispose();

            CloseJobOnce();
        }
    }

    // ---- Job Object interop ---------------------------------------------
    //
    // KILL_ON_JOB_CLOSE means: when the last handle to the job closes — including
    // when the OS closes our handles because the app was terminated — every
    // process in the job is killed. That is what stops an elevated winws (and its
    // WinDivert filter) from surviving a Task Manager kill of the app.

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private static IntPtr CreateKillOnCloseJob()
    {
        try
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);

            if (job == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr buffer = Marshal.AllocHGlobal(length);

            try
            {
                Marshal.StructureToPtr(info, buffer, false);

                if (!SetInformationJobObject(
                        job,
                        JobObjectExtendedLimitInformation,
                        buffer,
                        (uint)length))
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return job;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private void AssignToJob(Process process)
    {
        if (_job == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (!AssignProcessToJobObject(_job, process.Handle))
            {
                _log?.Invoke(
                    "EngineJob",
                    $"AssignFailed; win32={Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke("EngineJob", $"AssignException; {ex.Message}");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
