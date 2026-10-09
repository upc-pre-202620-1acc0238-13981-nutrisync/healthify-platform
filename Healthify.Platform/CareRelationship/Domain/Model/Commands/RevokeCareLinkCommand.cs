namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     Subflow 2.5 - Revoke Care Link. Issued by the policy "When Consent Withdrawn", never by a
///     user. It has no REST endpoint.
/// </summary>
public record RevokeCareLinkCommand(int CareLinkId);
