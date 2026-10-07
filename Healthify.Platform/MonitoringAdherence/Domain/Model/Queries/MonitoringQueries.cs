using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;

/// <summary>Read model Patient Monitoring Panel. Every window of one patient, most recent first.</summary>
/// <param name="PatientId">Whose windows.</param>
public record GetEvaluationWindowsByPatientIdQuery(int PatientId);

/// <summary>Read model Patient Monitoring Panel. The window that is still counting, or none.</summary>
/// <param name="PatientId">Whose window.</param>
public record GetCurrentEvaluationWindowByPatientIdQuery(int PatientId);

/// <summary>
///     Read model Daily Compliance Indicator, the one the patient sees.
/// </summary>
/// <remarks>
///     A null date means the whole series. An unlogged day is part of the answer, not a hole in it.
/// </remarks>
/// <param name="PatientId">Whose days.</param>
/// <param name="Date">One day, or null for the series.</param>
public record GetDailyComplianceByPatientIdQuery(int PatientId, DateOnly? Date);

/// <summary>
///     MA-6 - Daily Compliance Indicator over a range (PAC-2 "Esta semana", PT13.2, PT20 "Últimos 7 días"): every
///     calendar day from <paramref name="From" /> to <paramref name="To" /> and their summary.
/// </summary>
/// <param name="PatientId">Whose days.</param>
/// <param name="From">First day, included.</param>
/// <param name="To">Last day, included; at most 31 days after the first.</param>
public record GetDailyComplianceRangeQuery(int PatientId, DateOnly From, DateOnly To);

/// <summary>Read model Patient Monitoring Panel, deviations section. Read by the practitioner.</summary>
/// <param name="PatientId">Whose deviations.</param>
public record GetDeviationsByPatientIdQuery(int PatientId);

/// <summary>Read model Consistency Card, the one the patient sees.</summary>
/// <param name="PatientId">Whose index.</param>
public record GetConsistencyIndexByPatientIdQuery(int PatientId);

/// <summary>Read model Patient Record, referrals section.</summary>
/// <param name="PatientId">Whose referrals.</param>
public record GetReferralsByPatientIdQuery(int PatientId);

/// <summary>Read model Practitioner Agenda.</summary>
/// <param name="PractitionerId">Whose agenda.</param>
/// <param name="State">MA-2. Only visits in this state (PR17.0 "Próximas consultas" asks for Scheduled).</param>
/// <param name="From">MA-2. Only visits scheduled for this moment or later.</param>
public record GetScheduledFollowUpsByPractitionerIdQuery(
    int PractitionerId,
    FollowUpState? State = null,
    DateTimeOffset? From = null);

/// <summary>
///     MA-2. Read model Practitioner Agenda with the name of each patient. Same filters as
///     <see cref="GetScheduledFollowUpsByPractitionerIdQuery" />.
/// </summary>
public record GetPractitionerAgendaQuery(int PractitionerId, FollowUpState? State = null, DateTimeOffset? From = null);

/// <summary>
///     MA-2/RM-1/RM-2. The visit on the calendar of each patient (at most one per patient, by the rule One
///     Active Scheduled Visit Per Patient).
/// </summary>
/// <param name="PatientIds">Whose visits.</param>
public record GetScheduledFollowUpsByPatientIdsQuery(IReadOnlyCollection<int> PatientIds);

/// <summary>
///     MA-3. The visit on the calendar of one patient (PT3 and PT20 "Próxima consulta"), at most one by the rule
///     One Active Scheduled Visit Per Patient.
/// </summary>
/// <param name="PatientId">Whose visit.</param>
public record GetNextFollowUpByPatientIdQuery(int PatientId);

/// <summary>MA-3. The visits of one patient (PT25 "ANTERIORES" asks for Completed), most recent first.</summary>
/// <param name="PatientId">Whose visits.</param>
/// <param name="State">Only visits in this state; null for all of them.</param>
public record GetFollowUpsByPatientIdQuery(int PatientId, FollowUpState? State = null);

/// <summary>MA-4. One visit, to know whose it is before reading its check in.</summary>
/// <param name="FollowUpId">The visit.</param>
public record GetScheduledFollowUpByIdQuery(int FollowUpId);

/// <summary>MA-4. The check in of one visit (PT25.3), or none.</summary>
/// <param name="FollowUpId">The visit.</param>
public record GetPreVisitCheckInByFollowUpIdQuery(int FollowUpId);

/// <summary>
///     MA-4. The check in of the visit of this patient with this practitioner that is still open (Scheduled or
///     Missed): what EV-2 shows during the consultation. A check in of a visit already completed belongs to a
///     previous cycle and is not returned.
/// </summary>
/// <param name="PatientId">Whose check in.</param>
/// <param name="PractitionerId">Who reads it.</param>
public record GetLatestPreVisitCheckInQuery(int PatientId, int PractitionerId);

/// <summary>
///     Input to the time-driven policy of Subflow 5.9. Every window still counting days.
/// </summary>
/// <param name="BatchSize">How many to take in one cycle.</param>
public record GetOpenEvaluationWindowsQuery(int BatchSize);

/// <summary>
///     Input to the time-driven policy of Subflow 5.8. Every index sitting in Alert, already shown to
///     its patient and not yet escalated.
/// </summary>
/// <param name="AlertSinceBefore">The moment the three-week rule is measured against.</param>
/// <param name="BatchSize">How many to take in one cycle.</param>
public record GetEscalatableConsistencyIndicesQuery(DateTimeOffset AlertSinceBefore, int BatchSize);

/// <summary>Input to the time-driven policy of Subflow 5.10. Visits whose date has passed.</summary>
/// <param name="AsOf">The moment the scheduled date is compared against.</param>
/// <param name="BatchSize">How many to take in one cycle.</param>
public record GetOverdueScheduledFollowUpsQuery(DateTimeOffset AsOf, int BatchSize);

/// <summary>IA-2. The latest weekly summary of a patient (PT13, PT13.2), or none (PT13.2.V).</summary>
/// <param name="PatientId">Whose summary.</param>
public record GetLatestWeeklySummaryByPatientIdQuery(int PatientId);
