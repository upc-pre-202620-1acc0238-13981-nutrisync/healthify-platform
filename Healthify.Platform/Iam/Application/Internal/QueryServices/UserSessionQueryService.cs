using Healthify.Platform.Iam.Application.QueryServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Queries;
using Healthify.Platform.Iam.Domain.Repositories;

namespace Healthify.Platform.Iam.Application.Internal.QueryServices;

public class UserSessionQueryService(IUserSessionRepository sessionRepository) : IUserSessionQueryService
{
    public async Task<UserSession?> Handle(GetUserSessionByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await sessionRepository.FindByIdAsync(query.SessionId, cancellationToken);
    }

    public async Task<IEnumerable<UserSession>> Handle(GetUserSessionsByUserIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await sessionRepository.ListByUserIdAsync(query.UserId, cancellationToken);
    }
}
