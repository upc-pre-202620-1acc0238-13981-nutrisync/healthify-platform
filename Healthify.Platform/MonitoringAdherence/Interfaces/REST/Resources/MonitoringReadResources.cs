namespace Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;

/// <summary>
///     One evaluated day. Read models Daily Compliance Indicator and Patient Monitoring Panel.
/// </summary>
/// <remarks>
///     Business rule: Day Without Entries Marked Unlogged Not Non Compliant (Subflow 5.4). There are
///     four outcomes on this resource and <c>Unlogged</c> is one of them, sitting beside the other
///     three rather than below them. There is no streak here, no penalty and no grade, and nothing on
///     this resource says that a day was a failure.
/// </remarks>
/// <param name="Date">The calendar day the patient was living.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged.</param>
/// <param name="ObservedEnergyKcal">What the confirmed entries of that day added up to.</param>
/// <param name="TargetEnergyKcal">The energy target in force that day.</param>
/// <param name="PlanVersion">Version of the contract the day was evaluated against.</param>
/// <param name="EntryCount">How many entries the diary held that day.</param>
public record DailyComplianceResource(
    DateOnly Date,
    string Outcome,
    decimal ObservedEnergyKcal,
    decimal TargetEnergyKcal,
    int PlanVersion,
    int EntryCount)
{
    /// <summary>The calendar day the patient was living, never the server day.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>Met, Exceeded, Short or Unlogged. An unlogged day is a fact, not a verdict.</summary>
    public string Outcome { get; init; } = Outcome;

    /// <summary>What the confirmed entries of that day added up to.</summary>
    public decimal ObservedEnergyKcal { get; init; } = ObservedEnergyKcal;

    /// <summary>The energy target that was in force on that day, not the one in force now.</summary>
    public decimal TargetEnergyKcal { get; init; } = TargetEnergyKcal;

    /// <summary>Version of the published contract the day was evaluated against.</summary>
    public int PlanVersion { get; init; } = PlanVersion;

    /// <summary>How many entries the diary held that day.</summary>
    public int EntryCount { get; init; } = EntryCount;
}

/// <summary>
///     MA-6. The Daily Compliance Indicator over a range (PAC-2 "Esta semana · 5 de 7 días", PT13.2, PT20).
/// </summary>
/// <param name="From">First day of the range.</param>
/// <param name="To">Last day of the range.</param>
/// <param name="Days">Every calendar day of the range, oldest first.</param>
/// <param name="Summary">The same days counted by outcome.</param>
public record DailyComplianceRangeResource(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ComplianceDayResource> Days,
    ComplianceSummaryResource Summary)
{
    /// <summary>First day of the range, included.</summary>
    public DateOnly From { get; init; } = From;

    /// <summary>Last day of the range, included.</summary>
    public DateOnly To { get; init; } = To;

    /// <summary>Every calendar day of the range, oldest first. A day never evaluated is Unlogged.</summary>
    public IReadOnlyList<ComplianceDayResource> Days { get; init; } = Days;

    /// <summary>The days counted by outcome.</summary>
    public ComplianceSummaryResource Summary { get; init; } = Summary;
}

/// <summary>MA-6. One calendar day of a range.</summary>
/// <param name="Date">The calendar day the patient was living.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged.</param>
public record ComplianceDayResource(DateOnly Date, string Outcome)
{
    /// <summary>The calendar day the patient was living, never the server day.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>
    ///     Met, Exceeded, Short or Unlogged. Paint Unlogged as its own grey state ("Sin registro"), never as
    ///     "Por debajo".
    /// </summary>
    public string Outcome { get; init; } = Outcome;
}

/// <summary>
///     MA-6. The days of a range counted by outcome. "5 de 7 días" is <c>MetDays</c> of <c>TotalDays</c>
///     (DECISIÓN §12-#7: the denominator is the calendar days, unlogged ones included).
/// </summary>
/// <param name="MetDays">Days within the targets.</param>
/// <param name="ExceededDays">Days above.</param>
/// <param name="ShortDays">Days below.</param>
/// <param name="UnloggedDays">Days without a confirmed entry, evaluated or not.</param>
/// <param name="LoggedDays">Days with something logged ("Registro 6 de 7 días").</param>
/// <param name="TotalDays">Calendar days of the range.</param>
public record ComplianceSummaryResource(
    int MetDays,
    int ExceededDays,
    int ShortDays,
    int UnloggedDays,
    int LoggedDays,
    int TotalDays)
{
    /// <summary>Days within the targets.</summary>
    public int MetDays { get; init; } = MetDays;

    /// <summary>Days above the targets.</summary>
    public int ExceededDays { get; init; } = ExceededDays;

    /// <summary>Days below the targets.</summary>
    public int ShortDays { get; init; } = ShortDays;

    /// <summary>Days without a confirmed entry. Not a bad day: an absence of data.</summary>
    public int UnloggedDays { get; init; } = UnloggedDays;

    /// <summary>Days with something logged: Met + Exceeded + Short.</summary>
    public int LoggedDays { get; init; } = LoggedDays;

    /// <summary>Calendar days of the range: the denominator.</summary>
    public int TotalDays { get; init; } = TotalDays;
}

