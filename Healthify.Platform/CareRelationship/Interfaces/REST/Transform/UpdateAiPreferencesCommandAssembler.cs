using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class UpdateAiPreferencesCommandAssembler
{
    public static UpdateAiPreferencesCommand ToCommand(int patientId, UpdateAiPreferencesResource resource)
    {
        return new UpdateAiPreferencesCommand(patientId, resource.WeeklySummaryEnabled, resource.MealIdeasEnabled,
            resource.SuggestedQuestionsEnabled, resource.MealPhotoRecognitionEnabled);
    }
}
