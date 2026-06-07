using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Chillistica_game.App.Services;

public sealed class NamedPipeClientService
{
    public const string PipeName =
        "Chillistica_game.Control";

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
        try
        {
            string response =
                await SendCommandAsync(
                    command: "PING",
                    timeout: TimeSpan.FromSeconds(2),
                    cancellationToken: cancellationToken);

            return string.Equals(
                response,
                "PONG",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (
            Exception exception)
            when (
                exception is TimeoutException or
                OperationCanceledException or
                IOException)
        {
            return false;
        }
    }

    public async Task<string> GetServiceStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendCommandAsync(
                command: "STATUS",
                timeout: TimeSpan.FromSeconds(2),
                cancellationToken: cancellationToken);
        }
        catch (
            Exception exception)
            when (
                exception is TimeoutException or
                OperationCanceledException or
                IOException)
        {
            return "SERVICE_UNAVAILABLE";
        }
    }
}