/// <summary>The targets as they stood at one moment, frozen.</summary>
/// <param name="PlanVersion">Version of the published contract that was frozen.</param>
/// <param name="EnergyKcal">Daily energy target in kilocalories.</param>
/// <param name="ProteinG">Daily protein target in grams.</param>
/// <param name="CarbG">Daily carbohydrate target in grams.</param>
/// <param name="FatG">Daily fat target in grams.</param>
/// <param name="TakenAt">The moment these targets came into force.</param>
public record TargetsSnapshotResource(
    int PlanVersion,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    DateTimeOffset TakenAt)
{
    /// <summary>Version of the published contract that was frozen.</summary>
    public int PlanVersion { get; init; } = PlanVersion;

    /// <summary>Daily energy target in kilocalories.</summary>
    public decimal EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Daily protein target in grams.</summary>
    public decimal ProteinG { get; init; } = ProteinG;

    /// <summary>Daily carbohydrate target in grams.</summary>
    public decimal CarbG { get; init; } = CarbG;

    /// <summary>Daily fat target in grams.</summary>
    public decimal FatG { get; init; } = FatG;

    /// <summary>The moment these targets came into force.</summary>
    public DateTimeOffset TakenAt { get; init; } = TakenAt;
}

/// <summary>
///     One point of the clinical weight series.
/// </summary>
/// <remarks>
///     Business rule: Two Series Never Merged (Subflow 5.3). The source is on every point, always,
///     because a reading taken by a practitioner under a recorded protocol and a reading taken at
///     home are not the same kind of fact and whoever reads them is entitled to know which they have.
/// </remarks>
/// <param name="Date">The day the measurement was taken.</param>
/// <param name="ValueKg">The reading in kilograms.</param>
/// <param name="Source">Always <c>ClinicalMeasurement</c>.</param>
public record AnthropometryPointResource(DateOnly Date, decimal ValueKg, string Source)
{
    /// <summary>The day the measurement was taken.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>The reading in kilograms.</summary>
    public decimal ValueKg { get; init; } = ValueKg;

    /// <summary>Always <c>ClinicalMeasurement</c>.</summary>
    public string Source { get; init; } = Source;
}

/// <summary>What the window has seen so far, added up.</summary>
/// <param name="LoggedDays">Days the patient wrote something in.</param>
/// <param name="UnloggedDays">Days with an empty diary. Never a deviation.</param>
/// <param name="TotalEnergyKcal">What the logged days added up to.</param>
/// <param name="MeanObservedEnergyKcal">Mean energy across the logged days.</param>
/// <param name="MeanTargetEnergyKcal">Mean target across the same logged days.</param>
public record IntakeSummaryResource(
    int LoggedDays,
    int UnloggedDays,
    decimal TotalEnergyKcal,
    decimal MeanObservedEnergyKcal,
    decimal MeanTargetEnergyKcal)
{
    /// <summary>Days the patient wrote something in.</summary>
    public int LoggedDays { get; init; } = LoggedDays;

    /// <summary>Days with an empty diary. Counted apart and never added to the intake.</summary>
    public int UnloggedDays { get; init; } = UnloggedDays;

    /// <summary>What the logged days added up to.</summary>
    public decimal TotalEnergyKcal { get; init; } = TotalEnergyKcal;

    /// <summary>Mean energy across the logged days.</summary>
    public decimal MeanObservedEnergyKcal { get; init; } = MeanObservedEnergyKcal;

    /// <summary>Mean target across the same logged days.</summary>
    public decimal MeanTargetEnergyKcal { get; init; } = MeanTargetEnergyKcal;
}

