namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     Subflow 2.2 - Invitation Redemption. The patient scans the QR code during the consultation.
///     The patient identifier comes from the session token, never from the request body.
/// </summary>
/// <remarks>
///     ReplaceActiveLink (CR-1): true only when the patient explicitly confirmed switching practitioners: the link they
///     currently have is revoked in the same commit that redeems the invitation. False keeps the
///     rule One Active Link Per Patient answering with a conflict, as before.
/// </remarks>
public record RedeemInvitationCommand(string Token, int PatientId, bool ReplaceActiveLink = false);
