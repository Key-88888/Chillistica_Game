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
/// writable by the unprivileged user — anyone running as that user could swap
/// winws.exe, cygwin1.dll or WinDivert.dll for a payload and have it executed
/// as administrator the next time the user presses the button.
///
/// The expected hashes therefore CANNOT live in a file next to the binaries
/// (an attacker who can rewrite the binary can rewrite that file too). The
/// manifest is compiled into this assembly as an embedded resource instead, so
/// tampering requires replacing the app executable itself — which is the same
/// binary the user already accepts at the UAC prompt, i.e. the existing trust
/// boundary rather than a new hole.
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
    /// Verifies every pinned engine file against the embedded manifest and
    /// throws <see cref="InvalidOperationException"/> on any mismatch (fail
    /// closed). Call immediately before starting winws.
    ///
    /// Deliberately holds NO file locks across the launch. Keeping a
    /// <see cref="FileShare.Read"/> handle open would deny delete-sharing, and
    /// the Windows image loader opens DLLs with FILE_SHARE_READ|FILE_SHARE_DELETE
    /// — so locking cygwin1.dll / WinDivert.dll / WinDivert64.sys could make
    /// winws fail to load them or fail to install the driver. Closing a
    /// microsecond-wide TOCTOU window is not worth breaking the engine: the real
    /// threat this gate answers is a file swapped between sessions, which the
    /// hash check catches completely.
    /// </summary>
    public static void VerifyOrThrow()
    {
        EngineTrustManifest manifest = LoadEmbeddedManifest();

        if (manifest.TrustedBinaries.Count == 0)
        {
            throw new InvalidOperationException(
                "Манифест доверия движка пуст — запуск отменён.");
        }

        foreach (TrustedBinary entry in manifest.TrustedBinaries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path) ||
                string.IsNullOrWhiteSpace(entry.Sha256))
            {
                continue;
            }

            // Manifest paths are app-root relative (Engine\winws2\bin\...).
            string fullPath =
                Path.Combine(AppContext.BaseDirectory, entry.Path);

            if (!File.Exists(fullPath))
            {
                if (!entry.Required)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Файл движка отсутствует: {entry.Path}");
            }

            string actual;

            using (var stream = new FileStream(
                       fullPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                actual = Convert.ToHexString(SHA256.HashData(stream));
            }

            if (!actual.Equals(
                    entry.Sha256.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Файл движка не прошёл проверку целостности: {entry.Path}. " +
                    "Запуск отменён — возможна подмена.");
            }
        }
    }

    /// <summary>
    /// Best-effort advisory check: is the engine directory writable by a
    /// non-admin principal? This is NOT a hard gate — refusing would break the
    /// intended unzip-and-run distribution — but it is worth surfacing, because
    /// in such a location the integrity check above is the only thing standing
    /// between a local attacker and elevated code execution.
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
            // Unreadable ACL is not a reason to block anything — the hash gate
            // is the real control.
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
