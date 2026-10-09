namespace Healthify.Platform.CareRelationship.Domain.Model.Queries;

/// <summary>Resolves the invitation behind a scanned QR code, for redemption.</summary>
public record GetInvitationByTokenQuery(string Token);
