using System.IO.Pipes;
using System.Text;

namespace Chillistica_game.Service;

public sealed class NamedPipeServer : BackgroundService
{
    public const string PipeName =
        "Chillistica_game.Control";

    private readonly ServiceLogger _logger;

    public NamedPipeServer(
        ServiceLogger logger)
    {
        _logger = logger;
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
        await using var pipe =
            new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

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
            HandleCommand(command);

        await writer.WriteLineAsync(
            response);

        _logger.Info(
            stage: "NamedPipeCommand",
            result:
                $"Command={command ?? "<null>"}; Response={response}");
    }

    private static string HandleCommand(
        string? command)
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

            "" =>
                "ERROR EMPTY_COMMAND",

            _ =>
                "ERROR UNKNOWN_COMMAND"
        };
    }
}
