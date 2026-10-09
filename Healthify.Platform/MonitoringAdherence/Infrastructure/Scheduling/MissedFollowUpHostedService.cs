using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;

/// <summary>
///     Background worker for the third of the three time-driven policies of this bounded context.
/// </summary>
/// <remarks>
///     Implements the policy "When Scheduled Date Passed Without Visit" (Monitoring and Adherence,
///     Subflow 5.10), which issues Flag Missed Follow Up. Nobody triggers it: a visit that did not
///     happen produces no event, so only the clock can notice it.
///     Business rule: Missed Visit Does Not Close The Care Link (Subflow 5.10). This worker resolves
///     one command service, that service writes one state on one row, and the event it publishes has
///     no subscriber anywhere in the platform. Somebody who could not make it on Tuesday is still
///     somebody's patient on Wednesday.
///     Guards, in the order the platform requires them:
///     1. the whole cycle body is wrapped in try/catch, so a failing cycle never brings the host down;
///     2. scoped services are resolved from a scope of its own through IServiceScopeFactory;
///     3. the stopping token is propagated into every call;
///     4. the cycle is idempotent, because flagging an already missed visit is a no-op that publishes
///     no second event, and the query only returns visits still in the scheduled state;
///     5. EF migrations have already run in the composition root before the host starts.
/// </remarks>
public class MissedFollowUpHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MissedFollowUpHostedService> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = configuration.GetValue<int?>("Scheduling:MissedFollowUpIntervalHours") ?? 12;
        var interval = TimeSpan.FromHours(Math.Max(1, hours));

        logger.LogInformation("Missed follow up policy running every {Interval}", interval);

        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunCycleAsync(stoppingToken);
        } while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Guard 2: a scope of its own, so this worker never shares a request DbContext.
            await using var scope = scopeFactory.CreateAsyncScope();
            var queryService = scope.ServiceProvider
                .GetRequiredService<IScheduledFollowUpQueryService>();
            var commandService = scope.ServiceProvider
                .GetRequiredService<IScheduledFollowUpCommandService>();

            var overdue = await queryService.Handle(
                new GetOverdueScheduledFollowUpsQuery(DateTimeOffset.UtcNow, BatchSize), stoppingToken);

            foreach (var followUp in overdue)
            {
                if (stoppingToken.IsCancellationRequested) return;

                // Guard 4: flagging an already missed visit is a no-op that publishes nothing.
                var result = await commandService.Handle(
                    new FlagMissedFollowUpCommand(followUp.Id.Value), stoppingToken);

                if (result.IsFailure)
                    logger.LogWarning("Could not flag follow up {FollowUpId} as missed",
                        followUp.Id.Value);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            // Guard 1: the host stays up whatever happens in here.
            logger.LogError(ex, "The missed follow up cycle did not complete");
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
