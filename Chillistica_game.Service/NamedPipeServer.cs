using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;

namespace Chillistica_game.Service;

public sealed class NamedPipeServer : BackgroundService
{
    public const string PipeName =
        "Chillistica_game.Control";

    public const string ProtocolVersion =
        "2";

    private const int MaxConcurrentConnections = 4;

    // Hard cap on a single control command so a client cannot stream an
    // unbounded newline-free line and exhaust service memory (DoS).
    private const int MaxCommandLength = 4096;

    // Strict allowlist for a client-supplied app id: lowercase alphanumerics and
    // dashes only. Blocks path traversal ('..\'), separators, env vars, drive
    // letters and any character that could steer the strategy-catalog file path.
    private static readonly Regex AppIdPattern =
        new("^[a-z0-9-]{1,32}$", RegexOptions.Compiled);

    private static readonly TimeSpan IdleReadTimeout =
        TimeSpan.FromSeconds(5);

    // One-shot flag: the first NamedPipeServerStream created across all accept
    // loops asserts FirstPipeInstance to detect a squatter that pre-created the
    // pipe name; subsequent instances of the name we already own do not.
    private int _firstInstanceClaimed;

    private readonly ServiceLogger _logger;
    private readonly EngineProcessManager _engineProcessManager;

    public NamedPipeServer(
        ServiceLogger logger,
        EngineProcessManager engineProcessManager)
    {
        _logger = logger;
        _engineProcessManager = engineProcessManager;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.Info(
            stage: "NamedPipe",
            result:
                $"Listening; pipe={PipeName}; instances={MaxConcurrentConnections}");

        var acceptLoops =
            Enumerable.Range(0, MaxConcurrentConnections)
                .Select(_ => AcceptLoopAsync(stoppingToken));

        await Task.WhenAll(
            acceptLoops);

        _logger.Info(
            stage: "NamedPipe",
            result: "Stopped");
    }

