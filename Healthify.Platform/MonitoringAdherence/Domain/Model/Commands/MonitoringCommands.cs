namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;

/// <summary>
///     Subflow 5.1 - Open Evaluation Window. Issued by the policy that reacts to Care Link
///     Established, never by a user and never by an endpoint.
/// </summary>
/// <param name="PatientId">The patient the window is opened for.</param>
/// <param name="CareLinkId">The link whose establishment opened it.</param>
public record OpenEvaluationWindowCommand(int PatientId, int CareLinkId);

/// <summary>
///     Subflow 5.2 - Snapshot Active Targets. Issued by the policy that reacts to Active Targets
///     Updated, never by an endpoint.
/// </summary>
/// <remarks>
///     Every field comes from the published contract. There is no diagnosis among them, no clinical
///     rationale and no calculation basis, and there is nowhere on this record to put one.
/// </remarks>
/// <param name="PatientId">Whose targets these are.</param>
/// <param name="PlanVersion">Version of the published contract.</param>
/// <param name="ValidFrom">The moment these targets come into force.</param>
/// <param name="EnergyKcal">Daily energy target in kilocalories.</param>
/// <param name="ProteinG">Daily protein target in grams.</param>
/// <param name="CarbG">Daily carbohydrate target in grams.</param>
/// <param name="FatG">Daily fat target in grams.</param>
public record SnapshotActiveTargetsCommand(
    int PatientId,
    int PlanVersion,
    DateTimeOffset ValidFrom,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG);

/// <summary>
///     Subflow 5.3 - Append Anthropometry Point. Issued by the policy that reacts to Clinical
///     Measurement Taken, never by an endpoint.
/// </summary>
/// <param name="PatientId">Whose measurement it is.</param>
/// <param name="WeightKg">The reading in kilograms.</param>
/// <param name="TakenAt">When the practitioner took it.</param>
public record AppendAnthropometryPointCommand(int PatientId, decimal WeightKg, DateTimeOffset TakenAt);

/// <summary>
///     Subflow 5.4 - Evaluate Day. Issued by the policies that react to a diary entry, never by an
///     endpoint.
/// </summary>
/// <remarks>
///     The date is the calendar day the patient was living when they logged, taken from the declared
///     local timestamp on the event, never from the server clock.
/// </remarks>
/// <param name="PatientId">Whose day is being evaluated.</param>
/// <param name="Date">The day the patient was living.</param>
public record EvaluateDayCommand(int PatientId, DateOnly Date);

/// <summary>
///     Subflow 5.5 - Re Evaluate Window. Issued by the policy that reacts to Entry Synchronized.
/// </summary>
/// <remarks>
///     Business rule: Late Entry Re Evaluates Its Own Day Only (Subflow 5.5). The command carries one
///     date, which is the date the late entry declared. There is no range on this record and no way
///     to ask for one.
/// </remarks>
/// <param name="PatientId">Whose window is being re-read.</param>
/// <param name="Date">The day the late entry belongs to, and the only day that is touched.</param>
public record ReEvaluateWindowCommand(int PatientId, DateOnly Date);

/// <summary>
///     Subflow 5.4, MA-1 - Mark Unlogged Days Before. Issued by the policy that reacts to Diary Batch
///     Synchronized, with the most recent day the batch declared, after the batch's days are evaluated.
/// </summary>
/// <remarks>
///     Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Subflow 5.4). A batch is
///     re-evaluated day by day without filling the silence around it, so the days before its most
///     recent one that nobody wrote in, including the gaps between its own days, are marked here,
///     exactly as a live evaluation of that day would have marked them. A day already evaluated is
///     never touched.
/// </remarks>
/// <param name="PatientId">Whose window it is.</param>
/// <param name="Date">The most recent day of the batch. Only the days before it are considered.</param>
public record MarkUnloggedDaysBeforeCommand(int PatientId, DateOnly Date);

/// <summary>Subflow 5.6 - Detect Deviation. Issued by the internal policy that reacts to Day Evaluated.</summary>
/// <param name="PatientId">Whose window is being read.</param>
public record DetectDeviationCommand(int PatientId);

