using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class GrantConsentCommandAssembler
{
    public static GrantConsentCommand ToCommand(int careLinkId, int patientId, GrantConsentResource resource)
    {
        return new GrantConsentCommand(careLinkId, patientId, resource.Scope, resource.AiProcessingGranted);
    }
}
