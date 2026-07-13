using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Chillistica_game.Service;

public sealed class ServiceLogger
{
    private readonly string _logDirectory;
    private readonly object _syncRoot = new();
    private bool _directoryHardened;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = false
        };

    public ServiceLogger()
    {
        _logDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "Chillistica_game",
                "Logs");
    }

    public void Info(
        string stage,
        string result)
    {
        Write(
            level: "Information",
            stage: stage,
            result: result,
            error: null);
    }

    public void Error(
        string stage,
        Exception exception)
    {
        Write(
            level: "Error",
            stage: stage,
            result: "Failed",
            error: exception.Message);
    }

    public string GetCurrentLogPath()
    {
        return Path.Combine(
            _logDirectory,
            $"service-{DateTime.Now:yyyy-MM-dd}.jsonl");
    }

    private void Write(
        string level,
        string stage,
        string result,
        string? error)
    {
        try
        {
            if (!EnsureHardenedLogDirectory())
            {
                // The log directory is missing/unsafe (e.g. a planted junction);
                // drop the entry rather than let SYSTEM append through it.
                return;
            }

            var entry = new ServiceLogEntry
            {
                TimestampUtc =
                    DateTimeOffset.UtcNow,

                Level =
                    level,

                Stage =
                    stage,

                Result =
                    result,

                Error =
                    error
            };

            string json =
                JsonSerializer.Serialize(
                    entry,
                    SerializerOptions);

            lock (_syncRoot)
            {
                File.AppendAllText(
                    GetCurrentLogPath(),
                    json + Environment.NewLine);
            }
        }
        catch
        {
            // Ошибка журнала не должна завершать службу.
        }
    }

    // The service runs as LocalSystem and writes here, but %ProgramData% lets any
    // standard user create subfolders, so a low-privileged user could pre-create
    // (and own) this directory to read the winws bypass configuration out of the
    // logs, or plant a junction to redirect SYSTEM's appends. We therefore refuse
    // to write through a reparse point and lock the directory down to
    // SYSTEM + Administrators on first use.
    private bool EnsureHardenedLogDirectory()
    {
        Directory.CreateDirectory(
            _logDirectory);

        if (new DirectoryInfo(_logDirectory).Attributes
                .HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        if (_directoryHardened)
        {
            return true;
        }

        try
        {
            var system =
                new SecurityIdentifier(
                    WellKnownSidType.LocalSystemSid,
                    domainSid: null);

            var administrators =
                new SecurityIdentifier(
                    WellKnownSidType.BuiltinAdministratorsSid,
                    domainSid: null);

            var security = new DirectorySecurity();

            // Break inheritance and drop any inherited ACEs (this is what removes
            // the %ProgramData% "Users can read/create" grants).
            security.SetAccessRuleProtection(
                isProtected: true,
                preserveInheritance: false);

            security.SetOwner(administrators);

            foreach (SecurityIdentifier principal in new[] { system, administrators })
            {
                security.AddAccessRule(
                    new FileSystemAccessRule(
                        principal,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit |
                        InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
            }

            new DirectoryInfo(_logDirectory)
                .SetAccessControl(security);
        }
        catch
        {
            // Best effort: never break the service over an ACL-tightening quirk.
        }

        _directoryHardened = true;
        return true;
    }

    private sealed class ServiceLogEntry
    {
        public DateTimeOffset TimestampUtc { get; init; }

        public string Level { get; init; } =
            string.Empty;

        public string Stage { get; init; } =
            string.Empty;

        public string Result { get; init; } =
            string.Empty;

        public string? Error { get; init; }
    }
}
