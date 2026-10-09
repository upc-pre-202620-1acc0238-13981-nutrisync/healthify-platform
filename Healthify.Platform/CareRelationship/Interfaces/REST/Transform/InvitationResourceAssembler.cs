using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

namespace Healthify.Platform.CareRelationship.Interfaces.REST.Transform;

public static class InvitationResourceAssembler
{
    /// <summary>
    ///     Read model: Invitation Status. The token is withheld, because a single-use secret should
    ///     leave the server exactly once.
    /// </summary>
    public static InvitationResource ToResource(Invitation invitation)
    {
        return Build(invitation, null);
    }

    /// <summary>
    ///     Read model: QR Code On Screen. The only response that carries the token, returned to the
    ///     practitioner who just issued the invitation.
    /// </summary>
    public static InvitationResource ToResourceWithToken(Invitation invitation)
    {
        return Build(invitation, invitation.Token.Value);
    }

    private static InvitationResource Build(Invitation invitation, string? token)
    {
        var status = invitation.IsRedeemed ? "Redeemed"
            : invitation.IsExpired || invitation.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired"
            : "Pending";

        return new InvitationResource(
            invitation.Id.Value,
            invitation.IssuedBy,
            token,
            invitation.ExpiresAt,
            invitation.RedeemedAt,
            invitation.ExpiredAt,
            status);
    }
}
