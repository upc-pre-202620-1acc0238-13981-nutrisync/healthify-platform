namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>
///     Payload of Update AI Preferences. The whole state: a missing value means off, except
///     <c>mealPhotoRecognitionEnabled</c>, which older clients do not send and so keeps as stored.
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

    /// <summary>Meal photo recognition. Optional: when missing it does not change.</summary>
    public bool? MealPhotoRecognitionEnabled { get; init; } = MealPhotoRecognitionEnabled;
}