/// <summary>
///     Read model Patient Monitoring Panel, window section.
/// </summary>
/// <remarks>
///     Business rules: Two Series Never Merged (Subflow 5.3) and Evaluated Data Is Preserved
///     (Subflow 5.11). The two series appear as two fields and a closed window still returns
///     everything it holds.
/// </remarks>
/// <param name="WindowId">Identifier of the window.</param>
/// <param name="PatientId">Identifier of the patient.</param>
/// <param name="From">The first day it counts.</param>
/// <param name="To">The last day it counts.</param>
/// <param name="State">Open or Closed.</param>
/// <param name="ClosedAt">When the care link was revoked, if it was.</param>
/// <param name="LoggedDaysCount">How many days the patient wrote something in.</param>
/// <param name="IntakeSummary">What the window has seen, added up.</param>
/// <param name="CurrentTargets">The most recent snapshot, if there is one.</param>
/// <param name="TargetsSnapshots">Every snapshot the window was given, oldest first.</param>
/// <param name="DailyComplianceSeries">The day-by-day outcome, oldest first.</param>
/// <param name="AnthropometrySeries">The clinical weight series, oldest first.</param>
public record EvaluationWindowResource(
    int WindowId,
    int PatientId,
    DateOnly From,
    DateOnly To,
    string State,
    DateTimeOffset? ClosedAt,
    int LoggedDaysCount,
    IntakeSummaryResource IntakeSummary,
    TargetsSnapshotResource? CurrentTargets,
    IReadOnlyList<TargetsSnapshotResource> TargetsSnapshots,
    IReadOnlyList<DailyComplianceResource> DailyComplianceSeries,
    IReadOnlyList<AnthropometryPointResource> AnthropometrySeries)
{
    /// <summary>Identifier of the window.</summary>
    public int WindowId { get; init; } = WindowId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The first day this window counts.</summary>
    public DateOnly From { get; init; } = From;

    /// <summary>The last day this window counts.</summary>
    public DateOnly To { get; init; } = To;

    /// <summary>Open or Closed. A closed window has stopped counting and kept everything.</summary>
    public string State { get; init; } = State;

    /// <summary>When the care link was revoked, if it was.</summary>
    public DateTimeOffset? ClosedAt { get; init; } = ClosedAt;

    /// <summary>How many days the patient wrote something in.</summary>
    public int LoggedDaysCount { get; init; } = LoggedDaysCount;

    /// <summary>What the window has seen, added up.</summary>
    public IntakeSummaryResource IntakeSummary { get; init; } = IntakeSummary;

    /// <summary>The most recent snapshot, or null while no contract has been published.</summary>
    public TargetsSnapshotResource? CurrentTargets { get; init; } = CurrentTargets;

    /// <summary>Every snapshot the window was given. A later adjustment never rewrites an older one.</summary>
    public IReadOnlyList<TargetsSnapshotResource> TargetsSnapshots { get; init; } = TargetsSnapshots;

    /// <summary>The day-by-day outcome, oldest first.</summary>
    public IReadOnlyList<DailyComplianceResource> DailyComplianceSeries { get; init; } =
        DailyComplianceSeries;

    /// <summary>
    ///     The clinical weight series. The readings the patient takes at home are a separate series,
    ///     owned by a separate context, and are never in here.
    /// </summary>
    public IReadOnlyList<AnthropometryPointResource> AnthropometrySeries { get; init; } =
        AnthropometrySeries;
}

/// <summary>
///     Read model Patient Monitoring Panel, deviations section. Read by the practitioner.
/// </summary>
/// <remarks>
///     Business rule: Only Logged Days Count (Subflow 5.6). The two counts are on the resource so
///     that a practitioner can see what the reading is built on: five deviating days out of six
///     logged and five out of thirty are different situations, and the second one is mostly silence.
/// </remarks>
/// <param name="DeviationId">Identifier of the deviation.</param>
/// <param name="PatientId">Identifier of the patient.</param>
/// <param name="WindowId">The window it was read from.</param>
/// <param name="MagnitudeRelativeValue">Mean distance from target, as a fraction of the target.</param>
/// <param name="MagnitudeEnergyKcal">The same distance in kilocalories per day.</param>
/// <param name="Direction">Above or Below.</param>
/// <param name="DetectedAt">When it was last read.</param>
/// <param name="IsSustained">Whether it persisted across the majority of the logged days.</param>
/// <param name="SustainedAt">When it became sustained, if it did.</param>
/// <param name="LoggedDaysConsidered">How many logged days the horizon held.</param>
/// <param name="DeviatingDaysConsidered">How many of them deviated in this direction.</param>
/// <param name="Evidence">What was observed, in one sentence. Evidence, not a verdict.</param>
public record DeviationResource(
    int DeviationId,
    int PatientId,
    int WindowId,
    decimal MagnitudeRelativeValue,
    decimal MagnitudeEnergyKcal,
    string Direction,
    DateTimeOffset DetectedAt,
    bool IsSustained,
    DateTimeOffset? SustainedAt,
    int LoggedDaysConsidered,
    int DeviatingDaysConsidered,
    string Evidence)
{
    /// <summary>Identifier of the deviation.</summary>
    public int DeviationId { get; init; } = DeviationId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The window it was read from.</summary>
    public int WindowId { get; init; } = WindowId;

    /// <summary>Mean distance from target across the deviating days, as a fraction of the target.</summary>
    public decimal MagnitudeRelativeValue { get; init; } = MagnitudeRelativeValue;

    /// <summary>The same distance in kilocalories per day.</summary>
    public decimal MagnitudeEnergyKcal { get; init; } = MagnitudeEnergyKcal;

    /// <summary>Above or Below. A direction on a number line, not a judgement.</summary>
    public string Direction { get; init; } = Direction;

    /// <summary>When it was last read.</summary>
    public DateTimeOffset DetectedAt { get; init; } = DetectedAt;

    /// <summary>Whether it persisted across the majority of the logged days.</summary>
    public bool IsSustained { get; init; } = IsSustained;

    /// <summary>When it became sustained, if it did. That is the moment it reached the review inbox.</summary>
    public DateTimeOffset? SustainedAt { get; init; } = SustainedAt;

    /// <summary>How many logged days the horizon held.</summary>
    public int LoggedDaysConsidered { get; init; } = LoggedDaysConsidered;

    /// <summary>How many of them deviated in this direction.</summary>
    public int DeviatingDaysConsidered { get; init; } = DeviatingDaysConsidered;

    /// <summary>What was observed, in one sentence.</summary>
    public string Evidence { get; init; } = Evidence;
}

