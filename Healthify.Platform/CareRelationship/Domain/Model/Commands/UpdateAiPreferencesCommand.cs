namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>IA-1 - Update AI Preferences. Only the patient may issue it, about themselves.</summary>
/// <remarks>IN-7: a null <c>MealPhotoRecognitionEnabled</c> keeps the stored value (a client older than IN-7 does not send it).</remarks>
public record UpdateAiPreferencesCommand(
    int PatientId,
    bool WeeklySummaryEnabled,
    bool MealIdeasEnabled,
    bool SuggestedQuestionsEnabled,
    bool? MealPhotoRecognitionEnabled = null);
