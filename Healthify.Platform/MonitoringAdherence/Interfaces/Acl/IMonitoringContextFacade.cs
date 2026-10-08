namespace Healthify.Platform.MonitoringAdherence.Interfaces.Acl;

/// <summary>
///     One evaluated day. Primitives only.
/// </summary>
/// <remarks>
///     Two fields, and the second one has four values: Met, Exceeded, Short and Unlogged. There is no
///     grade on this DTO, no streak and no penalty, and <c>Unlogged</c> sits beside the other three
///     rather than below them, because a day nobody wrote in is a different fact from a bad day.
/// </remarks>
/// <param name="Date">The calendar day the patient was living.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged.</param>
public record DailyComplianceItem(DateOnly Date, string Outcome);

/// <summary>
///     Where the consistency index of a patient stands, and what has already been done about it.
/// </summary>
/// <remarks>
///     The two dates are on this contract because the order they happened in is the invariant: the
///     patient is shown the index first, and only after three weeks in Alert does a practitioner hear
///     about it. A read model that shows the escalation without showing the prompt would be showing
///     surveillance.
/// </remarks>
/// <param name="State">Normal, Watch or Alert.</param>
/// <param name="ShownToPatientAt">When the patient was asked. Null when they have not been.</param>
/// <param name="EscalatedAt">When the practitioner was told. Null when they have not been.</param>
public record ConsistencyStateItem(
    string State,
    DateTimeOffset? ShownToPatientAt,
    DateTimeOffset? EscalatedAt);

/// <summary>
///     One point of the clinical weight series. Primitives only.
/// </summary>
/// <remarks>
///     TODO: discrepancy between the prompt and this contract - the prompt declares
///     <c>GetAnthropometrySeries</c> as returning a list of <see cref="DailyComplianceItem" />, which
///     has nowhere to carry a weight. Both are provided: the declared signature is implemented
///     literally so that the contract matches the prompt, and this typed variant exists beside it so
///     that the composite panel of Phase 7 can render the series it is supposed to show. Reported
///     rather than silently corrected.
/// </remarks>
/// <param name="Date">The day the measurement was taken.</param>
/// <param name="ValueKg">The reading in kilograms.</param>
/// <param name="Source">Always <c>ClinicalMeasurement</c>. The two weight series are never merged.</param>
public record AnthropometryPointItem(DateOnly Date, decimal ValueKg, string Source);

/// <summary>
///     One referral, as the composite record shows it. Primitives only.
/// </summary>
/// <remarks>
///     Subflow 5.10 names Patient Record as the read model of Record Referral, and Patient Record is
///     composed exclusively through ACL contracts, so the referral has to be readable through one.
///     It is a record that something was decided on a date, not a workflow: what happens at the other end of a
///     referral happens outside this platform. RM-4 (DECISIÓN §12-#8) gives it one state, Open or Closed, closed
///     by hand by the practitioner.
/// </remarks>
/// <param name="ReferralId">Identifier of the referral.</param>
/// <param name="Specialty">Where the patient was sent.</param>
/// <param name="Reason">Why, in the practitioner's own words.</param>
/// <param name="IssuedBy">The practitioner who issued it.</param>
/// <param name="IssuedAt">When it was issued.</param>
/// <param name="Status">RM-4. Open ("en curso") or Closed.</param>
/// <param name="ClosedAt">RM-4. When it was closed, or null.</param>
public record ReferralItem(
    int ReferralId,
    string Specialty,
    string Reason,
    int IssuedBy,
    DateTimeOffset IssuedAt,
    string Status = "Open",
    DateTimeOffset? ClosedAt = null);

