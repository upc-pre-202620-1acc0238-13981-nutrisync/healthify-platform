namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>Payload of Subflow 2.2 - Invitation Redemption.</summary>
public record RedeemInvitationResource(string Token, bool ReplaceActiveLink = false)
{
    /// <summary>
    ///     The secret read from the QR code the practitioner showed. The patient identifier is taken
    ///     from the session token, never from this payload.
    /// </summary>
    public string Token { get; init; } = Token;

    /// <summary>
    ///     True only when the patient confirmed switching practitioners (CR-1). The current care link
    ///     is then revoked automatically when this invitation is redeemed. Defaults to false, which
    ///     keeps the conflict answer when a care link already exists.
    /// </summary>
    public bool ReplaceActiveLink { get; init; } = ReplaceActiveLink;
}
