using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Events;

/// <summary>Subflow 5.1. Stays inside Monitoring and Adherence.</summary>
/// <param name="WindowId">The window that was opened.</param>
/// <param name="PatientId">Whose window it is.</param>
/// <param name="From">The first day it counts.</param>
/// <param name="To">The last day it counts, before any day extends it.</param>
public record EvaluationWindowOpened(int WindowId, int PatientId, DateOnly From, DateOnly To)
    : DomainEventBase;

/// <summary>Subflow 5.2. Stays inside Monitoring and Adherence.</summary>
/// <param name="WindowId">The window the snapshot was added to.</param>
/// <param name="PatientId">Whose targets they are.</param>
/// <param name="PlanVersion">Version of the published contract that was frozen.</param>
/// <param name="TakenAt">The moment those targets came into force.</param>
public record TargetsSnapshotTaken(int WindowId, int PatientId, int PlanVersion, DateTimeOffset TakenAt)
    : DomainEventBase;

/// <summary>Subflow 5.3. Stays inside Monitoring and Adherence.</summary>
/// <param name="WindowId">The window the point was appended to.</param>
/// <param name="PatientId">Whose measurement it is.</param>
/// <param name="Date">The day it was taken.</param>
/// <param name="ValueKg">The reading in kilograms.</param>
public record AnthropometryPointAppended(int WindowId, int PatientId, DateOnly Date, decimal ValueKg)
    : DomainEventBase;

/// <summary>
///     Subflow 5.4. Stays inside Monitoring and Adherence, where the internal policy of Subflow 5.6
///     reads the window it belongs to.
/// </summary>
/// <param name="WindowId">The window the day belongs to.</param>
/// <param name="PatientId">Whose day it is.</param>
/// <param name="Date">The day the patient was living.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged.</param>
public record DayEvaluated(int WindowId, int PatientId, DateOnly Date, string Outcome) : DomainEventBase;

/// <summary>
///     Subflow 5.4. Stays inside Monitoring and Adherence. It is what the daily indicator the patient
///     sees is built from.
/// </summary>
/// <remarks>
///     It carries an outcome and nothing else. There is no streak on it, no penalty and no grade,
///     and <c>Unlogged</c> is one of the four values rather than a fifth kind of bad day.
/// </remarks>
/// <param name="WindowId">The window the day belongs to.</param>
/// <param name="PatientId">Whose day it is.</param>
/// <param name="Date">The day the patient was living.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged.</param>
public record DailyComplianceComputed(int WindowId, int PatientId, DateOnly Date, string Outcome)
    : DomainEventBase;

/// <summary>Subflow 5.5. Stays inside Monitoring and Adherence.</summary>
/// <param name="WindowId">The window that was re-read.</param>
/// <param name="PatientId">Whose window it is.</param>
/// <param name="Date">The single day the late entry belonged to.</param>
public record WindowReEvaluated(int WindowId, int PatientId, DateOnly Date) : DomainEventBase;

/// <summary>
///     Subflow 5.6. Stays inside Monitoring and Adherence: a deviation that has not persisted is not
///     a signal, and letting it cross would put a Tuesday in a clinical inbox.
/// </summary>
/// <param name="DeviationId">The deviation that was read.</param>
/// <param name="PatientId">Whose window it came from.</param>
/// <param name="WindowId">The window it was read from.</param>
/// <param name="Magnitude">Mean distance from target, as a fraction of the target.</param>
/// <param name="Direction">Above or Below.</param>
public record DeviationDetected(
    int DeviationId,
    int PatientId,
    int WindowId,
    decimal Magnitude,
    string Direction) : DomainEventBase;