/// <summary>
///     Read model Consistency Card, the one the patient sees.
/// </summary>
/// <remarks>
///     Business rules: Patient First Always and Patient Prompt Required Before Escalation (Subflows
///     5.7 and 5.8). Both dates are on the resource so the patient can see, in the same place, that
///     they were asked first and whether their practitioner has been told. The patient knows this
///     escalation exists and is told before it happens.
/// </remarks>
/// <param name="PatientId">Identifier of the patient.</param>
/// <param name="Value">Unexplained weight movement in kilograms per week.</param>
/// <param name="State">Normal, Watch or Alert.</param>
/// <param name="FirstFlaggedAt">When the index first left Normal in the current episode.</param>
/// <param name="ShownToPatientAt">When the patient saw the prompt (MA-7).</param>
/// <param name="EscalatedAt">When the practitioner was told.</param>
/// <param name="LastRecomputedAt">When the index was last recomputed.</param>
/// <param name="PatientPromptPending">MA-7. A prompt the patient has not seen yet.</param>
public record ConsistencyIndexResource(
    int PatientId,
    decimal Value,
    string State,
    DateTimeOffset? FirstFlaggedAt,
    DateTimeOffset? ShownToPatientAt,
    DateTimeOffset? EscalatedAt,
    DateTimeOffset LastRecomputedAt,
    bool PatientPromptPending = false)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>
    ///     Unexplained weight movement in kilograms per week: the distance between what the weight
    ///     trend shows and what the recorded intake implies.
    /// </summary>
    public decimal Value { get; init; } = Value;

    /// <summary>Normal, Watch or Alert. A statement about data, not about a person.</summary>
    public string State { get; init; } = State;

    /// <summary>When the index first left Normal in the current episode.</summary>
    public DateTimeOffset? FirstFlaggedAt { get; init; } = FirstFlaggedAt;

    /// <summary>
    ///     When the patient saw the prompt (MA-7: acknowledged by the app). Always before the practitioner is told.
    /// </summary>
    public DateTimeOffset? ShownToPatientAt { get; init; } = ShownToPatientAt;

    /// <summary>When the practitioner was told, if they have been.</summary>
    public DateTimeOffset? EscalatedAt { get; init; } = EscalatedAt;

    /// <summary>When the index was last recomputed.</summary>
    public DateTimeOffset LastRecomputedAt { get; init; } = LastRecomputedAt;

    /// <summary>
    ///     MA-7. True when there is a prompt the patient has not seen yet: show the PT3 card and then call
    ///     POST /patients/{id}/consistency-index/prompt-acknowledgement.
    /// </summary>
    public bool PatientPromptPending { get; init; } = PatientPromptPending;
}

/// <summary>Read model Patient Record, referrals section.</summary>
/// <param name="ReferralId">Identifier of the referral.</param>
/// <param name="PatientId">Identifier of the patient.</param>
/// <param name="Specialty">Which speciality.</param>
/// <param name="Reason">Why.</param>
/// <param name="IssuedBy">Identifier of the practitioner who issued it.</param>
/// <param name="IssuedAt">When.</param>
/// <param name="Status">RM-4. Open or Closed.</param>
/// <param name="ClosedAt">RM-4. When it was closed, or null.</param>
public record ReferralResource(
    int ReferralId,
    int PatientId,
    string Specialty,
    string Reason,
    int IssuedBy,
    DateTimeOffset IssuedAt,
    string Status = "Open",
    DateTimeOffset? ClosedAt = null)
{
    /// <summary>Identifier of the referral.</summary>
    public int ReferralId { get; init; } = ReferralId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The speciality the patient was sent to.</summary>
    public string Specialty { get; init; } = Specialty;

    /// <summary>Why the referral was made.</summary>
    public string Reason { get; init; } = Reason;

    /// <summary>Identifier of the practitioner who issued it.</summary>
    public int IssuedBy { get; init; } = IssuedBy;

    /// <summary>When it was issued.</summary>
    public DateTimeOffset IssuedAt { get; init; } = IssuedAt;

    /// <summary>RM-4. Open ("en curso") or Closed.</summary>
    public string Status { get; init; } = Status;

    /// <summary>RM-4. When the practitioner closed it, or null while it is open.</summary>
    public DateTimeOffset? ClosedAt { get; init; } = ClosedAt;
}

