using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;

namespace Healthify.Platform.CareRelationship.Application.QueryServices;

public interface IInvitationQueryService
{
    Task<Invitation?> Handle(GetInvitationByIdQuery query, CancellationToken cancellationToken = default);
    Task<Invitation?> Handle(GetInvitationByTokenQuery query, CancellationToken cancellationToken = default);

    Task<IEnumerable<Invitation>> Handle(GetExpirableInvitationsQuery query,
        CancellationToken cancellationToken = default);
}