/// <summary>
///     MA-2/RM-1/RM-2. The visit on the calendar of a patient (PAC-1 "Próxima consulta", PR1). Primitives only;
///     no clinical note travels with it.
/// </summary>
/// <param name="FollowUpId">Identifier of the visit.</param>
/// <param name="PatientId">Whose visit.</param>
/// <param name="ScheduledFor">When it is.</param>
/// <param name="Modality">InPerson or Remote.</param>
/// <param name="Preparation">Preparation codes; empty when none.</param>
/// <param name="ScheduledAt">When it was put on the calendar.</param>
/// <param name="PractitionerId">RM-5. Whose agenda it is on; 0 only where a caller builds it without one.</param>
public record NextFollowUpItem(
    int FollowUpId,
    int PatientId,
    DateTimeOffset ScheduledFor,
    string Modality,
    IReadOnlyList<string> Preparation,
    DateTimeOffset? ScheduledAt,
    int PractitionerId = 0);

/// <summary>
///     RM-2. How the days of a range went, counted. Primitives only.
/// </summary>
/// <remarks>
///     The denominator is the calendar days of the range, and a day nobody wrote in counts as Unlogged, never as
///     Short: a day without data is not a day of non-compliance. <c>Logged</c> is Met + Exceeded + Short.
/// </remarks>
/// <param name="Met">Days within the targets.</param>
/// <param name="Exceeded">Days above.</param>
/// <param name="Short">Days below.</param>
/// <param name="Unlogged">Days without a confirmed entry, evaluated or not.</param>
/// <param name="Logged">Days with something logged.</param>
/// <param name="TotalDays">Calendar days of the range.</param>
public record ComplianceSummaryItem(int Met, int Exceeded, int Short, int Unlogged, int Logged, int TotalDays);

/// <summary>
///     MA-6. A range of calendar days, day by day (a day never evaluated is Unlogged) and counted. Primitives only.
/// </summary>
/// <param name="Days">Every calendar day of the range, oldest first.</param>
/// <param name="Summary">The same days counted by outcome.</param>
public record ComplianceRangeItem(IReadOnlyList<DailyComplianceItem> Days, ComplianceSummaryItem Summary);

/// <summary>MA-4. One question of a check in. Primitives only; the AI generation it came from is not published.</summary>
/// <param name="Text">The question.</param>
/// <param name="Origin">Patient or AiSuggested.</param>
/// <param name="Language">X-2. es or en for an AI suggestion; null for the patient's own questions.</param>
public record CheckInQuestionItem(string Text, string Origin, string? Language = null);

/// <summary>
///     MA-4. What the patient told their practitioner before the visit (EV-2 "Antes de la consulta, … contó").
///     Primitives only.
/// </summary>
/// <remarks>
///     Business rule: Check In Raises No Signal (MA-4). It is a message for a person to read, so it travels as
///     the patient wrote it, with no score and no interpretation.
/// </remarks>
/// <param name="FollowUpId">The visit it is for.</param>
/// <param name="Feeling">Good, Fair or Hard.</param>
/// <param name="Difficulties">Dinners, Weekends, EatingOut, Schedules, Cravings; may be empty.</param>
/// <param name="Questions">The questions; may be empty.</param>
/// <param name="SubmittedAt">When it was first sent.</param>
/// <param name="EditedAt">When it was last edited, if it was.</param>
/// <param name="IsLocked">True once it can no longer be edited.</param>
public record PreVisitCheckInItem(
    int FollowUpId,
    string Feeling,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<CheckInQuestionItem> Questions,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? EditedAt,
    bool IsLocked);

