using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;

/// <summary>
///     Background worker for the first of the three time-driven policies of this bounded context.
/// </summary>
/// <remarks>
///     Implements the policy "When N Days Without Diary Entry" (Monitoring and Adherence, Subflow
///     5.9), which issues Flag Logging Gap. Nobody triggers it: neither a user nor an event, only the
///     passage of time, which is why it is a hosted service and not an event handler. Silence is the
///     one thing no event can announce.
///     Business rules: Gap Is Not A Deviation, Gap Excluded From Deviation Calculation and Gap Never
///     Escalates (Subflow 5.9). This worker resolves one command service and it is the one that
///     writes to the window. It cannot reach the deviation aggregate or the consistency index, and
///     the event the command publishes has exactly one subscriber, which reminds the patient and
///     stops there. Absence of data is not evidence of non-compliance.
///     Guards, in the order the platform requires them:
///     1. the whole cycle body is wrapped in try/catch, so a failing cycle never brings the host down;
///     2. scoped services are resolved from a scope of its own through IServiceScopeFactory;
///     3. the stopping token is propagated into every call;
///     4. the cycle is idempotent, because the aggregate records the day a gap was announced for and
///     refuses to announce the same one twice;
///     5. EF migrations have already run in the composition root before the host starts.
/// </remarks>
public class LoggingGapDetectionHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<LoggingGapDetectionHostedService> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = configuration.GetValue<int?>("Scheduling:LoggingGapIntervalHours") ?? 12;
        var interval = TimeSpan.FromHours(Math.Max(1, hours));

        logger.LogInformation("Logging gap detection policy running every {Interval}", interval);

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
            var queryService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowQueryService>();
            var commandService = scope.ServiceProvider
                .GetRequiredService<IEvaluationWindowCommandService>();

            var windows = await queryService.Handle(new GetOpenEvaluationWindowsQuery(BatchSize),
                stoppingToken);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            foreach (var window in windows)
            {
                if (stoppingToken.IsCancellationRequested) return;

                // Guard 4: the command decides whether the gap is old enough and whether it has
                // already been announced, so running this cycle twice changes nothing.
                var result = await commandService.Handle(
                    new FlagLoggingGapCommand(window.PatientId, today), stoppingToken);

                if (result.IsFailure)
                    logger.LogWarning("Could not check the logging gap of patient {PatientId}",
                        window.PatientId);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            // Guard 1: the host stays up whatever happens in here.
            logger.LogError(ex, "The logging gap detection cycle did not complete");
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
