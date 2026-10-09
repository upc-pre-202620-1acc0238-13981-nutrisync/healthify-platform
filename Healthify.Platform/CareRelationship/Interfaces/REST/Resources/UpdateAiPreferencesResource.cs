namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>
///     Payload of IA-1 - Update AI Preferences. The whole state: a missing value means off, except
///     <c>mealPhotoRecognitionEnabled</c> (IN-7), which a client older than IN-7 does not send and so keeps as stored.
/// </summary>
public record UpdateAiPreferencesResource(
    bool WeeklySummaryEnabled,
    bool MealIdeasEnabled,
    bool SuggestedQuestionsEnabled,
    bool? MealPhotoRecognitionEnabled = null)
{
    /// <summary>Weekly summary ("Resumen semanal").</summary>
    public bool WeeklySummaryEnabled { get; init; } = WeeklySummaryEnabled;

    /// <summary>Meal ideas ("Ideas de comidas").</summary>
    public bool MealIdeasEnabled { get; init; } = MealIdeasEnabled;

    /// <summary>Suggested questions ("Preguntas sugeridas").</summary>
    public bool SuggestedQuestionsEnabled { get; init; } = SuggestedQuestionsEnabled;

    /// <summary>IN-7. Meal photo recognition ("Reconocer comidas por foto"). Optional: when missing it does not change.</summary>
    public bool? MealPhotoRecognitionEnabled { get; init; } = MealPhotoRecognitionEnabled;
}