    private async Task AcceptLoopAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await HandleSingleConnectionAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // Expected client-side disconnect / broken pipe. Loop again
                // immediately without the penalty delay so one misbehaving
                // client cannot throttle overall accept capacity (DoS).
            }
            catch (Exception exception)
            {
                _logger.Error(
                    stage: "NamedPipe",
                    exception: exception);

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(1),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleSingleConnectionAsync(
        CancellationToken stoppingToken)
    {
        PipeSecurity pipeSecurity =
            CreatePipeSecurity();

        // Only the very first stream created (across all accept loops) asserts
        // FirstPipeInstance, so we fail to start if another process squatted the
        // name. Once we own an instance, later instances must not set the flag.
        bool assertFirstInstance =
            Interlocked.CompareExchange(
                ref _firstInstanceClaimed, 1, 0) == 0;

        await using NamedPipeServerStream pipe =
            CreatePipeInstance(
                assertFirstInstance,
                pipeSecurity);

        await pipe.WaitForConnectionAsync(
            stoppingToken);

        _logger.Info(
            stage: "NamedPipeConnection",
            result: "ClientConnected");

        using var reader =
            new StreamReader(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);

        using var writer =
            new StreamWriter(
                pipe,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: true)
            {
                AutoFlush = true
            };

        string? command;

        using (CancellationTokenSource readCts =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       stoppingToken))
        {
            readCts.CancelAfter(
                IdleReadTimeout);

            try
            {
                command =
                    await ReadCommandLineAsync(
                        reader,
                        readCts.Token);
            }
            catch (OperationCanceledException)
                when (!stoppingToken.IsCancellationRequested)
            {
                _logger.Info(
                    stage: "NamedPipeConnection",
                    result: "IdleTimeout; disconnecting client");

                return;
            }
            catch (InvalidDataException)
            {
                _logger.Info(
                    stage: "NamedPipeConnection",
                    result: "CommandTooLong; disconnecting client");

                await writer.WriteLineAsync(
                    "ERROR COMMAND_TOO_LONG");

                return;
            }
        }

        string response =
            await HandleCommandAsync(
                command,
                stoppingToken);

        await writer.WriteLineAsync(
            response);

        _logger.Info(
            stage: "NamedPipeCommand",
            result:
                $"Command={command ?? "<null>"}; Response={response}");
    }

    private NamedPipeServerStream CreatePipeInstance(
        bool assertFirstInstance,
        PipeSecurity pipeSecurity)
    {
        PipeOptions pipeOptions =
            PipeOptions.Asynchronous;

        if (assertFirstInstance)
        {
            pipeOptions |= PipeOptions.FirstPipeInstance;
        }

        try
        {
            return NamedPipeServerStreamAcl.Create(
                pipeName: PipeName,
                direction: PipeDirection.InOut,
                maxNumberOfServerInstances: MaxConcurrentConnections,
                transmissionMode: PipeTransmissionMode.Byte,
                options: pipeOptions,
                inBufferSize: 4096,
                outBufferSize: 4096,
                pipeSecurity: pipeSecurity);
        }
        catch
        {
            // The FirstPipeInstance assertion is our squatter check. If the create
            // fails (a squatter owns the name, or a transient error), release the
            // one-shot claim so a later accept attempt re-asserts it, instead of
            // permanently disabling the protection for the rest of the process.
            if (assertFirstInstance)
            {
                Interlocked.Exchange(
                    ref _firstInstanceClaimed, 0);
            }

            throw;
        }
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security =
            new PipeSecurity();

        var localSystemSid =
            new SecurityIdentifier(
                WellKnownSidType.LocalSystemSid,
                domainSid: null);

        // Authenticated users (the interactive user the WPF app runs as) may
        // drive the engine. Narrower than BUILTIN\Users: excludes anonymous /
        // guest logons. State-changing commands are additionally made safe by
        // strict appId/argument validation rather than by ACL alone.
        var authenticatedUsersSid =
            new SecurityIdentifier(
                WellKnownSidType.AuthenticatedUserSid,
                domainSid: null);

        security.AddAccessRule(
            new PipeAccessRule(
                localSystemSid,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

        security.AddAccessRule(
            new PipeAccessRule(
                authenticatedUsersSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

        security.SetOwner(
            localSystemSid);

        return security;
    }

    private async Task<string> HandleCommandAsync(
        string? command,
        CancellationToken cancellationToken)
    {
        string trimmed =
            command?.Trim()
            ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return "ERROR EMPTY_COMMAND";
        }

        int spaceIndex =
            trimmed.IndexOf(' ');

        string verb =
            (spaceIndex < 0
                ? trimmed
                : trimmed[..spaceIndex])
            .ToUpperInvariant();

        string argument =
            spaceIndex < 0
                ? string.Empty
                : trimmed[(spaceIndex + 1)..].Trim();

        return verb switch
        {
            "PING" =>
                "PONG",

            "STATUS" =>
                "SERVICE_RUNNING",

            "PROTOCOL_VERSION" =>
                ProtocolVersion,

            "ENGINE_STATUS" =>
                await _engineProcessManager.GetStatusAsync(
                    cancellationToken),

            "ENGINE_CAN_START" =>
                _engineProcessManager.GetCanStart(),

            "ENGINE_HEALTH" =>
                await _engineProcessManager.GetHealthAsync(
                    cancellationToken),

            "ENGINE_HASH_STATUS" =>
                _engineProcessManager.GetHashStatus(),

            "ENGINE_UNSAFE_APPROVAL_STATUS" =>
                _engineProcessManager.GetUnsafeApprovalStatus(),

            "ENGINE_DETAILS" =>
                await _engineProcessManager.GetDetailsAsync(
                    cancellationToken),

            "ENGINE_CONFIG" =>
                _engineProcessManager.GetConfig(),

            "ENGINE_CONFIG_JSON" =>
                _engineProcessManager.GetConfigJson(),

            "START_ENGINE" =>
                await HandleStartEngineAsync(
                    cancellationToken),

            "STOP_ENGINE" =>
                await HandleStopEngineAsync(
                    cancellationToken),

            "START_ENGINE_APPS" =>
                await HandleStartEngineAppsAsync(
                    argument,
                    cancellationToken),

            "GET_APP_STRATEGY_CATALOG" =>
                HandleGetAppStrategyCatalog(
                    argument),

            _ =>
                "ERROR UNKNOWN_COMMAND"
        };
    }

    private async Task<string> HandleStartEngineAsync(
        CancellationToken cancellationToken)
    {
        string response =
            await _engineProcessManager.StartAsync(
                cancellationToken);

        _logger.Info(
            stage: "EngineProcessCommand",
            result: response);

        return response;
    }

    private async Task<string> HandleStopEngineAsync(
        CancellationToken cancellationToken)
    {
        string response =
            await _engineProcessManager.StopAsync(
                cancellationToken);

        _logger.Info(
            stage: "EngineProcessCommand",
            result: response);

        return response;
    }

    private async Task<string> HandleStartEngineAppsAsync(
        string argument,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<(string AppId, int StrategyIndex)> selections;

        try
        {
            selections =
                ParseAppSelections(
                    argument);
        }
        catch (Exception exception)
        {
            // Opaque code to the caller; details go to the server log only, so
            // internal paths / hashes are not disclosed over the pipe.
            _logger.Info(
                stage: "EngineComposeParse",
                result: $"Rejected; {exception.GetType().Name}: {exception.Message}");

            return "ENGINE_COMPOSE_ERROR PARSE";
        }

        EngineOptions composedOptions;

        try
        {
            string composedProfilePath =
                ProfileComposer.Compose(
                    selections);

            composedOptions =
                EngineProfileLoader.LoadAndValidateExplicit(
                    composedProfilePath);
        }
        catch (Exception exception)
        {
            _logger.Error(
                stage: "EngineProfileCompose",
                exception: exception);

            return "ENGINE_COMPOSE_ERROR";
        }

        string stopResult =
            await _engineProcessManager.StopAsync(
                cancellationToken);

        _logger.Info(
            stage: "EngineProcessCommand",
            result: $"StopBeforeApply; {stopResult}");

        string applyResult =
            await _engineProcessManager.ApplyProfileAsync(
                composedOptions,
                cancellationToken);

        _logger.Info(
            stage: "EngineProcessCommand",
            result: applyResult);

        if (!applyResult.Equals(
                "ENGINE_PROFILE_APPLIED",
                StringComparison.OrdinalIgnoreCase))
        {
            return applyResult;
        }

        return await HandleStartEngineAsync(
            cancellationToken);
    }

    private string HandleGetAppStrategyCatalog(
        string appId)
    {
        string normalizedAppId =
            appId.Trim().ToLowerInvariant();

        if (!AppIdPattern.IsMatch(normalizedAppId))
        {
            return "ERROR INVALID_APP_ID";
        }

        try
        {
            AppStrategyCatalog catalog =
                ProfileComposer.LoadCatalog(
                    normalizedAppId);

            return JsonSerializer.Serialize(
                catalog);
        }
        catch (Exception exception)
        {
            _logger.Info(
                stage: "EngineCatalogRead",
                result: $"Failed; {exception.GetType().Name}: {exception.Message}");

            return "ERROR CATALOG";
        }
    }

    private static IReadOnlyList<(string AppId, int StrategyIndex)> ParseAppSelections(
        string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            throw new ArgumentException(
                "No app selections provided.");
        }

        var selections =
            new List<(string, int)>();

        foreach (string entry in argument.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            string[] parts =
                entry.Split(':', 2);

            if (parts.Length != 2 ||
                !int.TryParse(parts[1], out int strategyIndex))
            {
                throw new ArgumentException(
                    $"Invalid app selection entry: '{entry}'. Expected format 'appid:index'.");
            }

            string appId =
                parts[0].Trim().ToLowerInvariant();

            if (!AppIdPattern.IsMatch(appId))
            {
                throw new ArgumentException(
                    $"Invalid app id in selection entry: '{entry}'.");
            }

            if (strategyIndex < 0)
            {
                throw new ArgumentException(
                    $"Negative strategy index in selection entry: '{entry}'.");
            }

            selections.Add(
                (appId, strategyIndex));
        }

        return selections;
    }

    private static async Task<string?> ReadCommandLineAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var builder =
            new StringBuilder();

        var buffer =
            new char[256];

        while (true)
        {
            int read =
                await reader.ReadAsync(
                    buffer,
                    cancellationToken);

            if (read == 0)
            {
                // End of stream before a newline.
                break;
            }

            for (int i = 0; i < read; i++)
            {
                char current = buffer[i];

                if (current == '\n')
                {
                    return builder.ToString();
                }

                if (current == '\r')
                {
                    continue;
                }

                builder.Append(current);

                if (builder.Length > MaxCommandLength)
                {
                    throw new InvalidDataException(
                        "Command exceeds the maximum allowed length.");
                }
            }
        }

        return builder.Length == 0
            ? null
            : builder.ToString();
    }
}







