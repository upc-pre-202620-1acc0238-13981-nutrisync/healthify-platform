using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class DischargePatientCommandAssembler
{
    public static DischargePatientCommand ToCommand(int careLinkId, int practitionerId,
        DischargePatientResource resource)
    {
        return new DischargePatientCommand(careLinkId, practitionerId, resource.ClinicalReason);
    }
}
