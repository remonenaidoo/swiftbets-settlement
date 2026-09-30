using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftBets.Settlement.Application.Handlers;

namespace SwiftBets.Settlement.Infrastructure.Workers;

public sealed partial class ReconcilerWorker(IServiceScopeFactory scopes, IOptions<ReconcilerOptions> options, TimeProvider time, ILogger<ReconcilerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.IntervalSeconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var repaired = await scope.ServiceProvider.GetRequiredService<ReconcileHandler>()
                    .ReconcileAsync(TimeSpan.FromSeconds(options.Value.StuckAfterSeconds), stoppingToken);
                if (repaired > 0)
                {
                    LogRepaired(repaired);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconciler repaired {Count} stuck coupons")]
    private partial void LogRepaired(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciler pass failed")]
    private partial void LogFailed(Exception exception);
}
