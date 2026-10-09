using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Iam.Infrastructure.Persistence.EFC.Repositories;

public class UserSessionRepository(AppDbContext context)
    : BaseRepository<UserSession>(context), IUserSessionRepository
{
    public new async Task<UserSession?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var sessionId = new SessionId(id);
        return await Context.Set<UserSession>()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
    }

    public async Task<IEnumerable<UserSession>> ListByUserIdAsync(int userId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<UserSession>()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserSession?> FindByRefreshTokenHashAsync(string tokenHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tokenHash)) return null;
        return await Context.Set<UserSession>()
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash, cancellationToken);
    }

    public async Task ReloadAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await Context.Entry(session).ReloadAsync(cancellationToken);
    }

    public async Task<UserSession?> FindByPreviousRefreshTokenHashAsync(string tokenHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tokenHash)) return null;
        return await Context.Set<UserSession>()
            .FirstOrDefaultAsync(s => s.PreviousRefreshTokenHash == tokenHash, cancellationToken);
    }

    Task<UserSession?> IBaseRepository<UserSession>.FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}
