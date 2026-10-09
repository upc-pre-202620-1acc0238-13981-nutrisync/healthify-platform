using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Domain.Repositories;

namespace Healthify.Platform.CareRelationship.Application.Internal.QueryServices;

public class InvitationQueryService(IInvitationRepository invitationRepository) : IInvitationQueryService
{
    public async Task<Invitation?> Handle(GetInvitationByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await invitationRepository.FindByIdAsync(query.InvitationId, cancellationToken);
    }

    public async Task<Invitation?> Handle(GetInvitationByTokenQuery query,
        CancellationToken cancellationToken = default)
    {
        InvitationToken token;
        try
        {
            token = new InvitationToken(query.Token);
        }
        catch (ArgumentException)
        {
            // A malformed token matches no invitation. Queries report absence, not failure.
            return null;
        }

        return await invitationRepository.FindByTokenAsync(token, cancellationToken);
    }

    public async Task<IEnumerable<Invitation>> Handle(GetExpirableInvitationsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await invitationRepository.ListExpirableAsync(query.AsOf, query.MaxResults, cancellationToken);
    }
}
