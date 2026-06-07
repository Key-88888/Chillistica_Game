using System.Diagnostics;

namespace Chillistica_game.App.Services;

public sealed class ProcessDetectionService
{
    private static readonly IReadOnlyList<(string AppName, string[] ProcessNames)> KnownApps =
        new List<(string AppName, string[] ProcessNames)>
        {
            ("Discord", new[] { "Discord" }),
            ("Roblox", new[] { "RobloxPlayerBeta", "RobloxPlayerInstaller" }),
            ("Epic Games Launcher", new[] { "EpicGamesLauncher" }),
            ("Fortnite", new[] { "FortniteClient-Win64-Shipping", "FortniteLauncher" }),
            ("v2rayN", new[] { "v2rayN", "wv2rayN", "xray" }),
            ("Happ", new[] { "Happ" }),
            ("Zapret / winws", new[] { "winws", "winws2", "goodbyedpi" })
        };

    public IReadOnlyList<AppProcessStatus> GetStatuses()
    {
        Process[] processes =
            Process.GetProcesses();

        List<AppProcessStatus> result = new();

        foreach ((string appName, string[] processNames) in KnownApps)
        {
            List<string> running =
                processes
                    .Where(process =>
                        processNames.Any(name =>
                            string.Equals(
                                process.ProcessName,
                                name,
                                StringComparison.OrdinalIgnoreCase)))
                    .Select(process =>
                        $"{process.ProcessName}({process.Id})")
                    .Distinct()
                    .OrderBy(value => value)
                    .ToList();

            result.Add(new AppProcessStatus
            {
                AppName = appName,
                ProcessNames = processNames,
                IsRunning = running.Count > 0,
                RunningProcessesText =
                    running.Count > 0
                        ? string.Join(", ", running)
                        : "—"
            });
        }

        return result;
    }
}
