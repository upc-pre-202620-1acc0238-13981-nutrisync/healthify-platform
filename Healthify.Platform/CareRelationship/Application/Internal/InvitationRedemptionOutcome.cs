using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;

namespace Healthify.Platform.CareRelationship.Application.Internal;

/// <summary>
///     Application DTO carrying what redeeming an invitation produced. The care link is created by
///     the policy that reacts to Invitation Redeemed, in its own scope, so the command service reads
///     it back after publishing in order to answer the caller with it.
/// </summary>
public record InvitationRedemptionOutcome(Invitation Invitation, CareLink? CareLink);
