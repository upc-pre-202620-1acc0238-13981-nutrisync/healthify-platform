using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class IssueInvitationCommandAssembler
{
    public static IssueInvitationCommand ToCommand(int issuedBy, IssueInvitationResource resource)
    {
        return new IssueInvitationCommand(issuedBy, resource.ExpiresAt);
    }
}
