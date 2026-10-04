using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Scheduling;

/// <summary>
///     NC-10 (DECISIÓN §12-#12). Background worker of the scheduled rechecks: every 12 hours
///     (<c>Scheduling:ReviewItemRecheckIntervalHours</c>) it opens a <c>ScheduledRecheck</c> review item for each
///     adjustment whose <c>ResolvedAt + RecheckAfterDays</c> arrived.
/// </summary>
/// <remarks>
///     "Las señales llegan a una bandeja donde decide una persona": the recheck is one more item in the inbox. It
///     resolves the review item command service only and can never modify a plan.
///     Guards, as the other workers: the cycle in try/catch, a scope of its own, the stopping token propagated, and an
///     idempotent cycle (an issued recheck is marked and never opened again).
/// </remarks>
public class ReviewItemRecheckHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ReviewItemRecheckHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = configuration.GetValue<int?>("Scheduling:ReviewItemRecheckIntervalHours") ?? 12;
        var interval = TimeSpan.FromHours(Math.Max(1, hours));

        logger.LogInformation("Review item recheck policy running every {Interval}", interval);

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
            var commandService = scope.ServiceProvider.GetRequiredService<IReviewItemCommandService>();

            var result = await commandService.Handle(new OpenDueRechecksCommand(timeProvider.GetUtcNow()),
                stoppingToken);
            if (result.IsFailure) logger.LogWarning("The review item recheck cycle did not complete");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The review item recheck cycle did not complete");
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
