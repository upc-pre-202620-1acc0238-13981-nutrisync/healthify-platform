using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Queries;

namespace Healthify.Platform.Iam.Application.QueryServices;

public interface IUserSessionQueryService
{
    Task<UserSession?> Handle(GetUserSessionByIdQuery query, CancellationToken cancellationToken = default);

    Task<IEnumerable<UserSession>> Handle(GetUserSessionsByUserIdQuery query,
        CancellationToken cancellationToken = default);
}
