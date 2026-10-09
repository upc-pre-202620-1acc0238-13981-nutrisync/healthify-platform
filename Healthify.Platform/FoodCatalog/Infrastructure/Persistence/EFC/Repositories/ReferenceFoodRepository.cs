using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.FoodCatalog.Infrastructure.Persistence.EFC.Repositories;

public class ReferenceFoodRepository(AppDbContext context)
    : BaseRepository<ReferenceFood>(context), IReferenceFoodRepository
{
    public new async Task<ReferenceFood?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var referenceFoodId = new ReferenceFoodId(id);
        return await Context.Set<ReferenceFood>()
            .FirstOrDefaultAsync(f => f.Id == referenceFoodId, cancellationToken);
    }

    public async Task<ReferenceFood?> FindBySourceHashAsync(SourceHash sourceHash,
        CancellationToken cancellationToken = default)
    {
        // Compared against a value object instance: EF cannot translate a member of a converted type.
        return await Context.Set<ReferenceFood>()
            .FirstOrDefaultAsync(f => f.SourceHash == sourceHash, cancellationToken);
    }

    public async Task<IEnumerable<ReferenceFood>> SearchByLocalNameAsync(string term, int max,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Set<ReferenceFood>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(f => f.LocalNameText.Contains(term.Trim()));

        // Local overrides first: a practitioner added one precisely because the generic catalog was
        // not good enough for this population.
        return await query
            .OrderByDescending(f => f.IsLocalOverride)
            .ThenBy(f => f.LocalNameText)
            .Take(Math.Clamp(max, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ReferenceFood>> ListLocalCatalogAsync(int max,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<ReferenceFood>()
            .OrderByDescending(f => f.IsLocalOverride)
            .ThenBy(f => f.LocalNameText)
            .Take(Math.Clamp(max, 1, 1000))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsLocalOverrideWithNameAsync(string localName,
        CancellationToken cancellationToken = default)
    {
        var normalized = localName.Trim();
        return await Context.Set<ReferenceFood>()
            .AnyAsync(f => f.IsLocalOverride && f.LocalNameText == normalized, cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await Context.Set<ReferenceFood>().CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceFood>> ListVerifiedAsync(int max, IReadOnlyCollection<int> excludedIds,
        CancellationToken cancellationToken = default)
    {
        var excluded = excludedIds.Where(id => id > 0).Distinct().Select(id => new ReferenceFoodId(id)).ToList();
        return await Context.Set<ReferenceFood>()
            .Where(f => f.IsVerified && !excluded.Contains(f.Id))
            .OrderByDescending(f => f.IsLocalOverride)
            .ThenBy(f => f.LocalNameText)
            .Take(Math.Clamp(max, 1, 1000))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceFood>> FindByIdsAsync(IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        // Compared as value objects: EF cannot translate a member of a converted type.
        var typed = ids.Where(id => id > 0).Distinct().Select(id => new ReferenceFoodId(id)).ToList();
        if (typed.Count == 0) return [];
        return await Context.Set<ReferenceFood>().Where(f => typed.Contains(f.Id)).ToListAsync(cancellationToken);
    }

    Task<ReferenceFood?> IBaseRepository<ReferenceFood>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}
