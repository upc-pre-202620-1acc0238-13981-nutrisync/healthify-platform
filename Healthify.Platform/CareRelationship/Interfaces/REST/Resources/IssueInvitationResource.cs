namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>Payload of Subflow 2.1 - Invitation Issuing.</summary>
public record IssueInvitationResource(DateTimeOffset ExpiresAt)
{
    /// <summary>
    ///     When the invitation stops being redeemable. Required, and must be in the future: an
    ///     invitation without an expiry date is a permanent key.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; } = ExpiresAt;
}
