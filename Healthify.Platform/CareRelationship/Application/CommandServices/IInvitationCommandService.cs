using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.CareRelationship.Application.CommandServices;

public interface IInvitationCommandService
{
    /// <summary>Subflow 2.1 - Issue Invitation.</summary>
    Task<Result<Invitation, CareRelationshipError>> Handle(IssueInvitationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 2.1 - Expire Invitation. Invoked by the expiry policy, never by an endpoint.</summary>
    Task<Result<Invitation, CareRelationshipError>> Handle(ExpireInvitationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 2.2 - Redeem Invitation.</summary>
    Task<Result<InvitationRedemptionOutcome, CareRelationshipError>> Handle(RedeemInvitationCommand command,
        CancellationToken cancellationToken = default);
}
