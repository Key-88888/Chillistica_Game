using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Chillistica_game.App.Services;

public sealed class DiagnosticsService
{
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;

    public DiagnosticsService()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = true,
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli,

            ConnectTimeout = TcpTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = HttpTimeout
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Chillistica_game/0.1");
    }

    public async Task<DiagnosticsResult> CheckTargetAsync(
        DiagnosticsTarget target,
        CancellationToken cancellationToken = default)
    {
        bool dnsSuccess = false;
        bool tcpSuccess = false;
        bool httpsSuccess = false;
        bool httpsSkipped = !target.CheckHttps;

        long tcpLatencyMs = 0;
        long httpsLatencyMs = 0;

        string? error = null;

        try
        {
            using CancellationTokenSource dnsCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            dnsCts.CancelAfter(DnsTimeout);

            IPAddress[] addresses =
                await Dns.GetHostAddressesAsync(
                    target.Host,
                    dnsCts.Token);

            dnsSuccess = addresses.Length > 0;

            if (!dnsSuccess)
            {
                error = "DNS не вернул IP-адрес";
            }
        }
        catch (Exception ex)
        {
            error = $"DNS: {GetSafeError(ex)}";
        }

        if (dnsSuccess)
        {
            try
            {
                using TcpClient tcpClient = new();

                Stopwatch stopwatch = Stopwatch.StartNew();

                using CancellationTokenSource tcpCts =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                tcpCts.CancelAfter(TcpTimeout);

                await tcpClient.ConnectAsync(
                    target.Host,
                    target.Port,
                    tcpCts.Token);

                stopwatch.Stop();

                tcpLatencyMs = stopwatch.ElapsedMilliseconds;
                tcpSuccess = tcpClient.Connected;
            }
            catch (Exception ex)
            {
                error = $"TCP {target.Port}: {GetSafeError(ex)}";
            }
        }

        if (tcpSuccess && target.CheckHttps)
        {
            try
            {
                using HttpRequestMessage request =
                    new(
                        HttpMethod.Get,
                        $"https://{target.Host}/");

                Stopwatch stopwatch = Stopwatch.StartNew();

                using HttpResponseMessage response =
                    await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                stopwatch.Stop();

                httpsLatencyMs = stopwatch.ElapsedMilliseconds;

                httpsSuccess = (int)response.StatusCode < 500;

                if (!httpsSuccess)
                {
                    error =
                        $"HTTPS вернул код {(int)response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                error = $"HTTPS: {GetSafeError(ex)}";
            }
        }

        return new DiagnosticsResult
        {
            ServiceName = target.ServiceName,
            Host = target.Host,
            Port = target.Port,
            DnsSuccess = dnsSuccess,
            TcpSuccess = tcpSuccess,
            HttpsSuccess = httpsSuccess,
            HttpsSkipped = httpsSkipped,
            TcpLatencyMs = tcpLatencyMs,
            HttpsLatencyMs = httpsLatencyMs,
            Error = error
        };
    }

    private static string GetSafeError(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException =>
                "превышено время ожидания",

            SocketException socketException =>
                socketException.SocketErrorCode.ToString(),

            HttpRequestException httpException =>
                httpException.Message,

            _ =>
                exception.Message
        };
    }
}
