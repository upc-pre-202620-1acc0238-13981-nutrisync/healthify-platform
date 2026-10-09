using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Queries;

namespace Healthify.Platform.Iam.Application.QueryServices;

public interface IUserQueryService
{
    Task<User?> Handle(GetUserByIdQuery query, CancellationToken cancellationToken = default);
    Task<User?> Handle(GetUserByEmailQuery query, CancellationToken cancellationToken = default);

    /// <summary>IAM-1. The accounts that exist among the given ids; empty, never null.</summary>
    Task<IReadOnlyList<User>> Handle(GetUsersByIdsQuery query, CancellationToken cancellationToken = default);
}
