namespace Upcheck.Api.Monitoring;

/// <summary>
/// Background service that wakes on a fixed tick and runs any checks that have come due.
/// All the real work lives in <see cref="MonitorRunner"/>; this is just the loop, with a fresh
/// DI scope per tick so each run gets its own DbContext.
/// </summary>
public sealed class SchedulerWorker(IServiceScopeFactory scopeFactory, ILogger<SchedulerWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Upcheck scheduler started (tick {Seconds}s)", Tick.TotalSeconds);
        using var timer = new PeriodicTimer(Tick);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<MonitorRunner>();
                await runner.RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler tick failed");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
