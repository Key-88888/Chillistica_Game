using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Chillistica_game.App.Services;

public sealed class UpdateCheckService
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/Key-88888/Chillistica_Game/releases/latest";

    private readonly HttpClient _httpClient;

    public UpdateCheckService()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Chillistica_game/{GetRunningVersion()}");

        _httpClient.DefaultRequestHeaders.Accept.ParseAdd(
            "application/vnd.github+json");
    }

    public static Version GetRunningVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version
            ?? new Version(0, 0, 0);
    }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(
                    ReleasesApiUrl,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            using JsonDocument document =
                JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tagElement))
            {
                return null;
            }

            string? tagName =
                tagElement.GetString();

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            if (!Version.TryParse(
                    tagName.TrimStart('v', 'V'),
                    out Version? latestVersion))
            {
                return null;
            }

            if (latestVersion <= GetRunningVersion())
            {
                return null;
            }

            string? downloadUrl =
                FindReleaseZipUrl(document.RootElement);

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return null;
            }

            return new UpdateCheckResult
            {
                LatestVersion = latestVersion,
                TagName = tagName,
                DownloadUrl = downloadUrl
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> DownloadAndStageUpdateAsync(
        string downloadUrl,
        CancellationToken cancellationToken = default)
    {
        string updatesDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Chillistica_game",
                "updates");

        Directory.CreateDirectory(updatesDirectory);

        string zipPath =
            Path.Combine(updatesDirectory, "update.zip");

        string extractDirectory =
            Path.Combine(updatesDirectory, "staging");

        if (Directory.Exists(extractDirectory))
        {
            Directory.Delete(extractDirectory, recursive: true);
        }

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using (HttpResponseMessage response =
               await _httpClient.GetAsync(
                   downloadUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            await using FileStream fileStream =
                File.Create(zipPath);

            await response.Content.CopyToAsync(
                fileStream,
                cancellationToken);
        }

        ZipFile.ExtractToDirectory(zipPath, extractDirectory);
        File.Delete(zipPath);

        return extractDirectory;
    }

    public void LaunchElevatedApplyUpdate(
        string stagingFolderPath)
    {
        string installScriptPath =
            Path.Combine(stagingFolderPath, "install-package.ps1");

        if (!File.Exists(installScriptPath))
        {
            throw new FileNotFoundException(
                "install-package.ps1 not found in downloaded update.",
                installScriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{installScriptPath}\" -Silent",
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(startInfo);
    }

    private static string? FindReleaseZipUrl(
        JsonElement release)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? name =
                asset.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString()
                    : null;

            if (name is null ||
                !name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (asset.TryGetProperty("browser_download_url", out JsonElement urlElement))
            {
                return urlElement.GetString();
            }
        }

        return null;
    }
}