/// <summary>
///     Read model Practitioner Agenda.
/// </summary>
/// <remarks>
///     Business rule: Missed Visit Does Not Close The Care Link (Subflow 5.10). <c>Missed</c> is a
///     state on this row and nothing else in the platform reads it.
/// </remarks>
/// <param name="FollowUpId">Identifier of the visit.</param>
/// <param name="PatientId">Identifier of the patient.</param>
/// <param name="PractitionerId">Identifier of the practitioner.</param>
/// <param name="ScheduledFor">When the visit is, or was.</param>
/// <param name="State">Scheduled, Completed or Missed.</param>
/// <param name="MissedAt">When it was flagged missed, if it was.</param>
/// <param name="PatientFullName">MA-2/IAM-1. "Ana Flores", or null when it could not be read.</param>
/// <param name="Preparation">MA-2. How the patient should prepare.</param>
/// <param name="Modality">MA-2. InPerson or Remote.</param>
/// <param name="ScheduledAt">MA-2. When the visit was put on the calendar.</param>
/// <param name="CompletedAt">MA-2. When the consultation that completed it was published.</param>
/// <param name="CancelledAt">MA-2. When it was cancelled.</param>
public record ScheduledFollowUpResource(
    int FollowUpId,
    int PatientId,
    int PractitionerId,
    DateTimeOffset ScheduledFor,
    string State,
    DateTimeOffset? MissedAt,
    string? PatientFullName = null,
    IReadOnlyList<string>? Preparation = null,
    string Modality = "InPerson",
    DateTimeOffset? ScheduledAt = null,
    DateTimeOffset? CompletedAt = null,
    DateTimeOffset? CancelledAt = null)
{
    /// <summary>Identifier of the visit.</summary>
    public int FollowUpId { get; init; } = FollowUpId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Identifier of the practitioner whose agenda it is on.</summary>
    public int PractitionerId { get; init; } = PractitionerId;

    /// <summary>When the visit is, or was.</summary>
    public DateTimeOffset ScheduledFor { get; init; } = ScheduledFor;

    /// <summary>Scheduled, Completed, Missed or Cancelled.</summary>
    public string State { get; init; } = State;

    /// <summary>When it was flagged missed, if it was. It closes nothing.</summary>
    public DateTimeOffset? MissedAt { get; init; } = MissedAt;

    /// <summary>MA-2/IAM-1. Name of the patient (PR17.0 "Ana Flores"); null when Iam could not answer.</summary>
    public string? PatientFullName { get; init; } = PatientFullName;

    /// <summary>MA-2. Preparation codes (Fasting, LightClothing, BringBloodTests, EmptyBladder). Empty when none.</summary>
    public IReadOnlyList<string> Preparation { get; init; } = Preparation ?? [];

    /// <summary>MA-2. InPerson or Remote.</summary>
    public string Modality { get; init; } = Modality;

    /// <summary>MA-2. When the visit was put on the calendar ("Agendada el 4 de septiembre").</summary>
    public DateTimeOffset? ScheduledAt { get; init; } = ScheduledAt;

    /// <summary>MA-2. When the consultation that completed it was published.</summary>
    public DateTimeOffset? CompletedAt { get; init; } = CompletedAt;

    /// <summary>MA-2. When it was cancelled.</summary>
    public DateTimeOffset? CancelledAt { get; init; } = CancelledAt;
}

