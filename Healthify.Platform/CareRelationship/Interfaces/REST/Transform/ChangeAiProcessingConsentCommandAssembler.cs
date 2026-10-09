using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class ChangeAiProcessingConsentCommandAssembler
{
    public static ChangeAiProcessingConsentCommand ToCommand(int careLinkId, int patientId,
        ChangeAiProcessingConsentResource resource)
    {
        return new ChangeAiProcessingConsentCommand(careLinkId, patientId, resource.Granted);
    }
}
