using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.Iam.Domain.Repositories;

public interface IUserSessionRepository : IBaseRepository<UserSession>
{
    Task<IEnumerable<UserSession>> ListByUserIdAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>IAM-4. The session whose current refresh token has this hash.</summary>
    Task<UserSession?> FindByRefreshTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    ///     IAM-4. Discards the unsaved changes of <paramref name="session" /> and reads it again, after a save lost
    ///     a concurrency conflict.
    /// </summary>
    Task ReloadAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>IAM-4. The session that already rotated away from the refresh token with this hash.</summary>
    Task<UserSession?> FindByPreviousRefreshTokenHashAsync(string tokenHash,
        CancellationToken cancellationToken = default);
}
