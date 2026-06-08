using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Chillistica_game.Service;

public sealed class NamedPipeServer : BackgroundService
{
    public const string PipeName =
        "Chillistica_game.Control";

    public const string ProtocolVersion =
        "1";

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
            result: $"Listening; pipe={PipeName}");

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

        _logger.Info(
            stage: "NamedPipe",
            result: "Stopped");
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
                maxNumberOfServerInstances: 1,
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

        string? command =
            await reader.ReadLineAsync(
                stoppingToken);

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
        string normalized =
            command?
                .Trim()
                .ToUpperInvariant()
            ?? string.Empty;

        return normalized switch
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

            "" =>
                "ERROR EMPTY_COMMAND",

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
}







