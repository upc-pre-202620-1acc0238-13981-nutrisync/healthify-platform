using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class AiPreferencesResourceAssembler
{
    public static AiPreferencesResource ToResource(AiPreferencesStatus status)
    {
        return new AiPreferencesResource(status.ConsentGranted, status.WeeklySummaryEnabled, status.MealIdeasEnabled,
            status.SuggestedQuestionsEnabled, status.MealPhotoRecognitionEnabled);
    }
}
