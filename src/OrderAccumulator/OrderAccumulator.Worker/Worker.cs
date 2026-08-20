using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderAccumulator.Infrastructure.Fix;

namespace OrderAccumulator.Worker;

public class Worker : BackgroundService
{
    private readonly FixAccumulator _fixAccumulator;
    private readonly ILogger<Worker> _logger;

    public Worker(FixAccumulator fixAccumulator, ILogger<Worker> logger)
    {
        _fixAccumulator = fixAccumulator ?? throw new ArgumentNullException(nameof(fixAccumulator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("OrderAccumulator Worker starting...");
            _fixAccumulator.Start();

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker encountered an error");
            throw;
        }
        finally
        {
            _fixAccumulator.Stop();
            _logger.LogInformation("OrderAccumulator Worker stopped");
        }
    }
}
