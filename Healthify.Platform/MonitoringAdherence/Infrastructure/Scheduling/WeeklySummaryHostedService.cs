using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.MonitoringAdherence.Application.Internal;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;

/// <summary>
///     IA-2. Policy "When The Week Ends": generates the weekly summary of every patient with an open window, on the
///     schedule of <c>Ai:Features:WeeklySummary:Cron</c> (default <c>0 6 * * 1</c>, Mondays at 06:00) on the clinical
///     clock (<c>MonitoringAdherence:ClinicalTimeZone</c>, default America/Lima).
/// </summary>
/// <remarks>
///     Each run summarizes the most recent week that is over (Monday to Sunday). The command decides, patient by
///     patient: AI off, no consent or the preference off → nothing is read and nothing generated; fewer than three
///     logged days → nothing (PT13.2.V); a week already summarized → kept as it is. So a run is idempotent, and at
///     start-up the run of the last occurrence (within a week) is repeated in case the host was down at its hour.
///     Each cycle also deletes the summaries older than <c>Ai:RetentionDays</c> (§12-#14).
///     Guards: the cycle never throws; one scope per patient, so one failure does not poison the others; the
///     stopping token reaches every call.
/// </remarks>
public class WeeklySummaryHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IAiSettings aiSettings,
    TimeProvider timeProvider,
    ILogger<WeeklySummaryHostedService> logger) : BackgroundService
{
    public const string DefaultCron = "0 6 * * 1";
    private const int BatchSize = 5_000;

    /// <summary>The Monday of the most recent week that is over on <paramref name="localDate" />.</summary>
    public static DateOnly WeekToSummarize(DateOnly localDate)
    {
        return WeeklySummary.WeekStartOf(localDate.AddDays(-7));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = Schedule();
        var timeZone = TimeZone();
        logger.LogInformation("Weekly summary policy scheduled '{Cron}' in {TimeZone}", schedule.Expression,
            timeZone.Id);

        var previous = schedule.PreviousAtOrBefore(timeProvider.GetUtcNow(), TimeSpan.FromDays(7), timeZone);
        if (previous is { } missed) await RunCycleAsync(LocalDate(missed, timeZone), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = schedule.NextAfter(timeProvider.GetUtcNow(), timeZone);
            if (next is null)
            {
                logger.LogWarning("The weekly summary schedule '{Cron}' never runs", schedule.Expression);
                return;
            }

            try
            {
                var delay = next.Value - timeProvider.GetUtcNow();
                if (delay > TimeSpan.Zero) await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunCycleAsync(LocalDate(next.Value, timeZone), stoppingToken);
        }
    }

    /// <summary>One run: the week before <paramref name="runDate" /> for every open window, then the retention.</summary>
    /// <returns>Summaries generated in this run.</returns>
    public async Task<int> RunCycleAsync(DateOnly runDate, CancellationToken stoppingToken)
    {
        var generated = 0;
        try
        {
            // Nothing to do while the feature is off: no window is read.
            if (aiSettings.IsEnabled(AiFeature.WeeklySummary))
                generated = await GenerateAsync(WeekToSummarize(runDate), stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IWeeklySummaryCommandService>();
            var purged = await commandService.Handle(
                new PurgeExpiredWeeklySummariesCommand(timeProvider.GetUtcNow() - aiSettings.Retention),
                stoppingToken);
            if (purged is Result<int, MonitoringError>.Success { Value: > 0 } success)
                logger.LogInformation("Deleted {Count} weekly summaries past their retention", success.Value);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The weekly summary cycle did not complete");
        }

        return generated;
    }

    private async Task<int> GenerateAsync(DateOnly weekStart, CancellationToken stoppingToken)
    {
        List<int> patients;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var queryService = scope.ServiceProvider.GetRequiredService<IEvaluationWindowQueryService>();
            patients = (await queryService.Handle(new GetOpenEvaluationWindowsQuery(BatchSize), stoppingToken))
                .Select(w => w.PatientId).Distinct().ToList();
        }

        if (patients.Count >= BatchSize)
            logger.LogWarning("Weekly summary run reached its batch of {BatchSize} patients; the rest wait",
                BatchSize);

        var generated = 0;
        foreach (var patientId in patients)
        {
            if (stoppingToken.IsCancellationRequested) break;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var commandService = scope.ServiceProvider.GetRequiredService<IWeeklySummaryCommandService>();
                var result = await commandService.Handle(new GenerateWeeklySummaryCommand(patientId, weekStart),
                    stoppingToken);

                switch (result)
                {
                    case Result<WeeklySummary, MonitoringAiFailure>.Success:
                        generated++;
                        break;
                    // Not having consent, the preference or enough days is ordinary: not a warning.
                    case Result<WeeklySummary, MonitoringAiFailure>.Failure { Error.AiError: AiError.AiConsentRequired or AiError.AiFeatureDisabled }:
                    case Result<WeeklySummary, MonitoringAiFailure>.Failure { Error.Error: MonitoringError.NotEnoughData or MonitoringError.ActiveCareLinkRequired }:
                        break;
                    case Result<WeeklySummary, MonitoringAiFailure>.Failure failure:
                        logger.LogWarning("Weekly summary of patient {PatientId} not generated: {Reason}", patientId,
                            failure.Error);
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Weekly summary of patient {PatientId} failed", patientId);
            }
        }

        logger.LogInformation("Weekly summary run for the week of {WeekStart}: {Generated} of {Patients} patients",
            weekStart, generated, patients.Count);
        return generated;
    }

    private CronSchedule Schedule()
    {
        var configured = configuration["Ai:Features:WeeklySummary:Cron"];
        try
        {
            return CronSchedule.Parse(string.IsNullOrWhiteSpace(configured) ? DefaultCron : configured);
        }
        catch (FormatException ex)
        {
            logger.LogWarning("Ai:Features:WeeklySummary:Cron is invalid ({Message}); using '{Default}'", ex.Message,
                DefaultCron);
            return CronSchedule.Parse(DefaultCron);
        }
    }

    private TimeZoneInfo TimeZone()
    {
        var configured = configuration["MonitoringAdherence:ClinicalTimeZone"];
        foreach (var id in new[] { configured, ClinicalTimeZoneFollowUpCalendar.DefaultTimeZoneId })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                logger.LogWarning("Time zone {TimeZone} is not available for the weekly summary", id);
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static DateOnly LocalDate(DateTimeOffset moment, TimeZoneInfo timeZone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, timeZone).DateTime);
    }
}
