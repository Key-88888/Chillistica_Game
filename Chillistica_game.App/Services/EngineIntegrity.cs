using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace Chillistica_game.App.Services;

/// <summary>
/// Pre-launch trust gate for the native engine.
///
/// The app runs elevated (app.manifest requireAdministrator) and starts
/// winws.exe as a child, so that child inherits admin with no second UAC
/// prompt. Distribution is unzip-and-run, which means Engine\winws2\bin is
/// writable by the unprivileged user.
///
/// Three things this gate must do, and why:
///  1. Pin the expected bytes. The hashes CANNOT live in a file next to the
///     binaries (an attacker who can rewrite the binary can rewrite that file
///     too), so the manifest is compiled into this assembly as an embedded
///     resource. Tampering then requires replacing the app executable itself —
///     the binary the user already accepts at the UAC prompt.
///  2. Seal the directories. Hashing only the listed files misses a file that is
///     ADDED. That is not academic: winws.exe statically imports wlanapi.dll,
///     which is NOT a KnownDLL, so the loader resolves it from the image's own
///     directory BEFORE System32. Dropping Engine\winws2\bin\wlanapi.dll changes
///     no pinned hash, yet its DllMain would run as administrator. Any unexpected
///     file in a sealed directory is therefore a hard failure.
///  3. Close the check-to-launch window. The verified handles are kept open with
///     FileShare.Read across Process.Start, which denies writers and deleters.
///     This is safe for the image and its DLLs: the share check groups
///     FILE_EXECUTE with read, so the loader can still map them.
/// </summary>
public static class EngineIntegrity
{
    // Set explicitly in the csproj so the lookup does not depend on how MSBuild
    // derives resource names from a linked file.
    private const string ManifestResourceName =
        "Chillistica_game.trusted-manifest.json";

    private static readonly JsonSerializerOptions ReadOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

    /// <summary>
    /// Write-denying handles held on the verified engine files. Keep alive until
    /// winws has started, then dispose, so nothing can swap a verified file in
    /// the window between the hash and the launch.
    /// </summary>
    public sealed class EngineLease : IDisposable
    {
        private readonly List<FileStream> _locks;

        internal EngineLease(List<FileStream> locks) => _locks = locks;

        public void Dispose()
        {
            foreach (FileStream stream in _locks)
            {
                try { stream.Dispose(); } catch { /* best effort */ }
            }

            _locks.Clear();
        }
    }

    /// <summary>
    /// Verifies the pinned engine files and seals their directories. Throws
    /// <see cref="InvalidOperationException"/> on any mismatch, missing required
    /// file, or unexpected extra file (fail closed).
    /// </summary>
    public static EngineLease VerifyOrThrow()
    {
        EngineTrustManifest manifest = LoadEmbeddedManifest();

        if (manifest.TrustedBinaries.Count == 0)
        {
            throw new InvalidOperationException(
                "Манифест доверия движка пуст — запуск отменён.");
        }

        var pinnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var held = new List<FileStream>();

        try
        {
            foreach (TrustedBinary entry in manifest.TrustedBinaries)
            {
                if (string.IsNullOrWhiteSpace(entry.Path) ||
                    string.IsNullOrWhiteSpace(entry.Sha256))
                {
                    continue;
                }

                string relative = Normalize(entry.Path);
                pinnedPaths.Add(relative);

                string fullPath =
                    Path.Combine(AppContext.BaseDirectory, relative);

                if (!File.Exists(fullPath))
                {
                    if (!entry.Required)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Файл движка отсутствует: {entry.Path}");
                }

                // FileShare.Read denies write and delete to everyone else for as
                // long as this handle lives.
                var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                bool keepOpen = false;

                try
                {
                    string actual = Convert.ToHexString(SHA256.HashData(stream));

                    if (!actual.Equals(
                            entry.Sha256.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Файл движка не прошёл проверку целостности: {entry.Path}. " +
                            "Запуск отменён — возможна подмена.");
                    }

                    keepOpen = ShouldHoldAcrossLaunch(relative);
                }
                finally
                {
                    if (keepOpen)
                    {
                        held.Add(stream);
                    }
                    else
                    {
                        stream.Dispose();
                    }
                }
            }

            VerifySealedDirectories(manifest, pinnedPaths);

            return new EngineLease(held);
        }
        catch
        {
            foreach (FileStream stream in held)
            {
                try { stream.Dispose(); } catch { /* best effort */ }
            }

            throw;
        }
    }

