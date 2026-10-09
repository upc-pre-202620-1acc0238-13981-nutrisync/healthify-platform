namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     Subflow 2.1 - Expire Invitation. Issued by the time-driven policy
///     "When Expiration Date Reached", never by a user. It has no REST endpoint.
/// </summary>
public record ExpireInvitationCommand(int InvitationId);
