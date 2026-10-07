using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Services;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>
///     IA-2/IA-4/IA-5. Reads the deterministic facts of a period: the evaluated days of this context (MA-6) and, by
///     the Intake facade, the moments of the diary and the home weight trend (IN-5) over exactly the same days. One
///     read per source, never one per day.
/// </summary>
public class MonitoringFactsReader(
    IEvaluationWindowQueryService evaluationWindowQueryService,
    IIntakeContextFacade intakeContextFacade,
    IFollowUpCalendar calendar,
    TimeProvider timeProvider)
{
    /// <summary>"Today" on the clinical calendar (America/Lima by default).</summary>
    public DateOnly Today()
    {
        return DateOnly.FromDateTime(calendar.LocalDayOf(timeProvider.GetUtcNow()).Start.DateTime);
    }

    /// <param name="patientId">Whose period.</param>
    /// <param name="from">First day, included.</param>
    /// <param name="to">Last day, included.</param>
    /// <param name="includeWeight">Whether to read the IN-5 trend (not needed by every function).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<MonitoringPeriodFacts> ReadAsync(int patientId, DateOnly from, DateOnly to, bool includeWeight,
        CancellationToken cancellationToken = default)
    {
        var evaluated = await evaluationWindowQueryService.Handle(
            new GetDailyComplianceByPatientIdQuery(patientId, null), cancellationToken);

        var moments = await intakeContextFacade.GetDiaryEntryMoments(patientId, from, to, cancellationToken);
        var counted = moments
            .Where(m => m.IsCountedTowardsTargets)
            .GroupBy(m => m.Date)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DateTimeOffset>)g.Select(m => m.LocalTimestamp).ToList());

        // The trend of exactly this period (local days), not of the weeks before today: a summary generated late
        // still describes its own week.
        var weight = includeWeight
            ? await intakeContextFacade.GetWeightTrendSummaryBetween(patientId, from, to, cancellationToken)
            : null;

        return MonitoringPeriodFacts.Compute(from, to, evaluated, counted, weight?.ChangeKg, weight?.SlopeKgPerWeek,
            weight?.PointCount ?? 0);
    }

    /// <summary>es or en: the preferred language of IAM-3, or es when it cannot be read.</summary>
    public static string LanguageOf(string? preferred)
    {
        return string.Equals(preferred?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
    }
}
