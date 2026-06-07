namespace Chillistica_game.App.Services;

public sealed class DiagnosticsTarget
{
    public required string ServiceName { get; init; }

    public required string Host { get; init; }

    public int Port { get; init; } = 443;

    public bool CheckHttps { get; init; } = true;

    public string DisplayEndpoint =>
        Port == 443
            ? Host
            : $"{Host}:{Port}";
}
