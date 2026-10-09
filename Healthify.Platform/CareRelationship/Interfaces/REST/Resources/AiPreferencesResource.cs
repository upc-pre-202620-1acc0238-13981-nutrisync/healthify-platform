namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>IA-1. The AI functions of a patient, as PT21 and PT21.IA show them.</summary>
public record AiPreferencesResource(
    bool ConsentGranted,
    bool WeeklySummaryEnabled,
    bool MealIdeasEnabled,
    bool SuggestedQuestionsEnabled,
    bool MealPhotoRecognitionEnabled = false)
{
    /// <summary>
    ///     Whether the patient consents to AI processing on their active care link (CR-2). While false, every
    ///     function is off.
    /// </summary>
    public bool ConsentGranted { get; init; } = ConsentGranted;

    /// <summary>Weekly summary ("Resumen semanal").</summary>
    public bool WeeklySummaryEnabled { get; init; } = WeeklySummaryEnabled;

    /// <summary>Meal ideas ("Ideas de comidas").</summary>
    public bool MealIdeasEnabled { get; init; } = MealIdeasEnabled;

    /// <summary>Suggested questions ("Preguntas sugeridas").</summary>
    public bool SuggestedQuestionsEnabled { get; init; } = SuggestedQuestionsEnabled;

    /// <summary>IN-7. Meal photo recognition ("Reconocer comidas por foto").</summary>
    public bool MealPhotoRecognitionEnabled { get; init; } = MealPhotoRecognitionEnabled;
}
