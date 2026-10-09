namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>
///     An invitation as the practitioner sees it. Read models: QR Code On Screen when it is first
///     issued, Invitation Status afterwards.
/// </summary>
public record InvitationResource(
    int InvitationId,
    int IssuedBy,
    string? Token,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RedeemedAt,
    DateTimeOffset? ExpiredAt,
    string Status)
{
    /// <summary>Identifier of the invitation.</summary>
    public int InvitationId { get; init; } = InvitationId;

    /// <summary>Identifier of the practitioner who issued it.</summary>
    public int IssuedBy { get; init; } = IssuedBy;

    /// <summary>
    ///     The single-use secret that goes into the QR code. Returned only on the response that
    ///     creates the invitation; every later read reports it as null.
    /// </summary>
    public string? Token { get; init; } = Token;

    /// <summary>When the invitation stops being redeemable.</summary>
    public DateTimeOffset ExpiresAt { get; init; } = ExpiresAt;

    /// <summary>When the patient redeemed it, or null.</summary>
    public DateTimeOffset? RedeemedAt { get; init; } = RedeemedAt;

    /// <summary>When the expiry policy retired it, or null.</summary>
    public DateTimeOffset? ExpiredAt { get; init; } = ExpiredAt;

    /// <summary>Pending, Redeemed or Expired.</summary>
    public string Status { get; init; } = Status;
}
