using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Chillistica_game.App.Services;

public sealed class UpdateCheckService
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/Key-88888/Chillistica_Game/releases/latest";

    // Only assets served over HTTPS from GitHub's own hosts are ever fetched.
    private static readonly string[] AllowedAssetHosts =
    {
        "github.com",
        "githubusercontent.com" // matches objects.githubusercontent.com etc.
    };

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
            Timeout = TimeSpan.FromSeconds(30)
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
        // Fail closed: if no signing key is pinned into this build, the update
        // channel cannot be trusted, so do not offer or apply any update.
        if (!UpdateSignatureVerifier.IsConfigured)
        {
            return null;
        }

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

            // Bind the asset name to the advertised tag. The release always ships
            // "Chillistica_game-<tag>-win-x64.zip"; requiring an exact match stops
            // a MITM from advertising a fake-high version while pointing the
            // download at a genuinely-signed but OLDER asset (rollback defence),
            // since no signed asset exists whose name carries the forged version.
            string expectedZipName =
                $"Chillistica_game-{tagName}-win-x64.zip";

            string expectedSignatureName =
                expectedZipName + ".sig";

            string? downloadUrl =
                FindAssetUrl(document.RootElement, expectedZipName);

            string? signatureUrl =
                FindAssetUrl(document.RootElement, expectedSignatureName);

            // Both the package and its detached signature must be present and
            // served from an allowed HTTPS host, or the update is not offered.
            if (!IsAllowedAssetUrl(downloadUrl) ||
                !IsAllowedAssetUrl(signatureUrl))
            {
                return null;
            }

            return new UpdateCheckResult
            {
                LatestVersion = latestVersion,
                TagName = tagName,
                DownloadUrl = downloadUrl!,
                SignatureUrl = signatureUrl!
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the package and its detached signature to a staging folder and
    /// verifies the signature against the pinned public key BEFORE anything is
    /// extracted or executed. Throws if the signature is missing/invalid.
    /// The verified zip + signature are left in the staging folder so the
    /// elevated, admin-only updater can re-verify them (TOCTOU defence).
    /// </summary>
    public async Task<string> DownloadAndStageUpdateAsync(
        string downloadUrl,
        string signatureUrl,
        CancellationToken cancellationToken = default)
    {
        if (!UpdateSignatureVerifier.IsConfigured)
        {
            throw new InvalidOperationException(
                "Обновления отключены: в сборку не встроен ключ проверки подписи.");
        }

        if (!IsAllowedAssetUrl(downloadUrl) ||
            !IsAllowedAssetUrl(signatureUrl))
        {
            throw new InvalidOperationException(
                "Адрес обновления не прошёл проверку (недопустимый хост/схема).");
        }

        string updatesDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Chillistica_game",
                "updates");

        string stagingDirectory =
            Path.Combine(updatesDirectory, "staging");

        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        Directory.CreateDirectory(stagingDirectory);

        string zipPath =
            Path.Combine(stagingDirectory, "update.zip");

        string signaturePath =
            Path.Combine(stagingDirectory, "update.zip.sig");

        await DownloadToFileAsync(downloadUrl, zipPath, cancellationToken);
        await DownloadToFileAsync(signatureUrl, signaturePath, cancellationToken);

        byte[] signatureBytes =
            await File.ReadAllBytesAsync(signaturePath, cancellationToken);

        if (!UpdateSignatureVerifier.VerifyFile(zipPath, signatureBytes))
        {
            try
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }

            throw new InvalidOperationException(
                "Подпись обновления недействительна — установка отменена.");
        }

        return stagingDirectory;
    }

    /// <summary>
    /// Launches the TRUSTED, already-installed updater (which lives in the
    /// admin-only install directory and cannot be tampered by a standard user)
    /// elevated, passing the verified zip + signature. The freshly-downloaded
    /// script is never executed, closing the user-writable-staging TOCTOU.
    /// </summary>
    public void LaunchElevatedApplyUpdate(
        string stagingFolderPath)
    {
        string zipPath =
            Path.Combine(stagingFolderPath, "update.zip");

        string signaturePath =
            Path.Combine(stagingFolderPath, "update.zip.sig");

        if (!File.Exists(zipPath) || !File.Exists(signaturePath))
        {
            throw new FileNotFoundException(
                "Проверенный пакет обновления не найден в папке загрузки.",
                zipPath);
        }

        string trustedUpdaterPath =
            ResolveInstalledUpdaterPath();

        if (!File.Exists(trustedUpdaterPath))
        {
            throw new FileNotFoundException(
                "Доверенный установщик обновлений не найден. Переустановите приложение вручную.",
                trustedUpdaterPath);
        }

        // Defence in depth: only ever elevate a script that lives in an admin-only
        // location. If the app is run from a user-writable directory, its sibling
        // apply-update.ps1 could be attacker-planted and would then execute as
        // SYSTEM via runas — refuse rather than hand an untrusted script elevation.
        if (!IsUnderProgramFiles(trustedUpdaterPath))
        {
            throw new InvalidOperationException(
                "Обновление отменено: установщик обновлений находится вне защищённого каталога Program Files.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{trustedUpdaterPath}\" " +
                $"-ZipPath \"{zipPath}\" -SignaturePath \"{signaturePath}\"",
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(startInfo);
    }

    private async Task DownloadToFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        await using FileStream fileStream =
            File.Create(destinationPath);

        await response.Content.CopyToAsync(
            fileStream,
            cancellationToken);
    }

    private static bool IsUnderProgramFiles(string path)
    {
        string fullPath =
            Path.GetFullPath(path);

        foreach (Environment.SpecialFolder folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86
                 })
        {
            string root =
                Environment.GetFolderPath(folder);

            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            string normalizedRoot =
                Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(
                    normalizedRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveInstalledUpdaterPath()
    {
        // App runs from <InstallDir>\App\; the trusted updater is installed one
        // level up at <InstallDir>\apply-update.ps1 (admin-only).
        DirectoryInfo? installRoot =
            new DirectoryInfo(AppContext.BaseDirectory).Parent;

        string root =
            installRoot?.FullName
            ?? AppContext.BaseDirectory;

        return Path.Combine(root, "apply-update.ps1");
    }

    private static bool IsAllowedAssetUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string host = uri.Host;

        return AllowedAssetHosts.Any(allowed =>
            string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindAssetUrl(
        JsonElement release,
        string nameSuffix)
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
                !name.EndsWith(nameSuffix, StringComparison.OrdinalIgnoreCase))
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
