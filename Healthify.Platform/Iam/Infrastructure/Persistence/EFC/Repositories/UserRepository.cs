using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Iam.Infrastructure.Persistence.EFC.Repositories;

public class UserRepository(AppDbContext context) : BaseRepository<User>(context), IUserRepository
{
    public new async Task<User?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var userId = new UserId(id);
        return await Context.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default)
    {
        return await Context.Set<User>().FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<bool> ExistsByEmailAsync(Email email, CancellationToken cancellationToken = default)
    {
        return await Context.Set<User>().AnyAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var userIds = ids.Where(id => id > 0).Distinct().Select(id => new UserId(id)).ToList();
        if (userIds.Count == 0) return [];

        return await Context.Set<User>().Where(u => userIds.Contains(u.Id)).ToListAsync(cancellationToken);
    }

    // Explicit re-implementation so that interface-typed calls reach the method above and not the
    // one on BaseRepository.
    Task<User?> IBaseRepository<User>.FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}
