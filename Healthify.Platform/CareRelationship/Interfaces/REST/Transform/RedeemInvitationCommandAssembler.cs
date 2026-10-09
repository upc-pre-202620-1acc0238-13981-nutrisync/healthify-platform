using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class RedeemInvitationCommandAssembler
{
    /// <summary>The patient identifier comes from the session token, never from the payload.</summary>
    public static RedeemInvitationCommand ToCommand(int patientId, RedeemInvitationResource resource)
    {
        return new RedeemInvitationCommand(resource.Token, patientId, resource.ReplaceActiveLink);
    }
}
