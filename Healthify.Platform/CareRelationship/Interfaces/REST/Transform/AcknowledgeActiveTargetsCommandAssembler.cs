using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class AcknowledgeActiveTargetsCommandAssembler
{
    public static AcknowledgeActiveTargetsCommand ToCommand(int careLinkId, int patientId,
        AcknowledgeActiveTargetsResource resource)
    {
        return new AcknowledgeActiveTargetsCommand(careLinkId, patientId, resource.PlanVersion);
    }
}
