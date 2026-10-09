namespace Healthify.Platform.CareRelationship.Domain.Model.Aggregates;

/// <summary>
///     IA-1. Which of the patient AI functions the patient uses (PT21.IA "Elige qué funciones con IA quieres
///     usar"): weekly summary, meal ideas, suggested questions and, since IN-7, meal photo recognition («Reconocer
///     comidas por foto»). One per patient; it outlives a change of
///     practitioner, while the consent to AI processing (CR-2) belongs to each care link.
/// </summary>
/// <remarks>
///     Kept apart from <see cref="CareLink" /> on purpose: the consent is an act of the relationship, the preferences
///     are the patient's own choice of tools. They are coherent with the consent switch: none can be on while AI
///     processing is not consented, all of them are turned on when it is granted, and all go off when it is
///     withdrawn. Turning one off changes neither the plan nor the records.
/// </remarks>
public partial class AiPreferences
{
    /// <summary>Required by EF Core.</summary>
    protected AiPreferences()
    {
    }

    /// <summary>A patient without any AI function on, the state before any decision.</summary>
    public AiPreferences(int patientId)
    {
        if (patientId <= 0)
            throw new ArgumentException("AI preferences belong to a patient.", nameof(patientId));

        PatientId = patientId;
    }

    /// <summary>Cross-context reference to the patient account, and the identity of the preferences.</summary>
    public int PatientId { get; private set; }

    public bool WeeklySummaryEnabled { get; private set; }
    public bool MealIdeasEnabled { get; private set; }
    public bool SuggestedQuestionsEnabled { get; private set; }

    /// <summary>
    ///     IN-7. Meal photo recognition: the photo of a dish is sent to the AI (without metadata nor any data of the
    ///     patient) and never stored. Off for the rows that existed before IN-7: nobody had consented to sending photos.
    /// </summary>
    public bool MealPhotoRecognitionEnabled { get; private set; }

    public bool AnyEnabled => WeeklySummaryEnabled || MealIdeasEnabled || SuggestedQuestionsEnabled ||
                              MealPhotoRecognitionEnabled;

    /// <summary>IA-1 - Update AI Preferences. The patient picks the functions they use.</summary>
    /// <param name="weeklySummary">Weekly summary on or off.</param>
    /// <param name="mealIdeas">Meal ideas on or off.</param>
    /// <param name="suggestedQuestions">Suggested questions on or off.</param>
    /// <param name="aiProcessingConsented">Whether the patient's active consent includes AI processing (CR-2).</param>
    /// <param name="mealPhotoRecognition">IN-7. Meal photo recognition on or off; null keeps it as it is (older clients).</param>
    /// <returns>False when nothing changed.</returns>
    /// <exception cref="InvalidOperationException">A function turned on without consent to AI processing.</exception>
    public bool Change(bool weeklySummary, bool mealIdeas, bool suggestedQuestions, bool aiProcessingConsented,
        bool? mealPhotoRecognition = null)
    {
        // Business rule: AI Preferences Require Consent (IA-1). A function can be on only while the patient
        // consents to AI processing; the app offers the consent first (PT2 or PUT ai-processing-consent).
        if (!aiProcessingConsented &&
            (weeklySummary || mealIdeas || suggestedQuestions || mealPhotoRecognition == true))
            throw new InvalidOperationException("An AI function cannot be turned on without consent to AI processing.");

        // A stored function the patient did not mention cannot stay on without the consent either.
        var photos = (mealPhotoRecognition ?? MealPhotoRecognitionEnabled) && aiProcessingConsented;
        return Set(weeklySummary, mealIdeas, suggestedQuestions, photos);
    }

    /// <summary>
    ///     IA-1 - Policy "When AI Processing Consent Granted": every function is turned on with the consent
    ///     ("Los tres valen true cuando el consentimiento se otorga"; four since IN-7).
    /// </summary>
    /// <returns>False when they already were.</returns>
    public bool EnableAll()
    {
        return Set(true, true, true, true);
    }

    /// <summary>IA-1 - Policy "When AI Processing Consent Withdrawn": every function goes off.</summary>
    /// <returns>False when they already were.</returns>
    public bool DisableAll()
    {
        return Set(false, false, false, false);
    }

    private bool Set(bool weeklySummary, bool mealIdeas, bool suggestedQuestions, bool mealPhotoRecognition)
    {
        if (WeeklySummaryEnabled == weeklySummary && MealIdeasEnabled == mealIdeas &&
            SuggestedQuestionsEnabled == suggestedQuestions && MealPhotoRecognitionEnabled == mealPhotoRecognition)
            return false;

        WeeklySummaryEnabled = weeklySummary;
        MealIdeasEnabled = mealIdeas;
        SuggestedQuestionsEnabled = suggestedQuestions;
        MealPhotoRecognitionEnabled = mealPhotoRecognition;
        return true;
    }
}
