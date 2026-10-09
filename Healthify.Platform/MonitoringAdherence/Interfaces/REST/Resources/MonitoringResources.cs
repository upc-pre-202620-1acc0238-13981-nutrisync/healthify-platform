using System.ComponentModel.DataAnnotations;

namespace Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;

/// <summary>
///     Subflow 5.10 - Record Referral. One of only two write payloads in this bounded context.
/// </summary>
/// <remarks>
///     Business rule: Specialty And Reason Required (Subflow 5.10). Both are required here and
///     required again in the value objects behind them, because a referral that does not say where
///     to or why is a dead end for whoever receives it.
/// </remarks>
/// <param name="PatientId">Who is being referred.</param>
/// <param name="Specialty">Which speciality.</param>
/// <param name="Reason">Why.</param>
public record RecordReferralResource(
    [Required] int PatientId,
    [Required] [MaxLength(120)] string Specialty,
    [Required] [MaxLength(1000)] string Reason)
{
    /// <summary>Identifier of the patient being referred.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The speciality the patient is being sent to.</summary>
    public string Specialty { get; init; } = Specialty;

    /// <summary>Why the referral is being made.</summary>
    public string Reason { get; init; } = Reason;
}

/// <summary>
///     Subflow 5.10 - Schedule Follow Up. The other write payload.
/// </summary>
/// <param name="PatientId">Who is being seen.</param>
/// <param name="ScheduledFor">When.</param>
/// <param name="Preparation">MA-2. How the patient should prepare; may be empty.</param>
/// <param name="Modality">MA-2. InPerson (default) or Remote.</param>
public record ScheduleFollowUpResource(
    [Required] int PatientId,
    [Required] DateTimeOffset ScheduledFor,
    IReadOnlyList<string>? Preparation = null,
    string? Modality = null)
{
    /// <summary>Identifier of the patient being seen.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The moment of the visit. It has to be in the future.</summary>
    public DateTimeOffset ScheduledFor { get; init; } = ScheduledFor;

    /// <summary>
    ///     MA-2. How the patient should prepare, from the closed list Fasting, LightClothing, BringBloodTests,
    ///     EmptyBladder. The patient sees them in their app. Empty or absent means no instructions.
    /// </summary>
    public IReadOnlyList<string>? Preparation { get; init; } = Preparation;

    /// <summary>MA-2. InPerson or Remote. Absent means InPerson.</summary>
    public string? Modality { get; init; } = Modality;
}

/// <summary>MA-4. One question of the check in (PT25.2 "¿Algo que quieras preguntarle?").</summary>
/// <param name="Text">The question, 3 to 300 characters.</param>
/// <param name="Origin">Patient (default) or AiSuggested, when it is a suggestion the patient added.</param>
/// <param name="AiGenerationId">The AI generation it came from, when suggested.</param>
/// <param name="Language">X-2. es or en: the language of the suggestion (the one IA-4 returned).</param>
public record CheckInQuestionInputResource(
    [Required] [MaxLength(300)] string Text,
    string? Origin = null,
    long? AiGenerationId = null,
    string? Language = null)
{
    /// <summary>The question, 3 to 300 characters.</summary>
    public string Text { get; init; } = Text;

    /// <summary>Patient or AiSuggested. Absent means Patient.</summary>
    public string? Origin { get; init; } = Origin;

    /// <summary>The AI generation the suggestion came from (IA-4), for the audit. Ignored when the origin is Patient.</summary>
    public long? AiGenerationId { get; init; } = AiGenerationId;

    /// <summary>
    ///     X-2. es or en: the <c>language</c> of the suggestions it came from. Ignored when the origin is Patient. When
    ///     an AiSuggested question comes without it, the language of the request is kept.
    /// </summary>
    public string? Language { get; init; } = Language;
}

/// <summary>
///     MA-4. The check in the patient sends, or edits, before the visit (PT25.2 "Enviar a mi nutricionista").
/// </summary>
/// <param name="Feeling">Good, Fair or Hard. Required.</param>
/// <param name="Difficulties">Dinners, Weekends, EatingOut, Schedules, Cravings; may be empty.</param>
/// <param name="Questions">The questions; at most 3 written by the patient and 3 suggestions.</param>
public record SubmitPreVisitCheckInResource(
    string? Feeling,
    IReadOnlyList<string>? Difficulties = null,
    IReadOnlyList<CheckInQuestionInputResource>? Questions = null)
{
    /// <summary>"¿Cómo te sentiste con tu plan?": Good, Fair or Hard. Required.</summary>
    public string? Feeling { get; init; } = Feeling;

    /// <summary>"¿Qué te costó más?": Dinners, Weekends, EatingOut, Schedules, Cravings. Optional.</summary>
    public IReadOnlyList<string>? Difficulties { get; init; } = Difficulties;

    /// <summary>"¿Algo que quieras preguntarle?", with the AI suggestions the patient added. Optional.</summary>
    public IReadOnlyList<CheckInQuestionInputResource>? Questions { get; init; } = Questions;
}

/// <summary>MA-5. Cancel a visit. Practitioner only.</summary>
/// <param name="Reason">Short reason, at most 30 characters; optional.</param>
public record CancelFollowUpResource([MaxLength(30)] string? Reason = null)
{
    /// <summary>Short reason, at most 30 characters. Absent means "CancelledByPractitioner".</summary>
    public string? Reason { get; init; } = Reason;
}

/// <summary>MA-5. Move a visit to another future moment. Practitioner only.</summary>
/// <param name="ScheduledFor">The new moment; it has to be in the future.</param>
/// <param name="Preparation">New preparation codes; absent keeps the current ones.</param>
public record RescheduleFollowUpResource([Required] DateTimeOffset ScheduledFor,
    IReadOnlyList<string>? Preparation = null)
{
    /// <summary>The new moment of the visit. It has to be in the future.</summary>
    public DateTimeOffset ScheduledFor { get; init; } = ScheduledFor;

    /// <summary>
    ///     New preparation codes (Fasting, LightClothing, BringBloodTests, EmptyBladder), replacing the current ones.
    ///     Absent keeps them; an empty list removes them.
    /// </summary>
    public IReadOnlyList<string>? Preparation { get; init; } = Preparation;
}
