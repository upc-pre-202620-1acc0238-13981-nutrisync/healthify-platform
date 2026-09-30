using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.Iam.Domain.Repositories;

public interface IUserRepository : IBaseRepository<User>
{
    Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default);

    /// <summary>Backs the business rule Unique Email Required (Subflow 1.1).</summary>
    Task<bool> ExistsByEmailAsync(Email email, CancellationToken cancellationToken = default);

    /// <summary>IAM-1. One query for several accounts; ids that do not exist are simply absent.</summary>
    Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default);
}
