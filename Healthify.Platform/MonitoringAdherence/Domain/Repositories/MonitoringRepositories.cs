using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.MonitoringAdherence.Domain.Repositories;

/// <summary>
///     The windows.
/// </summary>
/// <remarks>
///     Business rule: Evaluated Data Is Preserved (Subflow 5.11). Nothing in this context calls the
///     inherited <c>Remove</c>, no command service references it and no endpoint reaches it. A closed
///     window is a finished reading, not a deleted one.
/// </remarks>
public interface IEvaluationWindowRepository : IBaseRepository<EvaluationWindow>
{
    /// <summary>Rule: One Open Window Per Patient (Subflow 5.1). The window still counting, or none.</summary>
    Task<EvaluationWindow?> FindOpenByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    /// <summary>Every window of one patient, most recent first. Closed ones included.</summary>
    Task<IEnumerable<EvaluationWindow>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    /// <summary>Input to the time-driven policy of Subflow 5.9.</summary>
    Task<IEnumerable<EvaluationWindow>> ListOpenAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

/// <summary>The deviations read from a window.</summary>
public interface IDeviationRepository : IBaseRepository<Deviation>
{
    Task<IEnumerable<Deviation>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     The deviation this window is already reporting in this direction, if there is one.
    /// </summary>
    /// <remarks>
    ///     Detect Deviation runs after every evaluated day, so without this the same tendency would
    ///     produce a new row every meal. One open reading per window and direction is restated rather
    ///     than duplicated.
    /// </remarks>
    Task<Deviation?> FindLatestByWindowAndDirectionAsync(WindowId windowId, DeviationDirection direction,
        CancellationToken cancellationToken = default);
}

/// <summary>One index per patient, keyed by the patient.</summary>
public interface IConsistencyIndexRepository : IBaseRepository<ConsistencyIndex>
{
    Task<ConsistencyIndex?> FindByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Input to the time-driven policy of Subflow 5.8: indices in Alert, already shown to their
    ///     patient, not yet escalated, and in that state since before the given moment.
    /// </summary>
    Task<IEnumerable<ConsistencyIndex>> ListEscalatableAsync(DateTimeOffset alertSinceBefore,
        int batchSize, CancellationToken cancellationToken = default);
}

/// <summary>The referrals.</summary>
public interface IReferralRepository : IBaseRepository<Referral>
{
    Task<IEnumerable<Referral>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);
}

/// <summary>MA-4. The check ins, one per visit.</summary>
public interface IPreVisitCheckInRepository : IBaseRepository<PreVisitCheckIn>
{
    /// <summary>Rule: One Check In Per Visit (MA-4).</summary>
    Task<PreVisitCheckIn?> FindByFollowUpIdAsync(int followUpId, CancellationToken cancellationToken = default);

    /// <summary>The check ins of these visits, in one query.</summary>
    Task<IEnumerable<PreVisitCheckIn>> ListByFollowUpIdsAsync(IReadOnlyCollection<int> followUpIds,
        CancellationToken cancellationToken = default);
}

/// <summary>The agenda.</summary>
public interface IScheduledFollowUpRepository : IBaseRepository<ScheduledFollowUp>
{
    /// <summary>Rule: One Active Scheduled Visit Per Patient (Subflow 5.10).</summary>
    Task<ScheduledFollowUp?> FindScheduledByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<ScheduledFollowUp>> ListByPractitionerIdAsync(int practitionerId,
        CancellationToken cancellationToken = default);

    /// <summary>MA-2. The agenda, optionally of one state and from a moment on, soonest first.</summary>
    Task<IEnumerable<ScheduledFollowUp>> ListByPractitionerIdAsync(int practitionerId, FollowUpState? state,
        DateTimeOffset? from, CancellationToken cancellationToken = default);

    /// <summary>
    ///     MA-2. The visit of this patient with this practitioner, still scheduled or flagged missed, whose
    ///     date falls in [<paramref name="from" />, <paramref name="to" />). Soonest first, at most one.
    /// </summary>
    Task<ScheduledFollowUp?> FindOpenForDayAsync(int patientId, int practitionerId, DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>MA-2/RM-1. The scheduled visit of each of these patients, in one query.</summary>
    Task<IEnumerable<ScheduledFollowUp>> ListScheduledByPatientIdsAsync(IReadOnlyCollection<int> patientIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     MA-4. The visits of this patient with this practitioner that are still open (Scheduled or Missed), most
    ///     recent first.
    /// </summary>
    Task<IEnumerable<ScheduledFollowUp>> ListOpenByPatientAndPractitionerAsync(int patientId, int practitionerId,
        CancellationToken cancellationToken = default);

    /// <summary>MA-3. The visits of one patient, optionally of one state, most recent first.</summary>
    Task<IEnumerable<ScheduledFollowUp>> ListByPatientIdAsync(int patientId, FollowUpState? state,
        CancellationToken cancellationToken = default);

    /// <summary>Input to the time-driven policy of Subflow 5.10.</summary>
    Task<IEnumerable<ScheduledFollowUp>> ListOverdueAsync(DateTimeOffset asOf, int batchSize,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     IA-2. The weekly summaries, one per patient and week. Generated content: unlike the evaluated data of this
///     context, it is removed when the AI consent is withdrawn, the function is turned off or its retention ends.
/// </summary>
public interface IWeeklySummaryRepository : IBaseRepository<WeeklySummary>
{
    /// <summary>The summary of the most recent week, or null.</summary>
    Task<WeeklySummary?> FindLatestByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>Rule: One Summary Per Patient And Week (IA-2).</summary>
    Task<WeeklySummary?> FindByPatientIdAndWeekStartAsync(int patientId, DateOnly weekStart,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<WeeklySummary>> ListByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>§12-#14. Summaries generated before the cut, oldest first.</summary>
    Task<IEnumerable<WeeklySummary>> ListGeneratedBeforeAsync(DateTimeOffset cut, int batchSize,
        CancellationToken cancellationToken = default);
}
