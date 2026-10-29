using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.Iam.Domain.Repositories;

public interface IPasswordResetCodeRepository : IBaseRepository<PasswordResetCode>
{
    /// <summary>The most recent code of the account, used or not; null if it never asked for one.</summary>
    Task<PasswordResetCode?> FindLatestByUserIdAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>The code whose current reset token has this hash; null if none has.</summary>
    Task<PasswordResetCode?> FindByResetTokenHashAsync(string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>Removes every code of the account, so that only the next one is valid.</summary>
    Task RemoveByUserIdAsync(int userId, CancellationToken cancellationToken = default);
}