/// <summary>
///     MA-3. A visit as the patient reads it (PT3 and PT20 "Próxima consulta", PT25 and PT25.1).
/// </summary>
/// <remarks>
///     No clinical note and no diagnosis travel with a visit: there is nothing on this record to carry one.
/// </remarks>
/// <param name="FollowUpId">Identifier of the visit.</param>
/// <param name="ScheduledFor">When it is, or was.</param>
/// <param name="Modality">InPerson or Remote.</param>
/// <param name="Preparation">Preparation codes; empty when none.</param>
/// <param name="ScheduledAt">When it was put on the calendar.</param>
/// <param name="PractitionerFullName">Name of the practitioner; null when Iam could not answer.</param>
/// <param name="State">Scheduled, Completed, Missed or Cancelled.</param>
public record PatientFollowUpResource(
    int FollowUpId,
    DateTimeOffset ScheduledFor,
    string Modality,
    IReadOnlyList<string> Preparation,
    DateTimeOffset? ScheduledAt,
    string? PractitionerFullName,
    string State)
{
    /// <summary>Identifier of the visit.</summary>
    public int FollowUpId { get; init; } = FollowUpId;

    /// <summary>When the visit is ("Jue 18 sept. · 10:00 a. m."), or was.</summary>
    public DateTimeOffset ScheduledFor { get; init; } = ScheduledFor;

    /// <summary>InPerson or Remote.</summary>
    public string Modality { get; init; } = Modality;

    /// <summary>How to prepare (Fasting, LightClothing, BringBloodTests, EmptyBladder). Empty when none.</summary>
    public IReadOnlyList<string> Preparation { get; init; } = Preparation;

    /// <summary>When the visit was put on the calendar ("Agendada el 4 de septiembre").</summary>
    public DateTimeOffset? ScheduledAt { get; init; } = ScheduledAt;

    /// <summary>Name of the practitioner who scheduled it; null when it could not be read.</summary>
    public string? PractitionerFullName { get; init; } = PractitionerFullName;

    /// <summary>Scheduled, Completed, Missed or Cancelled.</summary>
    public string State { get; init; } = State;
}

/// <summary>MA-4. One question of a check in, with where it came from.</summary>
/// <param name="Text">The question.</param>
/// <param name="Origin">Patient or AiSuggested.</param>
/// <param name="Language">X-2. es or en for an AI suggestion; null for the patient's own questions and before X-2.</param>
public record CheckInQuestionResource(string Text, string Origin, string? Language = null)
{
    /// <summary>The question.</summary>
    public string Text { get; init; } = Text;

    /// <summary>Patient or AiSuggested.</summary>
    public string Origin { get; init; } = Origin;

    /// <summary>X-2. es or en: the language an AI suggestion was generated in. Null for the patient's own questions.</summary>
    public string? Language { get; init; } = Language;
}

/// <summary>
///     MA-4. The check in of a visit (PT25.3 "Le contaste a tu nutricionista cómo te fue"; EV-2 "Antes de la
///     consulta, … contó").
/// </summary>
/// <param name="FollowUpId">The visit it is for.</param>
/// <param name="Feeling">Good, Fair or Hard.</param>
/// <param name="Difficulties">Dinners, Weekends, EatingOut, Schedules, Cravings; may be empty.</param>
/// <param name="Questions">The questions; may be empty.</param>
/// <param name="SubmittedAt">When it was first sent ("Enviado el 15 de septiembre").</param>
/// <param name="EditedAt">When it was last edited, if it was.</param>
/// <param name="IsLocked">True once it can no longer be edited (the hour of the visit arrived).</param>
public record PreVisitCheckInResource(
    int FollowUpId,
    string Feeling,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<CheckInQuestionResource> Questions,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? EditedAt,
    bool IsLocked)
{
    /// <summary>The visit it is for.</summary>
    public int FollowUpId { get; init; } = FollowUpId;

    /// <summary>Good, Fair or Hard.</summary>
    public string Feeling { get; init; } = Feeling;

    /// <summary>Dinners, Weekends, EatingOut, Schedules, Cravings. Empty when none.</summary>
    public IReadOnlyList<string> Difficulties { get; init; } = Difficulties;

    /// <summary>The questions for the practitioner. Empty when none.</summary>
    public IReadOnlyList<CheckInQuestionResource> Questions { get; init; } = Questions;

    /// <summary>When it was first sent.</summary>
    public DateTimeOffset SubmittedAt { get; init; } = SubmittedAt;

    /// <summary>When it was last edited; null when never.</summary>
    public DateTimeOffset? EditedAt { get; init; } = EditedAt;

    /// <summary>True once "Editar mi respuesta" is no longer possible.</summary>
    public bool IsLocked { get; init; } = IsLocked;
}

/// <summary>IA-2. The figures of the week the summary was written from (MA-6, IN-5), computed without AI.</summary>
/// <param name="MetDays">Days within the targets ("Cumpliste tus metas 5 de 7 días").</param>
/// <param name="TotalDays">Calendar days of the week: always 7.</param>
/// <param name="LoggedDays">Days with something logged ("registraste 6 de 7 días").</param>
/// <param name="UnloggedDays">Days without a record; never counted as missed targets.</param>
/// <param name="WeightChangeKg">Change of the smoothed home series, one decimal; null without a trend.</param>
public record WeeklySummaryFactsResource(
    int MetDays,
    int TotalDays,
    int LoggedDays,
    int UnloggedDays,
    decimal? WeightChangeKg)
{
    /// <summary>Days within the targets.</summary>
    public int MetDays { get; init; } = MetDays;

    /// <summary>Calendar days of the week.</summary>
    public int TotalDays { get; init; } = TotalDays;

    /// <summary>Days with something logged.</summary>
    public int LoggedDays { get; init; } = LoggedDays;

    /// <summary>Days without a record ("Sin registro").</summary>
    public int UnloggedDays { get; init; } = UnloggedDays;

    /// <summary>Change of the smoothed home series over the week, one decimal; null without a trend.</summary>
    public decimal? WeightChangeKg { get; init; } = WeightChangeKg;
}

