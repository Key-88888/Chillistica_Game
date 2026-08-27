using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using Chillistica_game.App.Services;

namespace Chillistica_game.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Extra backstop, on top of WinwsEngine's own AppDomain.ProcessExit
        // hook: a crash that terminates the process via an unhandled
        // exception is not guaranteed to run ProcessExit the same way a clean
        // return from Main() does. Best effort only — the Job Object
        // (KILL_ON_JOB_CLOSE) is what actually holds if even this cannot run,
        // e.g. a hard Task Manager "End task".
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            try { WinwsEngine.ActiveInstance?.StopImmediate(); } catch { /* best effort */ }
        };

        DispatcherUnhandledException += (_, _) =>
        {
            try { WinwsEngine.ActiveInstance?.StopImmediate(); } catch { /* best effort */ }
            // Deliberately not marking args.Handled = true: we only want the
            // engine stopped before the app dies, not to suppress the crash.
        };

        // Headless verification mode used by the trusted elevated updater
        // (apply-update.ps1): validate a downloaded package's detached signature
        // against the pinned public key, in this admin-only .NET 8 binary, and
        // return the result as the process exit code (0 = valid).
        if (e.Args.Length >= 3 &&
            string.Equals(e.Args[0], "--verify-update", StringComparison.OrdinalIgnoreCase))
        {
            int exitCode = 1;

            try
            {
                byte[] signatureBytes =
                    File.ReadAllBytes(e.Args[2]);

                if (UpdateSignatureVerifier.VerifyFile(e.Args[1], signatureBytes))
                {
                    exitCode = 0;
                }
            }
            catch
            {
                exitCode = 1;
            }

            Shutdown(exitCode);
            return;
        }

        // Headless engine self-test (admin-only diagnostic): compose the winws
        // command line for one OR MORE apps and actually launch the engine, so the
        // real production code path (StrategyComposer + WinwsEngine) can be verified
        // without the UI. A comma-separated appId list (e.g. "youtube,discord,
        // fortnite") composes the SAME multi-profile command the orchestrator sends
        // when several apps are checked at once — the case the single-app test
        // could not reach. Usage: --selftest-engine [appId[,appId...]] [resultFile] [holdSeconds]
        if (e.Args.Length >= 1 &&
            string.Equals(e.Args[0], "--selftest-engine", StringComparison.OrdinalIgnoreCase))
        {
            string appId = e.Args.Length >= 2 ? e.Args[1] : "youtube";
            string resultPath = e.Args.Length >= 3
                ? e.Args[2]
                : Path.Combine(Path.GetTempPath(), "chillistica-selftest.txt");
            // Optional 4th arg: keep the engine RUNNING this many seconds before
            // tearing it down, so an external probe can measure reachability while
            // the bypass is actually active. Without it the engine dies after ~3s
            // and there is no window in which to measure anything.
            int holdSeconds = 3;

            if (e.Args.Length >= 4 &&
                int.TryParse(e.Args[3], out int parsedHold) &&
                parsedHold is > 0 and <= 600)
            {
                holdSeconds = parsedHold;
            }

            Shutdown(RunEngineSelfTest(appId, resultPath, holdSeconds));
            return;
        }

        base.OnStartup(e);

        new MainWindow().Show();
    }

    private static int RunEngineSelfTest(string appId, string resultPath, int holdSeconds = 3)
    {
        var lines = new List<string>();

        try
        {
            string[] appIds = appId
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => a.ToLowerInvariant())
                .ToArray();

            if (appIds.Length == 0)
            {
                appIds = new[] { "youtube" };
            }

            lines.Add($"appId={string.Join(",", appIds)}");
            lines.Add($"engineDir={StrategyComposer.EngineDirectory}");
            lines.Add($"exe={StrategyComposer.WinwsExecutablePath}");
            lines.Add($"exeExists={File.Exists(StrategyComposer.WinwsExecutablePath)}");

            StrategyComposer.ComposedProfile composed =
                StrategyComposer.Compose(appIds.Select(a => (a, 0)).ToList());

            lines.Add($"args={composed.Arguments}");

            var engine = new WinwsEngine();

            string start = engine
                .StartWithAppsAsync(appIds.ToDictionary(a => a, _ => 0))
                .GetAwaiter()
                .GetResult();

            lines.Add($"startResult={start}");
            lines.Add($"holdSeconds={holdSeconds}");

            Thread.Sleep(TimeSpan.FromSeconds(holdSeconds));

            bool running = engine.IsRunning;
            lines.Add($"isRunning={running}");
            lines.Add("recentOutput:");
            lines.Add(engine.RecentOutput);

            engine.StopAsync().GetAwaiter().GetResult();
            lines.Add($"stoppedIsRunning={engine.IsRunning}");

            File.WriteAllLines(resultPath, lines);

            bool captured =
                engine.RecentOutput.Contains(
                    "capture is started",
                    StringComparison.OrdinalIgnoreCase);

            return (start == "ENGINE_STARTED" && running && captured) ? 0 : 1;
        }
        catch (Exception ex)
        {
            lines.Add($"exception={ex.Message}");
            File.WriteAllLines(resultPath, lines);
            return 2;
        }
    }
}