/// <summary>
///     Subflow 5.6. Integration event 12 of 13: it crosses into the clinical context, whose policy
///     opens a review item (Subflow 3.7).
/// </summary>
/// <remarks>
///     Business rule: Escalation Notifies Never Modifies The Plan, and invariant 3 of this bounded
///     context. Read the payload: a size, a direction and a sentence of evidence. There is no target,
///     no adjustment and no instruction on it, because what waits at the other end is a person
///     deciding, not a plan changing.
/// </remarks>
/// <param name="DeviationId">The deviation that became sustained.</param>
/// <param name="PatientId">Whose deviation it is.</param>
/// <param name="Magnitude">Mean distance from target, as a fraction of the target.</param>
/// <param name="Direction">Above or Below.</param>
/// <param name="Evidence">What was observed, in one sentence. Evidence, not a verdict.</param>
/// <param name="DeviatingDaysConsidered">
///     NC-11. How many logged days of the horizon deviated in this direction (0 from producers before NC-11).
/// </param>
/// <param name="LoggedDaysConsidered">
///     NC-11. How many logged days the horizon held. Unlogged days are not in it (0 from producers before NC-11).
/// </param>
/// <param name="MagnitudeEnergyKcal">
///     X-2. Mean distance from the energy target in kcal per day, unsigned (the sign is the direction); null from
///     producers before X-2.
/// </param>
public record SustainedDeviationDetected(
    int DeviationId,
    int PatientId,
    decimal Magnitude,
    string Direction,
    string Evidence,
    int DeviatingDaysConsidered = 0,
    int LoggedDaysConsidered = 0,
    decimal? MagnitudeEnergyKcal = null) : DomainEventBase;

/// <summary>Subflow 5.7. Stays inside Monitoring and Adherence.</summary>
/// <param name="PatientId">Whose index was recomputed.</param>
/// <param name="Value">Unexplained weight movement in kilograms per week.</param>
/// <param name="State">Normal, Watch or Alert.</param>
public record ConsistencyIndexRecomputed(int PatientId, decimal Value, string State) : DomainEventBase;

/// <summary>
///     Subflow 5.7. Stays inside Monitoring and Adherence, where the internal policy asks the patient.
/// </summary>
/// <remarks>
///     Business rule: Patient First Always (Subflow 5.7). This event has exactly one subscriber and
///     that subscriber prompts the patient. Nothing in the platform reacts to it by telling anybody
///     else.
/// </remarks>
/// <param name="PatientId">Whose index entered Alert.</param>
/// <param name="Value">Unexplained weight movement in kilograms per week.</param>
public record ConsistencyAlertRaised(int PatientId, decimal Value) : DomainEventBase;

/// <summary>Subflow 5.7. Stays inside Monitoring and Adherence.</summary>
/// <param name="PatientId">The patient who was asked.</param>
/// <param name="PromptedAt">When they were asked. Business rule: Prompt Date Recorded.</param>
public record PatientPromptedAboutConsistency(int PatientId, DateTimeOffset PromptedAt) : DomainEventBase;

/// <summary>MA-7. The patient saw the prompt (PT3). Stays inside Monitoring and Adherence.</summary>
/// <param name="PatientId">The patient who saw it.</param>
/// <param name="ShownAt">When. Business rule: Prompt Date Recorded.</param>
public record ConsistencyPromptAcknowledged(int PatientId, DateTimeOffset ShownAt) : DomainEventBase;

/// <summary>
///     Subflow 5.8. Integration event 13 of 13: it crosses into the clinical context, whose policy
///     opens a review item (Subflow 3.7).
/// </summary>
/// <remarks>
///     Business rules: Patient Prompt Required Before Escalation and Escalation Notifies Never
///     Modifies The Plan (Subflow 5.8). By the time this event exists the patient has already been
///     asked, because the aggregate refuses to escalate otherwise. The payload notifies and carries
///     nothing that could change a plan.
/// </remarks>
/// <param name="PatientId">Whose alert was escalated.</param>
/// <param name="PractitionerId">Who is being told.</param>
/// <param name="Value">Unexplained weight movement in kilograms per week.</param>
/// <param name="Evidence">What was observed, in one sentence, including when the patient was asked.</param>
/// <param name="State">X-2. The state of the index when it was escalated (Alert); null from producers before X-2.</param>
/// <param name="AlertSinceAt">X-2. Since when the index has been in alert.</param>
/// <param name="ShownToPatientAt">X-2. When the patient acknowledged the prompt (MA-7).</param>
/// <param name="WeeksInAlert">X-2. Whole weeks in alert at the moment of the escalation.</param>
public record AlertEscalatedToPractitioner(
    int PatientId,
    int PractitionerId,
    decimal Value,
    string Evidence,
    string? State = null,
    DateTimeOffset? AlertSinceAt = null,
    DateTimeOffset? ShownToPatientAt = null,
    int? WeeksInAlert = null) : DomainEventBase;

