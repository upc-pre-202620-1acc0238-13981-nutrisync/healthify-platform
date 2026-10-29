namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>Update AI Preferences. Only the patient may issue it, about themselves.</summary>
/// <remarks>A null <c>MealPhotoRecognitionEnabled</c> keeps the stored value (older clients do not send it).</remarks>
public record UpdateAiPreferencesCommand(
    int PatientId,
    bool WeeklySummaryEnabled,
    bool MealIdeasEnabled,
    bool SuggestedQuestionsEnabled,
    bool? MealPhotoRecognitionEnabled = null);