/// <summary>
///     IA-2. The weekly summary (PT13 "Resumen con IA · Tu semana", PT13.2). Written by the AI from the diary; the
///     app shows "Lo generó la IA a partir de tu diario. Puede equivocarse…".
/// </summary>
/// <param name="WeekStart">Monday ("Semana del 8 al 14 de septiembre").</param>
/// <param name="WeekEnd">Sunday.</param>
/// <param name="Headline">"Cumpliste tus metas 5 de 7 días."</param>
/// <param name="WentWell">"LO QUE SALIÓ BIEN": one to three bullets.</param>
/// <param name="WatchOut">"EN QUÉ FIJARTE": up to two bullets.</param>
/// <param name="Facts">The figures the texts come from.</param>
/// <param name="Language">es or en.</param>
/// <param name="GeneratedAt">When it was generated.</param>
public record WeeklySummaryResource(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    string Headline,
    IReadOnlyList<string> WentWell,
    IReadOnlyList<string> WatchOut,
    WeeklySummaryFactsResource Facts,
    string Language,
    DateTimeOffset GeneratedAt)
{
    /// <summary>Monday of the week.</summary>
    public DateOnly WeekStart { get; init; } = WeekStart;

    /// <summary>Sunday of the week.</summary>
    public DateOnly WeekEnd { get; init; } = WeekEnd;

    /// <summary>The first sentence of the card.</summary>
    public string Headline { get; init; } = Headline;

    /// <summary>What went well: one to three bullets.</summary>
    public IReadOnlyList<string> WentWell { get; init; } = WentWell;

    /// <summary>What to look at: up to two bullets, as an invitation.</summary>
    public IReadOnlyList<string> WatchOut { get; init; } = WatchOut;

    /// <summary>The deterministic figures of the week.</summary>
    public WeeklySummaryFactsResource Facts { get; init; } = Facts;

    /// <summary>es or en.</summary>
    public string Language { get; init; } = Language;

    /// <summary>When it was generated.</summary>
    public DateTimeOffset GeneratedAt { get; init; } = GeneratedAt;
}

/// <summary>IA-4. One suggested question (PT25 chips).</summary>
/// <param name="Id">Identifier within the generation.</param>
/// <param name="Text">The question, in first person.</param>
public record SuggestedQuestionResource(string Id, string Text)
{
    /// <summary>Identifier within the generation.</summary>
    public string Id { get; init; } = Id;

    /// <summary>The question, in first person, ending in a question mark.</summary>
    public string Text { get; init; } = Text;
}

/// <summary>
///     IA-4. Questions the patient could bring to the visit (PT25 "Prepara tu consulta", PT25.2 "También podrías
///     preguntar"). Adding one to the check in sends it with origin AiSuggested and this aiGenerationId (MA-4).
/// </summary>
/// <param name="Questions">Three to five questions.</param>
/// <param name="AiGenerationId">The generation they came from.</param>
/// <param name="BasedOnFrom">First day of the diary read.</param>
/// <param name="BasedOnTo">Last day of the diary read.</param>
/// <param name="Language">X-2. es or en: the language the questions were generated in.</param>
public record SuggestedQuestionsResource(
    IReadOnlyList<SuggestedQuestionResource> Questions,
    long AiGenerationId,
    DateOnly BasedOnFrom,
    DateOnly BasedOnTo,
    string? Language = null)
{
    /// <summary>Three to five questions.</summary>
    public IReadOnlyList<SuggestedQuestionResource> Questions { get; init; } = Questions;

    /// <summary>The generation, to send with an accepted suggestion (MA-4).</summary>
    public long AiGenerationId { get; init; } = AiGenerationId;

    /// <summary>First day of the diary read ("según tu diario de estas semanas").</summary>
    public DateOnly BasedOnFrom { get; init; } = BasedOnFrom;

    /// <summary>Last day of the diary read.</summary>
    public DateOnly BasedOnTo { get; init; } = BasedOnTo;

    /// <summary>X-2. es or en: the language of the questions, to send back with an accepted suggestion (MA-4).</summary>
    public string? Language { get; init; } = Language;
}

