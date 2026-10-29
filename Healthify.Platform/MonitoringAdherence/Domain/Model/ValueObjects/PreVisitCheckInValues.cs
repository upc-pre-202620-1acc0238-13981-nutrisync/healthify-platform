namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     How the patient felt about the plan: Good, Fair or Hard.
/// </summary>
/// <remarks>
///     Business rule: Check In Raises No Signal. <c>Hard</c> is what the patient chose to tell their
///     practitioner, not a reading of their adherence: nothing reads it to escalate, evaluate or deviate.
/// </remarks>
public sealed record PlanFeeling
{
    public const string Good = "Good";
    public const string Fair = "Fair";
    public const string Hard = "Hard";

    /// <summary>NOTE: technical constant, the size of the feeling column.</summary>
    public const int MaximumLength = 10;

    private static readonly string[] Allowed = [Good, Fair, Hard];

    public PlanFeeling(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A check in says how the plan felt.", nameof(value));

        var match = Allowed.FirstOrDefault(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException(
            $"'{value}' is not a plan feeling. Allowed: {string.Join(", ", Allowed)}.", nameof(value));
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     What was hardest, several allowed: dinners, weekends, eating out, schedules, cravings.
/// </summary>
/// <remarks>A closed list: the app shows each one with a fixed, reviewed wording.</remarks>
public sealed record CheckInDifficulty
{
    public const string Dinners = "Dinners";
    public const string Weekends = "Weekends";
    public const string EatingOut = "EatingOut";
    public const string Schedules = "Schedules";
    public const string Cravings = "Cravings";

    private static readonly string[] Allowed = [Dinners, Weekends, EatingOut, Schedules, Cravings];

    public CheckInDifficulty(string? value)
    {
        var match = Allowed.FirstOrDefault(a => a.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException(
            $"'{value}' is not a check in difficulty. Allowed: {string.Join(", ", Allowed)}.", nameof(value));
    }

    public string Value { get; }

    /// <summary>Each one validated, duplicates removed, in the order given. Null or empty means none.</summary>
    public static IReadOnlyList<CheckInDifficulty> ListOf(IEnumerable<string>? values)
    {
        return (values ?? []).Select(v => new CheckInDifficulty(v)).Distinct().ToList();
    }

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     Where a question of the check in came from: written by the patient, or an AI suggestion the patient
///     accepted (shown as an AI suggestion).
/// </summary>
/// <remarks>Kept so that what came from the AI can be audited.</remarks>
public sealed record QuestionOrigin
{
    public const string Patient = "Patient";
    public const string AiSuggested = "AiSuggested";

    private static readonly string[] Allowed = [Patient, AiSuggested];

    public QuestionOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Value = Patient;
            return;
        }

        var match = Allowed.FirstOrDefault(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException(
            $"'{value}' is not a question origin. Allowed: {string.Join(", ", Allowed)}.", nameof(value));
    }

    public string Value { get; }

    public bool IsAiSuggested => Value == AiSuggested;

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     One question for the practitioner, with where it came from.
/// </summary>
/// <remarks>
///     Stored as JSON inside the check in, so the constructor parameters carry the property names the serializer
///     binds to.
/// </remarks>
public sealed record PatientQuestion
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 300;

    /// <param name="text">The question, 3 to 300 characters.</param>
    /// <param name="origin">Patient (default) or AiSuggested.</param>
    /// <param name="aiGenerationId">The AI generation it came from, when it was suggested; null otherwise.</param>
    /// <param name="language">
    ///     <c>es</c> or <c>en</c>: the language an AI suggestion was generated in. Kept only for AiSuggested; a
    ///     question the patient wrote is their own text, never translated, and has none.
    /// </param>
    public PatientQuestion(string text, string? origin = null, long? aiGenerationId = null, string? language = null)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length is < MinimumLength or > MaximumLength)
            throw new ArgumentException(
                $"A question has between {MinimumLength} and {MaximumLength} characters.", nameof(text));

        var questionOrigin = new QuestionOrigin(origin);
        Text = trimmed;
        Origin = questionOrigin.Value;
        AiGenerationId = questionOrigin.IsAiSuggested ? aiGenerationId : null;
        Language = questionOrigin.IsAiSuggested ? LanguageOrNull(language) : null;
    }

    public string Text { get; }

    /// <summary>Patient or AiSuggested.</summary>
    public string Origin { get; }

    /// <summary>
    ///     NOTE: technical field, for the audit of AI suggestions. Never shown. The <c>ai_generations</c> id is a
    ///     bigint: a long, like every AiGenerationId of the platform. Questions stored before were whole JSON numbers
    ///     and read back the same.
    /// </summary>
    public long? AiGenerationId { get; }

    /// <summary>
    ///     Es or en for an AI suggestion; null for the patient's own questions and for suggestions stored before
    ///     the language was recorded (they live in the JSON of <c>pre_visit_check_ins.questions</c> and read back
    ///     without it).
    /// </summary>
    public string? Language { get; }

    private static string? LanguageOrNull(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var normalized = language.Trim().ToLowerInvariant();
        return normalized is "es" or "en"
            ? normalized
            : throw new ArgumentException($"'{language}' is not a language of the app. Allowed: es, en.",
                nameof(language));
    }
}