/// <summary>
///     Public ACL contract of the Monitoring and Adherence bounded context.
/// </summary>
/// <remarks>
///     Read only, without exception. This context is the one that interprets, and interpretation is
///     not something another context gets to ask for: there is no method here that writes, and the
///     absence is the enforcement.
///     Notice what is not published either. There is no deviation on this contract and no evidence
///     string. Those cross the boundary as events, once, when they are sustained, and they land in a
///     human inbox. A query that returned them on demand would be a second, quieter path from a
///     signal to a clinical decision.
///     Every method degrades gracefully: null, an empty list, and never an exception.
/// </remarks>
public interface IMonitoringContextFacade
{
    /// <summary>
    ///     The last <paramref name="days" /> evaluated days of a patient, oldest first. An empty list
    ///     when there are none or the lookup fails.
    /// </summary>
    /// <param name="patientId">Whose days.</param>
    /// <param name="days">How many days back to take.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<DailyComplianceItem>> GetDailyComplianceSeries(int patientId, int days,
        CancellationToken ct = default);

    /// <summary>Where the consistency index stands, or null when there is none or the lookup fails.</summary>
    /// <param name="patientId">Whose index.</param>
    /// <param name="ct">Cancellation.</param>
    Task<ConsistencyStateItem?> GetConsistencyState(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     The clinical weight series, in the shape the prompt declares for it.
    /// </summary>
    /// <remarks>
    ///     TODO: discrepancy between the prompt and this contract - see
    ///     <see cref="AnthropometryPointItem" />. The declared return type cannot carry a weight, so
    ///     the <c>Outcome</c> field carries the reading formatted with the invariant culture and
    ///     <see cref="GetAnthropometrySeriesPoints" /> is what a caller should actually use.
    /// </remarks>
    /// <param name="patientId">Whose series.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<DailyComplianceItem>> GetAnthropometrySeries(int patientId,
        CancellationToken ct = default);

    /// <summary>
    ///     The clinical weight series, typed. Empty when there is none or the lookup fails.
    /// </summary>
    /// <remarks>
    ///     Clinical measurements only. The readings the patient takes at home are a separate series
    ///     owned by a separate context, and merging them here would quietly give a bathroom scale
    ///     clinical authority.
    /// </remarks>
    /// <param name="patientId">Whose series.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<AnthropometryPointItem>> GetAnthropometrySeriesPoints(int patientId,
        CancellationToken ct = default);

    /// <summary>
    ///     The referrals of a patient, most recent first. An empty list when there are none or the
    ///     lookup fails.
    /// </summary>
    /// <remarks>
    ///     Subflow 5.10, the referrals section of Patient Record. Read only, like everything else on
    ///     this contract.
    /// </remarks>
    /// <param name="patientId">Whose referrals.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<ReferralItem>> GetReferrals(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     MA-2/RM-1/RM-2. The visit on the calendar of each patient, keyed by patient, in one query so that a
    ///     roster does not read one patient at a time. A patient without one is absent; empty when the lookup
    ///     fails.
    /// </summary>
    Task<IReadOnlyDictionary<int, NextFollowUpItem>> GetNextFollowUpsByPatientIds(IEnumerable<int> patientIds,
        CancellationToken ct = default);

    /// <summary>
    ///     RM-2. The days from <paramref name="from" /> to <paramref name="to" /> (both included) counted by
    ///     outcome, or null when the range is empty or the lookup fails.
    /// </summary>
    /// <remarks>MA-6: the same range as <see cref="GetComplianceRange" />, counted only.</remarks>
    Task<ComplianceSummaryItem?> GetComplianceSummary(int patientId, DateOnly from, DateOnly to,
        CancellationToken ct = default);

    /// <summary>
    ///     MA-6. Every calendar day from <paramref name="from" /> to <paramref name="to" /> (both included, at most 31)
    ///     and their summary, for PAC-2 "Esta semana". Null when the range is not valid or the lookup fails.
    /// </summary>
    Task<ComplianceRangeItem?> GetComplianceRange(int patientId, DateOnly from, DateOnly to,
        CancellationToken ct = default);

    /// <summary>
    ///     MA-4. The check in of the visit of this patient with this practitioner that is still open (Scheduled or
    ///     Missed), or null when there is none, the patient has not answered, or the lookup fails. A check in of a
    ///     visit already completed belongs to a previous cycle and is not returned.
    /// </summary>
    /// <param name="patientId">Whose check in.</param>
    /// <param name="practitionerId">Who reads it.</param>
    /// <param name="ct">Cancellation.</param>
    Task<PreVisitCheckInItem?> GetLatestCheckInForPatient(int patientId, int practitionerId,
        CancellationToken ct = default);
}
