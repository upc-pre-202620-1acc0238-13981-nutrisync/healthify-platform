using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.CareRelationship.Application.Acl;

/// <summary>
///     IA-1. This context's answer to the AI pipeline's question "does this patient allow function X?" (IA-0,
///     guard 2). Shared declares <see cref="IAiConsentPolicy" />; consent lives here, so it is implemented here.
/// </summary>
/// <remarks>
///     - Patient functions (weekly summary, meal ideas, suggested questions, meal photo recognition): the consent to AI processing of the
///     active link (CR-2) and the preference for that function (IA-1).
///     - Practitioner functions: the consent to AI processing only (§12-#5); the function falls back to its
///     deterministic answer when it is missing.
///     Delegates to the query service of this context, never to a repository, and answers false on any failure.
/// </remarks>
public class CareRelationshipAiConsentPolicy(IAiPreferencesQueryService preferencesQueryService) : IAiConsentPolicy
{
    public async Task<bool> IsAllowedAsync(int patientId, AiFeature feature,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await preferencesQueryService.Handle(new GetAiPreferencesByPatientIdQuery(patientId),
                cancellationToken);
            if (!status.ConsentGranted) return false;

            // DECISIÓN §12-#5: the practitioner functions require the patient's consent to AI processing and
            // nothing else; the patient's preferences only choose the patient's own tools.
            if (feature.Audience == AiFeatureAudience.Practitioner) return true;

            if (feature == AiFeature.WeeklySummary) return status.WeeklySummaryEnabled;
            if (feature == AiFeature.MealIdeas) return status.MealIdeasEnabled;
            if (feature == AiFeature.SuggestedQuestions) return status.SuggestedQuestionsEnabled;
            if (feature == AiFeature.MealPhotoRecognition) return status.MealPhotoRecognitionEnabled;
            // A patient function without a preference of its own was never offered to the patient.
            return false;
        }
        catch
        {
            return false; // graceful degradation: no answer means no processing
        }
    }
}