/// <summary>
///     Subflow 5.6 - Flag Sustained Deviation. Issued by the internal policy that reacts to Deviation
///     Detected.
/// </summary>
/// <param name="DeviationId">The deviation being tested against the majority rule.</param>
public record FlagSustainedDeviationCommand(int DeviationId);

/// <summary>
///     Subflow 5.7 - Recompute Consistency Index. Issued by the policy that reacts to Weight Trend
///     Recalculated, never by an endpoint.
/// </summary>
/// <param name="PatientId">Whose index is being recomputed.</param>
public record RecomputeConsistencyIndexCommand(int PatientId);

/// <summary>
///     Subflow 5.7 - Prompt Patient. Issued by the internal policy that reacts to Consistency Alert
///     Raised.
/// </summary>
/// <remarks>
///     Business rule: Patient First Always (Subflow 5.7). This command exists so that the patient is
///     asked, and the command that tells the practitioner refuses to run until it has.
/// </remarks>
/// <param name="PatientId">The patient being asked.</param>
public record PromptPatientCommand(int PatientId);

/// <summary>
///     MA-7 - Acknowledge Consistency Prompt. Issued by the patient's app when it painted the card in PT3 ("Algo no
///     cuadra"). The moment is the server's: the patient is the one who saw it.
/// </summary>
/// <param name="PatientId">The patient, from the token.</param>
public record AcknowledgeConsistencyPromptCommand(int PatientId);

/// <summary>
///     Subflow 5.8 - Escalate To Practitioner. Issued by the time-driven policy that watches for an
///     alert sustained three weeks.
/// </summary>
/// <remarks>
///     Business rule: Escalation Notifies Never Modifies The Plan (Subflow 5.8). What this command
///     leads to is an item in a human inbox. There is no field on it that could carry a change to a
///     plan and no service in this context that could apply one.
/// </remarks>
/// <param name="PatientId">The patient whose alert is being escalated.</param>
public record EscalateToPractitionerCommand(int PatientId);

/// <summary>
///     Subflow 5.9 - Flag Logging Gap. Issued by the time-driven policy that watches for N days
///     without a diary entry.
/// </summary>
/// <remarks>
///     Business rule: Gap Is Not A Deviation (Subflow 5.9). This command does not reach the deviation
///     aggregate, and there is no field on it that a deviation could be built from.
/// </remarks>
/// <param name="PatientId">The patient whose diary has gone quiet.</param>
/// <param name="AsOf">The day the gap was noticed.</param>
public record FlagLoggingGapCommand(int PatientId, DateOnly AsOf);

/// <summary>
///     Subflow 5.9 - Remind Patient. Issued by the internal policy that reacts to Logging Gap
///     Detected.
/// </summary>
/// <remarks>
///     Business rule: Reminder Is Local And Non Accusatory (Subflow 5.9). Local means it goes to the
///     patient and to nobody else; the practitioner is not copied in on a reminder.
/// </remarks>
/// <param name="PatientId">The patient being reminded.</param>
public record RemindPatientCommand(int PatientId);

/// <summary>Subflow 5.10 - Record Referral. Issued by the practitioner.</summary>
/// <param name="PatientId">Who is being referred.</param>
/// <param name="PractitionerId">Who is referring them.</param>
/// <param name="Specialty">Which speciality.</param>
/// <param name="Reason">Why.</param>
public record RecordReferralCommand(int PatientId, int PractitionerId, string Specialty, string Reason);

/// <summary>RM-4 - Close Referral (DECISIÓN §12-#8). Issued by the practitioner who recorded it.</summary>
/// <param name="ReferralId">The referral.</param>
/// <param name="PractitionerId">Who closes it, from the token.</param>
public record CloseReferralCommand(int ReferralId, int PractitionerId);

