using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Services;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;

/// <summary>
///     Runs once at start-up and re-evaluates the recent logged days of every open window.
/// </summary>
/// <remarks>
///     The first day of a targets snapshot used to be its UTC date, so targets published in the evening in Lima
///     were not in force on the patient's own day: those days were never evaluated, or were later filled in as
///     unlogged although the diary held entries. This worker repairs them through Re Evaluate Logged Days, which
///     only touches days with entries and changes nothing in a day that is already right, so running it on every
///     start is harmless. <c>Scheduling:ClinicalDayRepairLookbackDays</c> (default 14; 0 turns it off) bounds how
///     far back it looks.
///     Guards, as in the other workers: the body is wrapped in try/catch, it resolves its services from a scope
///     of its own, and the stopping token reaches every call.
/// </remarks>
public class ClinicalDayRepairHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IFollowUpCalendar calendar,
    ILogger<ClinicalDayRepairHostedService> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lookbackDays = configuration.GetValue<int?>("Scheduling:ClinicalDayRepairLookbackDays") ?? 14;
        if (lookbackDays <= 0) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var queryService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowQueryService>();
            var commandService = scope.ServiceProvider
                .GetRequiredService<IEvaluationWindowCommandService>();

            var windows = await queryService.Handle(new GetOpenEvaluationWindowsQuery(BatchSize),
                stoppingToken);

            var today = calendar.LocalDateOf(DateTimeOffset.UtcNow);
            var from = today.AddDays(1 - lookbackDays);
            var repaired = 0;

            foreach (var window in windows)
            {
                if (stoppingToken.IsCancellationRequested) return;

                var result = await commandService.Handle(
                    new ReEvaluateLoggedDaysCommand(window.PatientId, from, today), stoppingToken);

                repaired += result.Fold(days => days, _ =>
                {
                    logger.LogWarning("Could not repair the logged days of patient {PatientId}", window.PatientId);
                    return 0;
                });
            }

            logger.LogInformation("Clinical day repair re-evaluated {Days} day(s) since {From}", repaired, from);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The clinical day repair did not complete");
        }
    }
}
