namespace Chillistica_game.Service;

public sealed class Worker : BackgroundService
{
    private readonly ServiceLogger _logger;

    public Worker(
        ServiceLogger logger)
    {
        _logger =
            logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.Info(
            stage: "Service",
            result: "Started");

        try
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Нормальная остановка службы.
        }
        catch (Exception exception)
        {
            _logger.Error(
                stage: "ServiceRuntime",
                exception: exception);

            throw;
        }
        finally
        {
            _logger.Info(
                stage: "Service",
                result: "Stopped");
        }
    }
}
