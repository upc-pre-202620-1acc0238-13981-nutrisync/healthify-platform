using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Iam.Infrastructure.Persistence.EFC.Repositories;

public class PasswordResetCodeRepository(AppDbContext context)
    : BaseRepository<PasswordResetCode>(context), IPasswordResetCodeRepository
{
    public async Task<PasswordResetCode?> FindLatestByUserIdAsync(int userId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<PasswordResetCode>()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IssuedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task RemoveByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var codes = await Context.Set<PasswordResetCode>()
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);
        if (codes.Count > 0) Context.Set<PasswordResetCode>().RemoveRange(codes);
    }
}
