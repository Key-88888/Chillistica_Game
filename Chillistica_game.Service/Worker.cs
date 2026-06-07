namespace Chillistica_game.Service;

public sealed class Worker : BackgroundService
{
    private readonly ServiceLogger _logger;
    private readonly EngineProcessManager _engineProcessManager;

    public Worker(
        ServiceLogger logger,
        EngineProcessManager engineProcessManager)
    {
        _logger =
            logger;

        _engineProcessManager =
            engineProcessManager;
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
            try
            {
                string stopResult =
                    await _engineProcessManager.StopAsync();

                _logger.Info(
                    stage: "ServiceEngineCleanup",
                    result: stopResult);
            }
            catch (Exception exception)
            {
                _logger.Error(
                    stage: "ServiceEngineCleanup",
                    exception: exception);
            }

            _logger.Info(
                stage: "Service",
                result: "Stopped");
        }
    }
}
