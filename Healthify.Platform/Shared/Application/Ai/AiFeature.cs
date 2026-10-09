namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>Who asks for a generation: decides which consent the policy checks (IA-0, guard 2).</summary>
public enum AiFeatureAudience
{
    /// <summary>The patient asks, about themselves: general AI consent plus the preference for the feature.</summary>
    Patient,

    /// <summary>The practitioner asks, about a patient: general AI consent of that patient only (§12-#5).</summary>
    Practitioner
}

/// <summary>
///     IA-0. One AI function of the platform, as the technical module knows it: a stable name (the
///     <c>Ai:Features:&lt;Name&gt;</c> configuration key, the <c>feature</c> column of <c>ai_generations</c> and the
///     prompt file prefix) and its audience. The function itself, its input and its business rules live in the
///     context that owns its data.
/// </summary>
/// <remarks>
///     A closed catalog on purpose: a name that is not here cannot reach the provider, be enabled by configuration
///     or be audited under a misspelling.
/// </remarks>
public sealed record AiFeature
{
    private AiFeature(string name, string promptName, AiFeatureAudience audience)
    {
        Name = name;
        PromptName = promptName;
        Audience = audience;
    }

    /// <summary>IA-2 (MonitoringAdherence). Weekly summary for the patient.</summary>
    public static AiFeature WeeklySummary { get; } = new("WeeklySummary", "weekly-summary", AiFeatureAudience.Patient);

    /// <summary>IA-3 (IntakeBodyResponse). Meal ideas within the remaining targets.</summary>
    public static AiFeature MealIdeas { get; } = new("MealIdeas", "meal-ideas", AiFeatureAudience.Patient);

    /// <summary>IA-4 (MonitoringAdherence). Questions the patient may bring to the follow-up.</summary>
    public static AiFeature SuggestedQuestions { get; } =
        new("SuggestedQuestions", "suggested-questions", AiFeatureAudience.Patient);

    /// <summary>IA-5 (MonitoringAdherence). Monitoring summary for the practitioner.</summary>
    public static AiFeature PractitionerMonitoringSummary { get; } = new("PractitionerMonitoringSummary",
        "practitioner-monitoring-summary", AiFeatureAudience.Practitioner);

    /// <summary>IA-6 (NutritionalCare). Diagnosis suggestion during the consultation.</summary>
    public static AiFeature DiagnosisSuggestion { get; } =
        new("DiagnosisSuggestion", "diagnosis-suggestion", AiFeatureAudience.Practitioner);

    /// <summary>IA-7 (NutritionalCare). Guideline suggestions for the plan.</summary>
    public static AiFeature GuidelineSuggestions { get; } =
        new("GuidelineSuggestions", "guideline-suggestions", AiFeatureAudience.Practitioner);

    /// <summary>IA-8 (NutritionalCare). Plan adjustment proposal.</summary>
    public static AiFeature PlanAdjustmentProposal { get; } =
        new("PlanAdjustmentProposal", "plan-adjustment-proposal", AiFeatureAudience.Practitioner);

    /// <summary>
    ///     IN-7 (IntakeBodyResponse). Recognizes the dish of a photo and estimates its portion and its nutrients. The
    ///     only function that sends an image, stripped of metadata and without any data of the patient.
    /// </summary>
    public static AiFeature MealPhotoRecognition { get; } =
        new("MealPhotoRecognition", "meal-photo-recognition", AiFeatureAudience.Patient);

    public static IReadOnlyList<AiFeature> All { get; } =
    [
        WeeklySummary, MealIdeas, SuggestedQuestions, PractitionerMonitoringSummary, DiagnosisSuggestion,
        GuidelineSuggestions, PlanAdjustmentProposal, MealPhotoRecognition
    ];

    /// <summary>PascalCase name: configuration key and audit value.</summary>
    public string Name { get; }

    /// <summary>kebab-case name: prefix of the prompt files (<c>meal-ideas@1.md</c>).</summary>
    public string PromptName { get; }

    public AiFeatureAudience Audience { get; }

    /// <summary>The feature with this name, or null. Case-insensitive.</summary>
    public static AiFeature? FromName(string? name)
    {
        return All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public override string ToString()
    {
        return Name;
    }
}
