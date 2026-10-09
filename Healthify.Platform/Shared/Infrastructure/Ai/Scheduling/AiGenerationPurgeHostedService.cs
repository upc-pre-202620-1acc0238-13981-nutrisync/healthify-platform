using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.Shared.Infrastructure.Ai.Scheduling;

/// <summary>
///     IA-0, retention (§12-#14). Deletes the <c>ai_generations</c> rows whose <c>expires_at</c> has passed. Runs even
///     with AI off, so rows written while it was on still expire.
/// </summary>
/// <remarks>
///     Same guards as the other workers: the cycle body never brings the host down, the log is resolved from a
///     scope of its own, the stopping token reaches every call, and the cycle is idempotent (deleting what already
///     expired twice deletes nothing the second time).
/// </remarks>
public class AiGenerationPurgeHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AiGenerationPurgeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = configuration.GetValue<int?>("Scheduling:AiGenerationPurgeIntervalMinutes") ?? 360;
        var interval = TimeSpan.FromMinutes(Math.Max(1, minutes));

        logger.LogInformation("AI generation retention purge running every {Interval}", interval);

        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunCycleAsync(stoppingToken);
        } while (await SafeWaitAsync(timer, stoppingToken));
    }

    public async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var log = scope.ServiceProvider.GetRequiredService<IAiGenerationLog>();
            var purged = await log.PurgeExpiredAsync(timeProvider.GetUtcNow(), stoppingToken);
            if (purged > 0) logger.LogInformation("Purged {Count} expired AI generations", purged);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The AI generation purge cycle did not complete");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