/// <summary>Subflow 5.10 - Schedule Follow Up. Issued by the practitioner.</summary>
/// <param name="PatientId">Who is being seen.</param>
/// <param name="PractitionerId">Who is seeing them.</param>
/// <param name="ScheduledFor">When.</param>
/// <param name="Preparation">MA-2. Codes of the closed list; null or empty means no instructions.</param>
/// <param name="Modality">MA-2. InPerson (default) or Remote.</param>
public record ScheduleFollowUpCommand(
    int PatientId,
    int PractitionerId,
    DateTimeOffset ScheduledFor,
    IReadOnlyList<string>? Preparation = null,
    string Modality = "InPerson");

/// <summary>
///     Subflow 5.10 - Flag Missed Follow Up. Issued by the time-driven policy that watches for a
///     scheduled date that passed without a visit.
/// </summary>
/// <param name="FollowUpId">The visit that did not happen.</param>
public record FlagMissedFollowUpCommand(int FollowUpId);

/// <summary>
///     MA-2 - Complete Follow Up. Issued by the policy that reacts to Consultation Completed (Nutritional
///     Care, NC-2), never by an endpoint.
/// </summary>
/// <param name="PatientId">Whose consultation.</param>
/// <param name="PractitionerId">Who held it.</param>
/// <param name="ConsultationId">The consultation that was published.</param>
/// <param name="CompletedAt">When it was published.</param>
/// <param name="ScheduledFollowUpId">The visit it started from, when the consultation knew it.</param>
public record CompleteFollowUpFromConsultationCommand(
    int PatientId,
    int PractitionerId,
    int ConsultationId,
    DateTimeOffset CompletedAt,
    int? ScheduledFollowUpId);

/// <summary>MA-5 - Cancel Follow Up. Issued by the practitioner whose agenda the visit is on.</summary>
/// <param name="FollowUpId">The visit.</param>
/// <param name="PractitionerId">Who cancels it, from the token.</param>
/// <param name="Reason">Short reason, at most 30 characters; null stores "CancelledByPractitioner".</param>
public record CancelFollowUpCommand(int FollowUpId, int PractitionerId, string? Reason = null);

/// <summary>
///     CR-4 - Cancel the future visits of a patient when the treatment ends: discharge, or a switch of practitioner
///     (CR-1). Issued by the policies on Treatment Discharged and Care Link Revoked only, never by an endpoint.
/// </summary>
/// <param name="PatientId">Whose visits.</param>
/// <param name="Reason">"Discharged" or "SwitchedPractitioner", at most 30 characters.</param>
/// <param name="PractitionerId">
///     Only the visits with this practitioner (the one whose link ended); null cancels them all.
/// </param>
public record CancelFollowUpsForPatientCommand(int PatientId, string Reason, int? PractitionerId = null);

/// <summary>MA-5 - Reschedule Follow Up. Issued by the practitioner whose agenda the visit is on.</summary>
/// <param name="FollowUpId">The visit.</param>
/// <param name="PractitionerId">Who moves it, from the token.</param>
/// <param name="ScheduledFor">The new moment; it has to be in the future.</param>
/// <param name="Preparation">New preparation codes; null keeps the current ones.</param>
public record RescheduleFollowUpCommand(
    int FollowUpId,
    int PractitionerId,
    DateTimeOffset ScheduledFor,
    IReadOnlyList<string>? Preparation = null);

/// <summary>
///     MA-4 - Submit Pre Visit Check In. Issued by the patient (PT25.2 "Enviar a mi nutricionista" and PT25.3
///     "Editar mi respuesta"): creates the check in of the visit, or edits it.
/// </summary>
/// <remarks>
///     Business rule: Check In Raises No Signal (MA-4). Nothing on this record reaches a window, a deviation or the
///     consistency index; the check in is voluntary and is read by a person.
/// </remarks>
/// <param name="FollowUpId">The visit it is for.</param>
/// <param name="PatientId">Who writes it, from the token.</param>
/// <param name="Feeling">Good, Fair or Hard. Required.</param>
/// <param name="Difficulties">Dinners, Weekends, EatingOut, Schedules, Cravings; may be empty.</param>
/// <param name="Questions">The questions for the practitioner; may be empty.</param>
public record SubmitPreVisitCheckInCommand(
    int FollowUpId,
    int PatientId,
    string? Feeling,
    IReadOnlyList<string>? Difficulties = null,
    IReadOnlyList<PreVisitCheckInQuestionInput>? Questions = null);

