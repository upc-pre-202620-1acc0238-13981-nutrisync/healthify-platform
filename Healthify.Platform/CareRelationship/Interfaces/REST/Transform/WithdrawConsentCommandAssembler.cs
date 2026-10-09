using Healthify.Platform.CareRelationship.Domain.Model.Commands;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class WithdrawConsentCommandAssembler
{
    /// <summary>Takes no reason argument: no justification is required, or accepted.</summary>
    public static WithdrawConsentCommand ToCommand(int careLinkId, int patientId)
    {
        return new WithdrawConsentCommand(careLinkId, patientId);
    }
}