/// <summary>
///     Subflow 5.9. Deliberately does not cross a boundary, and this one is the important absence.
/// </summary>
/// <remarks>
///     Business rules: Gap Is Not A Deviation and Gap Never Escalates (Subflow 5.9). A day nobody
///     wrote in is not evidence of anything. The only handler this event has is the one that reminds
///     the patient, locally and without accusation, and no other bounded context subscribes to it.
/// </remarks>
/// <param name="PatientId">Whose diary has gone quiet.</param>
/// <param name="LastEntryDate">The last day anything was written, or null when there is none.</param>
/// <param name="DaysWithoutEntry">How many days of silence there have been.</param>
public record LoggingGapDetected(int PatientId, DateOnly? LastEntryDate, int DaysWithoutEntry)
    : DomainEventBase;

/// <summary>
///     Subflow 5.9. Stays inside Monitoring and Adherence and reaches nobody but the patient.
/// </summary>
/// <param name="PatientId">The patient who was reminded.</param>
/// <param name="RemindedAt">When.</param>
public record PatientReminded(int PatientId, DateTimeOffset RemindedAt) : DomainEventBase;

/// <summary>Subflow 5.10. Stays inside Monitoring and Adherence.</summary>
/// <param name="ReferralId">The referral that was recorded.</param>
/// <param name="PatientId">Who is being referred.</param>
/// <param name="Specialty">Which speciality.</param>
public record ReferralRecorded(int ReferralId, int PatientId, string Specialty) : DomainEventBase;

/// <summary>RM-4. Stays inside Monitoring and Adherence. Nothing subscribes to it yet.</summary>
/// <param name="ReferralId">The referral that was closed.</param>
/// <param name="PatientId">Whose referral.</param>
public record ReferralClosed(int ReferralId, int PatientId) : DomainEventBase;

/// <summary>Subflow 5.10. Stays inside Monitoring and Adherence.</summary>
/// <param name="FollowUpId">The visit that was scheduled.</param>
/// <param name="PatientId">Who is being seen.</param>
/// <param name="ScheduledFor">When.</param>
public record FollowUpScheduled(int FollowUpId, int PatientId, DateTimeOffset ScheduledFor)
    : DomainEventBase;

/// <summary>
///     Subflow 5.10. Stays inside Monitoring and Adherence.
/// </summary>
/// <remarks>
///     Business rule: Missed Visit Does Not Close The Care Link (Subflow 5.10). No context subscribes
///     to this event, which is how the rule is kept: there is nothing listening that could act on it.
/// </remarks>
/// <param name="FollowUpId">The visit that did not happen.</param>
/// <param name="PatientId">Whose visit it was.</param>
public record FollowUpMissed(int FollowUpId, int PatientId) : DomainEventBase;

/// <summary>MA-5. Stays inside Monitoring and Adherence. Nothing subscribes to it yet.</summary>
/// <param name="FollowUpId">The visit that was cancelled.</param>
/// <param name="PatientId">Whose visit it was.</param>
/// <param name="Reason">Why, in at most 30 characters.</param>
public record FollowUpCancelled(int FollowUpId, int PatientId, string Reason) : DomainEventBase;

/// <summary>MA-5. Stays inside Monitoring and Adherence. Nothing subscribes to it yet.</summary>
/// <param name="FollowUpId">The visit that was moved.</param>
/// <param name="PatientId">Whose visit it is.</param>
/// <param name="ScheduledFor">The new moment.</param>
public record FollowUpRescheduled(int FollowUpId, int PatientId, DateTimeOffset ScheduledFor) : DomainEventBase;

/// <summary>
///     MA-4. Stays inside Monitoring and Adherence. Internal, for metrics: the mockup shows no notification to the
///     practitioner.
/// </summary>
/// <remarks>
///     Business rule: Check In Raises No Signal (MA-4). No context subscribes to this event, which is how the
///     rule is kept: nothing listening could turn "Hard" into a signal, a deviation or an escalation.
/// </remarks>
/// <param name="FollowUpId">The visit the check in is for.</param>
/// <param name="PatientId">Who sent it.</param>
/// <param name="PractitionerId">Who will read it.</param>
public record PreVisitCheckInSubmitted(int FollowUpId, int PatientId, int PractitionerId) : DomainEventBase;

/// <summary>Subflow 5.11. Stays inside Monitoring and Adherence.</summary>
/// <param name="WindowId">The window that stopped counting.</param>
/// <param name="PatientId">Whose window it was.</param>
/// <param name="ClosedAt">The moment the care link was revoked.</param>
public record EvaluationWindowClosed(int WindowId, int PatientId, DateTimeOffset ClosedAt)
    : DomainEventBase;
