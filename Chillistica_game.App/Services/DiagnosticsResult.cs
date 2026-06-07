namespace Chillistica_game.App.Services;

public sealed class DiagnosticsResult
{
    public required string ServiceName { get; init; }

    public required string Host { get; init; }

    public int Port { get; init; }

    public required string Mode { get; init; }

    public bool DnsSuccess { get; init; }

    public bool TcpSuccess { get; init; }

    public bool HttpsSuccess { get; init; }

    public bool HttpsSkipped { get; init; }

    public long TcpLatencyMs { get; init; }

    public long HttpsLatencyMs { get; init; }

    public string? Error { get; init; }

    public bool IsSuccessful =>
        DnsSuccess &&
        TcpSuccess &&
        (HttpsSuccess || HttpsSkipped);

    public string ToDisplayText()
    {
        string endpoint = Port == 443
            ? Host
            : $"{Host}:{Port}";

        string httpsText = HttpsSkipped
            ? "не проверялся"
            : HttpsSuccess
                ? $"{HttpsLatencyMs} мс"
                : "нет";

        if (IsSuccessful)
        {
            return
                $"{ServiceName} [{Mode}]: работает\n" +
                $"Узел: {endpoint}\n" +
                $"DNS: да\n" +
                $"TCP {Port}: {TcpLatencyMs} мс\n" +
                $"HTTPS: {httpsText}";
        }

        return
            $"{ServiceName} [{Mode}]: обнаружена проблема\n" +
            $"Узел: {endpoint}\n" +
            $"DNS: {(DnsSuccess ? "да" : "нет")}\n" +
            $"TCP {Port}: {(TcpSuccess ? $"{TcpLatencyMs} мс" : "нет")}\n" +
            $"HTTPS: {httpsText}\n" +
            $"Ошибка: {Error ?? "неизвестная ошибка"}";
    }
}
