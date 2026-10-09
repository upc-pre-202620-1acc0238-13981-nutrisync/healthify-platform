namespace Healthify.Platform.CareRelationship.Domain.Model.Queries;

/// <summary>
///     Feeds the time-driven policy "When Expiration Date Reached": invitations that are past their
///     expiration date and have neither been redeemed nor expired yet.
/// </summary>
public record GetExpirableInvitationsQuery(DateTimeOffset AsOf, int MaxResults);
