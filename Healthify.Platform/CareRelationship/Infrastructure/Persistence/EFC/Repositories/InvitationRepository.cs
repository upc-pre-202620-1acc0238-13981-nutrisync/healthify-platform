using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.CareRelationship.Infrastructure.Persistence.EFC.Repositories;

public class InvitationRepository(AppDbContext context)
    : BaseRepository<Invitation>(context), IInvitationRepository
{
    public new async Task<Invitation?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var invitationId = new InvitationId(id);
        return await Context.Set<Invitation>()
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
    }

    public async Task<Invitation?> FindByTokenAsync(InvitationToken token,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<Invitation>().FirstOrDefaultAsync(i => i.Token == token, cancellationToken);
    }

    public async Task<IEnumerable<Invitation>> ListExpirableAsync(DateTimeOffset asOf, int maxResults,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<Invitation>()
            .Where(i => i.RedeemedAt == null && i.ExpiredAt == null && i.ExpiresAt <= asOf)
            .OrderBy(i => i.ExpiresAt)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }

    // Explicit re-implementation so that interface-typed calls reach the method above.
    Task<Invitation?> IBaseRepository<Invitation>.FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}
