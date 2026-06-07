namespace Chillistica_game.App.Services;

public sealed class DiagnosticsResult
{
    public required string ServiceName { get; init; }

    public required string Host { get; init; }

    public bool DnsSuccess { get; init; }

    public bool TcpSuccess { get; init; }

    public bool HttpsSuccess { get; init; }

    public long TcpLatencyMs { get; init; }

    public long HttpsLatencyMs { get; init; }

    public string? Error { get; init; }

    public bool IsSuccessful =>
        DnsSuccess &&
        TcpSuccess &&
        HttpsSuccess;

    public string ToDisplayText()
    {
        if (IsSuccessful)
        {
            return
                $"{ServiceName}: работает\n" +
                $"DNS: да\n" +
                $"TCP 443: {TcpLatencyMs} мс\n" +
                $"HTTPS: {HttpsLatencyMs} мс";
        }

        return
            $"{ServiceName}: обнаружена проблема\n" +
            $"DNS: {(DnsSuccess ? "да" : "нет")}\n" +
            $"TCP 443: {(TcpSuccess ? $"{TcpLatencyMs} мс" : "нет")}\n" +
            $"HTTPS: {(HttpsSuccess ? $"{HttpsLatencyMs} мс" : "нет")}\n" +
            $"Ошибка: {Error ?? "неизвестная ошибка"}";
    }
}