/// <summary>IA-5. The facts of the period, computed without AI (MA-6, IN-5).</summary>
/// <param name="MetDays">Days within the targets.</param>
/// <param name="TotalDays">Calendar days of the range.</param>
/// <param name="ShortDays">Weekdays below the target ("Thu", "Sat").</param>
/// <param name="DominantMealSlot">The main slot most often empty on those days (Breakfast, Lunch, Dinner), or null.</param>
/// <param name="ExceededDays">Days above the target.</param>
/// <param name="LoggedDays">Days with something logged.</param>
/// <param name="UnloggedDays">Days without a record; never counted as below.</param>
/// <param name="OffPlanEntryCount">Entries answered as off the plan.</param>
/// <param name="WeightSlopeKgPerWeek">Slope of the smoothed home series, one decimal; null without a trend.</param>
/// <param name="WeightChangeKg">Change of that series over the range, one decimal; null without a trend.</param>
public record MonitoringSummaryFactsResource(
    int MetDays,
    int TotalDays,
    IReadOnlyList<string> ShortDays,
    string? DominantMealSlot,
    int ExceededDays,
    int LoggedDays,
    int UnloggedDays,
    int OffPlanEntryCount,
    decimal? WeightSlopeKgPerWeek,
    decimal? WeightChangeKg)
{
    /// <summary>Days within the targets.</summary>
    public int MetDays { get; init; } = MetDays;

    /// <summary>Calendar days of the range.</summary>
    public int TotalDays { get; init; } = TotalDays;

    /// <summary>Weekdays below the target (Mon to Sun).</summary>
    public IReadOnlyList<string> ShortDays { get; init; } = ShortDays;

    /// <summary>Breakfast, Lunch or Dinner: the slot most often empty on the short days; null when none.</summary>
    public string? DominantMealSlot { get; init; } = DominantMealSlot;

    /// <summary>Days above the target.</summary>
    public int ExceededDays { get; init; } = ExceededDays;

    /// <summary>Days with something logged.</summary>
    public int LoggedDays { get; init; } = LoggedDays;

    /// <summary>Days without a record ("Sin registro").</summary>
    public int UnloggedDays { get; init; } = UnloggedDays;

    /// <summary>Entries answered as off the plan (descriptive).</summary>
    public int OffPlanEntryCount { get; init; } = OffPlanEntryCount;

    /// <summary>Slope of the smoothed home series, kg per week; null without a trend.</summary>
    public decimal? WeightSlopeKgPerWeek { get; init; } = WeightSlopeKgPerWeek;

    /// <summary>Change of the smoothed home series over the range; null without a trend.</summary>
    public decimal? WeightChangeKg { get; init; } = WeightChangeKg;
}

/// <summary>
///     IA-5. Monitoring summary for the practitioner (PAC-2 "Resumen generado con IA · Revísalo antes de usarlo en
///     consulta"). Without the patient's AI consent or with the feature off, only the facts (text null).
/// </summary>
/// <param name="Text">The summary; null in the fallback.</param>
/// <param name="Facts">The facts, always.</param>
/// <param name="From">First day of the range.</param>
/// <param name="To">Last day of the range.</param>
/// <param name="ConsistencyState">Only after a ConsistencyEscalation reached the practitioner; null otherwise.</param>
/// <param name="AiGenerationId">The generation; null in the fallback.</param>
/// <param name="GeneratedAt">When the text was generated or the facts computed.</param>
/// <param name="TextUnavailableReason">AiFeatureDisabled, AiConsentRequired or NotEnoughData; null with a text.</param>
public record MonitoringSummaryResource(
    string? Text,
    MonitoringSummaryFactsResource Facts,
    DateOnly From,
    DateOnly To,
    string? ConsistencyState,
    long? AiGenerationId,
    DateTimeOffset GeneratedAt,
    string? TextUnavailableReason)
{
    /// <summary>The summary written by the AI; null in the deterministic fallback.</summary>
    public string? Text { get; init; } = Text;

    /// <summary>The facts of the period.</summary>
    public MonitoringSummaryFactsResource Facts { get; init; } = Facts;

    /// <summary>First day of the range.</summary>
    public DateOnly From { get; init; } = From;

    /// <summary>Last day of the range.</summary>
    public DateOnly To { get; init; } = To;

    /// <summary>Normal, Watch or Alert, only once escalated to the practitioner; null otherwise.</summary>
    public string? ConsistencyState { get; init; } = ConsistencyState;

    /// <summary>The generation; null in the fallback.</summary>
    public long? AiGenerationId { get; init; } = AiGenerationId;

    /// <summary>When the text was generated or the facts computed.</summary>
    public DateTimeOffset GeneratedAt { get; init; } = GeneratedAt;

    /// <summary>Why there is no text; null when there is one.</summary>
    public string? TextUnavailableReason { get; init; } = TextUnavailableReason;
}
