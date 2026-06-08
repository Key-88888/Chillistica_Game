using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Chillistica_game.App.Services;

public sealed class NamedPipeClientService
{
    public const string PipeName =
        "Chillistica_game.Control";

    public const string SupportedProtocolVersion =
        "1";

    public async Task<string> SendCommandAsync(
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException(
                "Command cannot be empty.",
                nameof(command));
        }

        using CancellationTokenSource timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCts.CancelAfter(timeout);

        await using var pipe =
            new NamedPipeClientStream(
                serverName: ".",
                pipeName: PipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

        await pipe.ConnectAsync(
            timeoutCts.Token);

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

        using var reader =
            new StreamReader(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);

        await writer.WriteLineAsync(
            command.Trim());

        string? response =
            await reader.ReadLineAsync(
                timeoutCts.Token);

        return response ??
            "ERROR EMPTY_RESPONSE";
    }

    public async Task<bool> IsServiceAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        string response =
            await SendCommandSafelyAsync(
                command: "PING",
                unavailableResponse: "SERVICE_UNAVAILABLE",
                cancellationToken: cancellationToken);

        return string.Equals(
            response,
            "PONG",
            StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> GetServiceStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandSafelyAsync(
            command: "STATUS",
            unavailableResponse: "SERVICE_UNAVAILABLE",
            cancellationToken: cancellationToken);
    }

    public Task<string> GetProtocolVersionAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandSafelyAsync(
            command: "PROTOCOL_VERSION",
            unavailableResponse: "PROTOCOL_UNAVAILABLE",
            cancellationToken: cancellationToken);
    }

    public Task<string> GetEngineStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandSafelyAsync(
            command: "ENGINE_STATUS",
            unavailableResponse: "ENGINE_UNAVAILABLE",
            cancellationToken: cancellationToken);
    }

    public Task<string> GetEngineCanStartAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandSafelyAsync(
            command: "ENGINE_CAN_START",
            unavailableResponse: "ENGINE_UNAVAILABLE",
            cancellationToken: cancellationToken);
    }
    public Task<string> GetEngineHealthAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandSafelyAsync(
            command: "ENGINE_HEALTH",
            unavailableResponse: "ENGINE_HEALTH_UNAVAILABLE",
            cancellationToken: cancellationToken);
    }
    public Task<string> GetEngineConfigAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandSafelyAsync(
            command: "ENGINE_CONFIG",
            unavailableResponse: "ENGINE_CONFIG_UNAVAILABLE",
            cancellationToken: cancellationToken);
    }

    public Task<string> GetEngineConfigJsonAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandSafelyAsync(
            command: "ENGINE_CONFIG_JSON",
            unavailableResponse: "ENGINE_CONFIG_JSON_UNAVAILABLE",
            cancellationToken: cancellationToken);
    }

    public Task<string> StartEngineAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandSafelyAsync(
            command: "START_ENGINE",
            unavailableResponse: "ENGINE_UNAVAILABLE",
            cancellationToken: cancellationToken);
    }

    public Task<string> StopEngineAsync(
        CancellationToken cancellationToken = default)
    {
        return SendCommandSafelyAsync(
            command: "STOP_ENGINE",
            unavailableResponse: "ENGINE_UNAVAILABLE",
            cancellationToken: cancellationToken);
    }

    private async Task<string> SendCommandSafelyAsync(
        string command,
        string unavailableResponse,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendCommandAsync(
                command: command,
                timeout: TimeSpan.FromSeconds(3),
                cancellationToken: cancellationToken);
        }
        catch (
            Exception exception)
            when (
                exception is TimeoutException or
                OperationCanceledException or
                IOException or
                UnauthorizedAccessException)
        {
            return unavailableResponse;
        }
    }
}