    /// <summary>
    /// A sealed directory must contain EXACTLY the pinned files. This is what
    /// turns "attacker adds a DLL the loader will pick up" into a refusal.
    /// </summary>
    private static void VerifySealedDirectories(
        EngineTrustManifest manifest,
        HashSet<string> pinnedPaths)
    {
        foreach (string sealedDirectory in manifest.SealedDirectories)
        {
            if (string.IsNullOrWhiteSpace(sealedDirectory))
            {
                continue;
            }

            string fullDirectory =
                Path.Combine(AppContext.BaseDirectory, Normalize(sealedDirectory));

            if (!Directory.Exists(fullDirectory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(
                         fullDirectory, "*", SearchOption.AllDirectories))
            {
                string relative =
                    Normalize(Path.GetRelativePath(AppContext.BaseDirectory, file));

                if (!pinnedPaths.Contains(relative))
                {
                    throw new InvalidOperationException(
                        $"В защищённом каталоге движка обнаружен посторонний файл: {relative}. " +
                        "Запуск отменён — возможна подмена.");
                }
            }
        }
    }

    /// <summary>
    /// Hold the image and the user-mode DLLs it loads. The kernel driver
    /// (.sys) is deliberately released after hashing: loading it is gated by
    /// Windows driver-signature enforcement, and holding it could interfere with
    /// service-side driver installation.
    /// </summary>
    private static bool ShouldHoldAcrossLaunch(string relativePath) =>
        relativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        path.Replace('/', '\\').TrimStart('\\');

    /// <summary>
    /// Advisory only: is the engine directory writable by a non-admin principal?
    /// Refusing outright would break the intended unzip-and-run distribution, but
    /// in such a location the checks above are the only thing between a local
    /// attacker and elevated code execution.
    /// </summary>
    public static bool IsEngineDirectoryUserWritable()
    {
        SecurityIdentifier[] broad =
        {
            new(WellKnownSidType.WorldSid, null),
            new(WellKnownSidType.BuiltinUsersSid, null),
            new(WellKnownSidType.AuthenticatedUserSid, null),
            new(WellKnownSidType.InteractiveSid, null)
        };

        const FileSystemRights WriteRights =
            FileSystemRights.WriteData |
            FileSystemRights.CreateFiles |
            FileSystemRights.CreateDirectories |
            FileSystemRights.AppendData |
            FileSystemRights.Write |
            FileSystemRights.Modify |
            FileSystemRights.FullControl;

        try
        {
            var security = new DirectorySecurity(
                StrategyComposer.EngineDirectory,
                AccessControlSections.Access);

            foreach (FileSystemAccessRule rule in
                     security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow ||
                    (rule.FileSystemRights & WriteRights) == 0)
                {
                    continue;
                }

                if (broad.Any(sid => sid.Equals(rule.IdentityReference)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Unreadable ACL is not a reason to block: the hash + seal checks are
            // the real control.
        }

        return false;
    }

    private static EngineTrustManifest LoadEmbeddedManifest()
    {
        Assembly assembly = typeof(EngineIntegrity).Assembly;

        using Stream? stream =
            assembly.GetManifestResourceStream(ManifestResourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                "В сборку не встроен манифест доверия движка — запуск отменён.");
        }

        return JsonSerializer.Deserialize<EngineTrustManifest>(stream, ReadOptions)
            ?? throw new InvalidOperationException(
                "Манифест доверия движка не читается — запуск отменён.");
    }

    public sealed class EngineTrustManifest
    {
        public int SchemaVersion { get; init; }

        public IReadOnlyList<string> SealedDirectories { get; init; } =
            Array.Empty<string>();

        public IReadOnlyList<TrustedBinary> TrustedBinaries { get; init; } =
            Array.Empty<TrustedBinary>();
    }

    public sealed class TrustedBinary
    {
        public string Path { get; init; } = string.Empty;

        public string Sha256 { get; init; } = string.Empty;

        public bool Required { get; init; } = true;
    }
}