/// <summary>MA-4. One question of <see cref="SubmitPreVisitCheckInCommand" />.</summary>
/// <param name="Text">The question, 3 to 300 characters.</param>
/// <param name="Origin">Patient (default) or AiSuggested.</param>
/// <param name="AiGenerationId">The AI generation it came from, when suggested.</param>
/// <param name="Language">X-2. es or en: the language of an AI suggestion. Ignored for the patient's own questions.</param>
public record PreVisitCheckInQuestionInput(string Text, string? Origin = null, long? AiGenerationId = null,
    string? Language = null);

/// <summary>
///     Subflow 5.11 - Close Evaluation Window. Issued by the policy that reacts to Care Link Revoked,
///     and by the one that reacts to Care Link Established when a window of a previous link is still
///     open (CR-1). Never by an endpoint.
/// </summary>
/// <param name="PatientId">Whose window is closing.</param>
/// <param name="RevokedAt">The moment the link was revoked, or the moment it was superseded.</param>
/// <param name="CareLinkId">
///     CR-1. The link whose window is closing. When given, only an open window of that link is
///     closed, so a late revocation of a previous link never closes the window of the new one. Null
///     closes whichever window is open.
/// </param>
public record CloseEvaluationWindowCommand(int PatientId, DateTimeOffset RevokedAt, int? CareLinkId = null);

/// <summary>
///     IA-2 - Generate Weekly Summary. Issued by <c>WeeklySummaryHostedService</c> for each patient with an open
///     window, never by an endpoint. Idempotent: a week already summarized is returned as it is.
/// </summary>
/// <param name="PatientId">Whose week.</param>
/// <param name="WeekStart">Monday of the week, on the clinical calendar.</param>
public record GenerateWeeklySummaryCommand(int PatientId, DateOnly WeekStart);

/// <summary>
///     IA-4 - Suggest Questions (PT25 "Prepara tu consulta"). One generation per visit, or per week without one;
///     generated again when the check in of the visit changes.
/// </summary>
/// <param name="PatientId">Who asks, from the token.</param>
/// <param name="FollowUpId">The visit the questions are for; null for the week.</param>
public record SuggestQuestionsCommand(int PatientId, int? FollowUpId);

/// <summary>
///     IA-5 - Summarize Monitoring (PAC-2 "Resumen generado con IA"). Cached six hours per patient and range.
/// </summary>
/// <param name="PatientId">Whose period.</param>
/// <param name="PractitionerId">Who reads it, from the token.</param>
/// <param name="From">First day; null for the last seven days.</param>
/// <param name="To">Last day; null for yesterday.</param>
public record SummarizeMonitoringCommand(int PatientId, int PractitionerId, DateOnly? From = null, DateOnly? To = null);

/// <summary>
///     IA-1/CR-2 (§12-#14) - Purge Monitoring AI Content. Issued by the policies that react to the AI consent being
///     withdrawn or a function being turned off. Deletes what this context generated for the patient.
/// </summary>
/// <param name="PatientId">Whose content.</param>
/// <param name="WeeklySummaries">IA-2: the weekly summaries.</param>
/// <param name="SuggestedQuestions">IA-4: the cached suggested questions.</param>
/// <param name="MonitoringSummaries">IA-5: the cached monitoring summaries.</param>
public record PurgeMonitoringAiContentCommand(
    int PatientId,
    bool WeeklySummaries,
    bool SuggestedQuestions,
    bool MonitoringSummaries);

/// <summary>§12-#14 - Retention of the weekly summaries: the ones generated before the cut are deleted.</summary>
/// <param name="GeneratedBefore">The cut (now minus <c>Ai:RetentionDays</c>).</param>
/// <param name="BatchSize">How many to delete in one cycle.</param>
public record PurgeExpiredWeeklySummariesCommand(DateTimeOffset GeneratedBefore, int BatchSize = 500);
