namespace Healthify.Platform.CareRelationship.Application.Internal;

/// <summary>
///     What the AI functions screen shows: whether the patient consents to AI processing, and which functions are on. A
///     function counts as on only while the consent is there, whatever was stored.
/// </summary>
/// <remarks><c>MealPhotoRecognitionEnabled</c> is the meal photo recognition switch.</remarks>
public record AiPreferencesStatus(
    int PatientId,
    bool ConsentGranted,
    bool WeeklySummaryEnabled,
    bool MealIdeasEnabled,
    bool SuggestedQuestionsEnabled,
    bool MealPhotoRecognitionEnabled = false)
{
    /// <summary>The state of a patient: consent from the active link, functions from the stored preferences.</summary>
    public static AiPreferencesStatus Of(int patientId, bool consentGranted, bool weeklySummary, bool mealIdeas,
        bool suggestedQuestions, bool mealPhotoRecognition = false)
    {
        return new AiPreferencesStatus(patientId, consentGranted, consentGranted && weeklySummary,
            consentGranted && mealIdeas, consentGranted && suggestedQuestions,
            consentGranted && mealPhotoRecognition);
    }
}
