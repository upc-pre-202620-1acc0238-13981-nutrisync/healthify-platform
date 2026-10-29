namespace Healthify.Platform.ReadModels.Interfaces.REST.Resources;

/// <summary>
///     The summary of one patient. <c>Baseline</c> null means the patient has no baseline yet and
///     the app shows the empty state.
/// </summary>
/// <param name="PatientId">Identifier of the patient.</param>
/// <param name="FullName">"Ana Flores", or null when it could not be read.</param>
/// <param name="LinkedSince">When the care link was established, or null when it could not be read.</param>
/// <param name="LinkStatus">Active or PendingConsent.</param>
/// <param name="ActivePlanVersion">The published version in force, or null (no plan).</param>
/// <param name="Baseline">Sex, age, height and medical history; null means the empty state.</param>
/// <param name="NextFollowUp">The next visit on the calendar, or null.</param>
/// <param name="ConsultationInProgress">The consultation to resume, or null.</param>
/// <param name="SinceLastConsultation">How things went since the last consultation.</param>
public record PatientSummaryResource(
    int PatientId,
    string? FullName,
    DateTimeOffset? LinkedSince,
    string LinkStatus,
    int? ActivePlanVersion,
    PatientBaselineSummaryResource? Baseline,
    NextFollowUpResource? NextFollowUp,
    ConsultationInProgressResource? ConsultationInProgress,
    SinceLastConsultationResource SinceLastConsultation)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>"Ana Flores", for the header. Null when Iam could not answer.</summary>
    public string? FullName { get; init; } = FullName;

    /// <summary>When the link was established. Null when Care Relationship could not answer.</summary>
    public DateTimeOffset? LinkedSince { get; init; } = LinkedSince;

    /// <summary>Active or PendingConsent.</summary>
    public string LinkStatus { get; init; } = LinkStatus;

    /// <summary>The plan version in force. Null when no plan is published.</summary>
    public int? ActivePlanVersion { get; init; } = ActivePlanVersion;

    /// <summary>The baseline. Null means the patient has none yet: the app shows the empty state.</summary>
    public PatientBaselineSummaryResource? Baseline { get; init; } = Baseline;

    /// <summary>The next consultation. Null when nothing is on the calendar.</summary>
    public NextFollowUpResource? NextFollowUp { get; init; } = NextFollowUp;

    /// <summary>The consultation in progress. Null when there is none.</summary>
    public ConsultationInProgressResource? ConsultationInProgress { get; init; } = ConsultationInProgress;

    /// <summary>What changed since the last consultation.</summary>
    public SinceLastConsultationResource SinceLastConsultation { get; init; } = SinceLastConsultation;
}

/// <summary>The baseline: sex, age, height and conditions. Practitioner only.</summary>
/// <param name="BiologicalSex">Female or Male.</param>
/// <param name="AgeYears">Completed years today.</param>
/// <param name="HeightCm">Height in centimetres.</param>
/// <param name="Conditions">Medical history codes of the closed list; empty means none.</param>
public record PatientBaselineSummaryResource(
    string BiologicalSex,
    int AgeYears,
    decimal HeightCm,
    IReadOnlyList<string> Conditions)
{
    /// <summary>Female or Male.</summary>
    public string BiologicalSex { get; init; } = BiologicalSex;

    /// <summary>Completed years today, in the time zone of the practice.</summary>
    public int AgeYears { get; init; } = AgeYears;

    /// <summary>Height in centimetres.</summary>
    public decimal HeightCm { get; init; } = HeightCm;

    /// <summary>Medical history codes from the closed list. Empty means none.</summary>
    public IReadOnlyList<string> Conditions { get; init; } = Conditions;
}

