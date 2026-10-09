using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;

/// <summary>
///     Background worker for the second of the three time-driven policies of this bounded context.
/// </summary>
/// <remarks>
///     Implements the policy "When Consistency Alert Sustained Three Weeks" (Monitoring and
///     Adherence, Subflow 5.8), which issues Escalate To Practitioner. Nobody triggers it: the thing
///     it waits for is three weeks passing, and no event announces that.
///     Business rules: Patient Prompt Required Before Escalation, Three Weeks In Alert Required and
///     Escalation Notifies Never Modifies The Plan (Subflow 5.8), plus invariants 2 and 3. The query
///     below already excludes any index that has not been shown to its patient, the command checks
///     the same thing again, and the aggregate refuses a third time. What comes out the far side is
///     an item in a human inbox; nothing in this worker can reach a plan.
///     TODO: UNCALIBRATED - the threshold of the Consistency Index is the largest technical risk in
///     the project (technical document section 10.2, risk 1). Parameter:
///     Monitoring:ConsistencyAlertThreshold, shipped as 1.5 kilograms per week, derived from the
///     largest deficit the clinical context admits rather than validated against any cohort. At zero
///     no index ever leaves Normal, so this worker runs, finds nothing and escalates nobody; that
///     dormant state remains the safe one, and it is what an absent key falls back to.
///     Guards, in the order the platform requires them:
///     1. the whole cycle body is wrapped in try/catch, so a failing cycle never brings the host down;
///     2. scoped services are resolved from a scope of its own through IServiceScopeFactory;
///     3. the stopping token is propagated into every call;
///     4. the cycle is idempotent, because the aggregate records the escalation date and an index
///     that already carries one is excluded by the query and refused by the aggregate;
///     5. EF migrations have already run in the composition root before the host starts.
/// </remarks>
public class ConsistencyEscalationHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ConsistencyEscalationHostedService> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = configuration.GetValue<int?>("Scheduling:ConsistencyEscalationIntervalHours") ?? 24;
        var interval = TimeSpan.FromHours(Math.Max(1, hours));

        logger.LogInformation("Consistency escalation policy running every {Interval}", interval);

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
            var queryService = scope.ServiceProvider.GetRequiredService<IConsistencyIndexQueryService>();
            var commandService = scope.ServiceProvider
                .GetRequiredService<IConsistencyIndexCommandService>();

            // Business rule: Three Weeks In Alert Required (Monitoring and Adherence, Subflow 5.8)
            var weeks = configuration.GetValue<int?>("Monitoring:ConsistencyEscalationWeeks") ?? 3;
            var alertSinceBefore = DateTimeOffset.UtcNow.AddDays(-7 * Math.Max(1, weeks));

            var due = await queryService.Handle(
                new GetEscalatableConsistencyIndicesQuery(alertSinceBefore, BatchSize), stoppingToken);

            // MA-7: the patient saw the prompt at least N days ago; the command checks it again.
            var acknowledgementDays = ConsistencyIndexCommandService.AcknowledgementDays(configuration);
            var now = DateTimeOffset.UtcNow;

            foreach (var index in due.Where(i => i.ShownToPatientAtLeast(acknowledgementDays, now)))
            {
                if (stoppingToken.IsCancellationRequested) return;

                // Guard 4: escalating an already escalated index is a no-op that publishes nothing.
                var result = await commandService.Handle(
                    new EscalateToPractitionerCommand(index.PatientId), stoppingToken);

                if (result.IsFailure)
                    logger.LogWarning("Could not escalate the consistency alert of patient {PatientId}",
                        index.PatientId);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            // Guard 1: the host stays up whatever happens in here.
            logger.LogError(ex, "The consistency escalation cycle did not complete");
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
