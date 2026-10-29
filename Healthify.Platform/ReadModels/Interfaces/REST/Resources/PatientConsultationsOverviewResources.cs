namespace Healthify.Platform.ReadModels.Interfaces.REST.Resources;

/// <summary>
///     The patient's consultations screen: the next visit, its check in and the consultations already held. Only what
///     the patient can see: no diagnosis, no calculation basis, no BMI.
/// </summary>
/// <param name="PatientId">Identifier of the patient.</param>
/// <param name="Next">The next visit, or null when there is none.</param>
/// <param name="CheckIn">The check in of the next visit; null means the app shows the reply card.</param>
/// <param name="Past">The consultations already held, most recent first.</param>
public record PatientConsultationsOverviewResource(
    int PatientId,
    UpcomingConsultationResource? Next,
    ConsultationCheckInSummaryResource? CheckIn,
    IReadOnlyList<PastConsultationResource> Past)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The next consultation. Null when there is no visit on the calendar.</summary>
    public UpcomingConsultationResource? Next { get; init; } = Next;

    /// <summary>The check in of the next visit; null shows the reply card.</summary>
    public ConsultationCheckInSummaryResource? CheckIn { get; init; } = CheckIn;

    /// <summary>The earlier consultations, most recent first.</summary>
    public IReadOnlyList<PastConsultationResource> Past { get; init; } = Past;
}

/// <summary>The next visit: with whom, in person or remote, and when it was scheduled.</summary>
/// <param name="FollowUpId">Identifier of the visit.</param>
/// <param name="ScheduledFor">When it is.</param>
/// <param name="Modality">InPerson or Remote.</param>
/// <param name="Preparation">Preparation codes; empty when none.</param>
/// <param name="ScheduledAt">When it was put on the calendar.</param>
/// <param name="PractitionerFullName">Who it is with; null when it could not be read.</param>
public record UpcomingConsultationResource(
    int FollowUpId,
    DateTimeOffset ScheduledFor,
    string Modality,
    IReadOnlyList<string> Preparation,
    DateTimeOffset? ScheduledAt,
    string? PractitionerFullName)
{
    /// <summary>Identifier of the visit, for the check in.</summary>
    public int FollowUpId { get; init; } = FollowUpId;

    /// <summary>When the visit is.</summary>
    public DateTimeOffset ScheduledFor { get; init; } = ScheduledFor;

    /// <summary>InPerson or Remote.</summary>
    public string Modality { get; init; } = Modality;

    /// <summary>Fasting, LightClothing, BringBloodTests, EmptyBladder. Empty when none.</summary>
    public IReadOnlyList<string> Preparation { get; init; } = Preparation;

    /// <summary>When the visit was scheduled.</summary>
    public DateTimeOffset? ScheduledAt { get; init; } = ScheduledAt;

    /// <summary>Name of the practitioner; null when it could not be read.</summary>
    public string? PractitionerFullName { get; init; } = PractitionerFullName;
}

/// <summary>What the patient told the nutritionist about how it went, and when it was sent.</summary>
/// <param name="Feeling">Good, Fair or Hard.</param>
/// <param name="Difficulties">Dinners, Weekends, EatingOut, Schedules, Cravings; may be empty.</param>
/// <param name="Questions">The questions, as the patient sent them.</param>
/// <param name="SubmittedAt">When it was first sent.</param>
/// <param name="EditedAt">When it was last edited, if it was.</param>
/// <param name="IsLocked">True once the patient can no longer edit the answer.</param>
public record ConsultationCheckInSummaryResource(
    string Feeling,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<string> Questions,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? EditedAt,
    bool IsLocked)
{
    /// <summary>Good, Fair or Hard.</summary>
    public string Feeling { get; init; } = Feeling;

    /// <summary>What was hardest. Empty when none.</summary>
    public IReadOnlyList<string> Difficulties { get; init; } = Difficulties;

    /// <summary>The questions for the practitioner. Empty when none.</summary>
    public IReadOnlyList<string> Questions { get; init; } = Questions;

    /// <summary>When the answer was sent.</summary>
    public DateTimeOffset SubmittedAt { get; init; } = SubmittedAt;

    /// <summary>When it was last edited; null when never.</summary>
    public DateTimeOffset? EditedAt { get; init; } = EditedAt;

    /// <summary>True once the hour of the visit arrived.</summary>
    public bool IsLocked { get; init; } = IsLocked;
}

/// <summary>
///     One consultation already held. The app builds the text in es/en from <c>Label</c>: "First consultation"
///     or "Assessment and new plan (version 3)".
/// </summary>
/// <param name="ConsultationId">Identifier of the consultation.</param>
/// <param name="Date">When it was published.</param>
/// <param name="Label">FirstConsultation or AssessmentAndNewPlan.</param>
/// <param name="PlanVersion">The version it published.</param>
public record PastConsultationResource(int ConsultationId, DateTimeOffset Date, string Label, int? PlanVersion)
{
    public const string FirstConsultation = "FirstConsultation";
    public const string AssessmentAndNewPlan = "AssessmentAndNewPlan";

    /// <summary>Identifier of the consultation.</summary>
    public int ConsultationId { get; init; } = ConsultationId;

    /// <summary>When it was published.</summary>
    public DateTimeOffset Date { get; init; } = Date;

    /// <summary>FirstConsultation or AssessmentAndNewPlan.</summary>
    public string Label { get; init; } = Label;

    /// <summary>The plan version shown after the label.</summary>
    public int? PlanVersion { get; init; } = PlanVersion;
}