/// <summary>The next consultation. No clinical note travels with it.</summary>
/// <param name="FollowUpId">Identifier of the visit.</param>
/// <param name="ScheduledFor">When it is.</param>
/// <param name="Modality">InPerson or Remote.</param>
/// <param name="Preparation">Preparation codes; empty when none.</param>
/// <param name="ScheduledAt">When it was put on the calendar.</param>
public record NextFollowUpResource(
    int FollowUpId,
    DateTimeOffset ScheduledFor,
    string Modality,
    IReadOnlyList<string> Preparation,
    DateTimeOffset? ScheduledAt)
{
    /// <summary>Identifier of the visit.</summary>
    public int FollowUpId { get; init; } = FollowUpId;

    /// <summary>When the visit is.</summary>
    public DateTimeOffset ScheduledFor { get; init; } = ScheduledFor;

    /// <summary>InPerson or Remote.</summary>
    public string Modality { get; init; } = Modality;

    /// <summary>Fasting, LightClothing, BringBloodTests or EmptyBladder. Empty when none.</summary>
    public IReadOnlyList<string> Preparation { get; init; } = Preparation;

    /// <summary>When the visit was put on the calendar.</summary>
    public DateTimeOffset? ScheduledAt { get; init; } = ScheduledAt;
}

/// <summary>The consultation in progress: its step out of 4 and when it was last saved.</summary>
/// <param name="ConsultationId">The consultation to resume.</param>
/// <param name="StepNumber">1 to 4.</param>
/// <param name="StepName">Measurement, Diagnosis, Targets or Publication.</param>
/// <param name="LastSavedAt">When a step was last saved.</param>
public record ConsultationInProgressResource(
    int ConsultationId,
    int StepNumber,
    string StepName,
    DateTimeOffset LastSavedAt)
{
    /// <summary>The consultation to resume.</summary>
    public int ConsultationId { get; init; } = ConsultationId;

    /// <summary>The step, out of 4.</summary>
    public int StepNumber { get; init; } = StepNumber;

    /// <summary>Measurement, Diagnosis, Targets or Publication.</summary>
    public string StepName { get; init; } = StepName;

    /// <summary>When a step was last saved.</summary>
    public DateTimeOffset LastSavedAt { get; init; } = LastSavedAt;
}

/// <summary>
///     Since the last consultation: the home weigh in trend per week over four weeks, and compliance as "5 of 7
///     days". Never a single day's weight.
/// </summary>
/// <param name="FromDate">The day the section starts: the last consultation, or 7 days ago before the first.</param>
/// <param name="WeightSlopeKgPerWeek">Slope of the smoothed home series, or null without enough points.</param>
/// <param name="TrendWeeks">How many weeks the trend covers.</param>
/// <param name="Compliance">Days within targets out of calendar days, or null when it could not be read.</param>
public record SinceLastConsultationResource(
    DateOnly FromDate,
    decimal? WeightSlopeKgPerWeek,
    int TrendWeeks,
    ComplianceRatioResource? Compliance)
{
    /// <summary>The first day covered: the day of the last consultation, or 7 days ago before the first one.</summary>
    public DateOnly FromDate { get; init; } = FromDate;

    /// <summary>Kilograms per week. Null with fewer than two smoothed points.</summary>
    public decimal? WeightSlopeKgPerWeek { get; init; } = WeightSlopeKgPerWeek;

    /// <summary>How many weeks the trend covers.</summary>
    public int TrendWeeks { get; init; } = TrendWeeks;

    /// <summary>"5 of 7 days". Null when Monitoring could not answer.</summary>
    public ComplianceRatioResource? Compliance { get; init; } = Compliance;
}

/// <summary>
///     "5 of 7 days": days within targets out of calendar days. A day nobody logged is in the total and
///     never counts as below target.
/// </summary>
/// <param name="Met">Days within targets.</param>
/// <param name="Total">Calendar days of the range.</param>
public record ComplianceRatioResource(int Met, int Total)
{
    /// <summary>Days within targets.</summary>
    public int Met { get; init; } = Met;

    /// <summary>Calendar days of the range, logged or not.</summary>
    public int Total { get; init; } = Total;
}
