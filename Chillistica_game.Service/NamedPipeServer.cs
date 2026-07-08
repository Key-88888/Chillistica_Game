using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace Chillistica_game.Service;

public sealed class NamedPipeServer : BackgroundService
{
    public const string PipeName =
        "Chillistica_game.Control";

    public const string ProtocolVersion =
        "2";

    private const int MaxConcurrentConnections = 4;

    private static readonly TimeSpan IdleReadTimeout =
        TimeSpan.FromSeconds(5);

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

        await using NamedPipeServerStream pipe =
            NamedPipeServerStreamAcl.Create(
                pipeName: PipeName,
                direction: PipeDirection.InOut,
                maxNumberOfServerInstances: MaxConcurrentConnections,
                transmissionMode: PipeTransmissionMode.Byte,
                options: PipeOptions.Asynchronous,
                inBufferSize: 4096,
                outBufferSize: 4096,
                pipeSecurity: pipeSecurity);

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
                    await reader.ReadLineAsync(
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

    private static PipeSecurity CreatePipeSecurity()
    {
        var security =
            new PipeSecurity();

        var localSystemSid =
            new SecurityIdentifier(
                WellKnownSidType.LocalSystemSid,
                domainSid: null);

        var localUsersSid =
            new SecurityIdentifier(
                WellKnownSidType.BuiltinUsersSid,
                domainSid: null);

        security.AddAccessRule(
            new PipeAccessRule(
                localSystemSid,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

        security.AddAccessRule(
            new PipeAccessRule(
                localUsersSid,
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
            return $"ENGINE_COMPOSE_ERROR PARSE: {exception.Message}";
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

            return $"ENGINE_COMPOSE_ERROR: {exception.Message}";
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
        if (string.IsNullOrWhiteSpace(appId))
        {
            return "ERROR EMPTY_APP_ID";
        }

        try
        {
            AppStrategyCatalog catalog =
                ProfileComposer.LoadCatalog(
                    appId.Trim());

            return JsonSerializer.Serialize(
                catalog);
        }
        catch (Exception exception)
        {
            return $"ERROR CATALOG: {exception.Message}";
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

            selections.Add(
                (parts[0].Trim().ToLowerInvariant(), strategyIndex));
        }

        return selections;
    }
}







