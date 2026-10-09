using Healthify.Platform.FoodCatalog.Application.Internal;
using Healthify.Platform.FoodCatalog.Application.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Queries;
using Healthify.Platform.FoodCatalog.Domain.Repositories;
using Healthify.Platform.FoodCatalog.Domain.Services;

namespace Healthify.Platform.FoodCatalog.Application.Internal.QueryServices;

public class ReferenceFoodQueryService(IReferenceFoodRepository repository) : IReferenceFoodQueryService
{
    public async Task<ReferenceFood?> Handle(GetReferenceFoodByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByIdAsync(query.ReferenceFoodId, cancellationToken);
    }

    public async Task<IEnumerable<ReferenceFood>> Handle(SearchReferenceFoodsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.SearchByLocalNameAsync(query.Term, query.Max, cancellationToken);
    }

    public async Task<IEnumerable<ReferenceFood>> Handle(GetLocalFoodCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListLocalCatalogAsync(query.Max, cancellationToken);
    }

    /// <remarks>
    ///     FC-2. Each distinct name narrows the catalog with its most telling word (one indexed search on the local
    ///     name, never a provider) and <see cref="FoodNameMatcher" /> picks among what comes back.
    /// </remarks>
    public async Task<IReadOnlyList<FoodNameResolution>> Handle(ResolveReferenceFoodsByNamesQuery query,
        CancellationToken cancellationToken = default)
    {
        var max = Math.Clamp(query.Max, 1, MaxNamesPerResolution);
        var resolved = new Dictionary<string, ReferenceFood?>(StringComparer.Ordinal);

        foreach (var name in query.Names)
        {
            var key = FoodNameMatcher.Normalize(name);
            if (key.Length == 0 || resolved.ContainsKey(key) || resolved.Count >= max) continue;

            var term = FoodNameMatcher.SearchTermOf(name);
            var candidates = term is null
                ? []
                : await repository.SearchByLocalNameAsync(term, CandidatesPerName, cancellationToken);
            resolved[key] = FoodNameMatcher.BestMatch(name, candidates);
        }

        return query.Names
            .Select(name => new FoodNameResolution(name,
                resolved.GetValueOrDefault(FoodNameMatcher.Normalize(name))))
            .ToList();
    }

    public async Task<IReadOnlyList<ReferenceFood>> Handle(GetReferenceFoodsByIdsQuery query,
        CancellationToken cancellationToken = default)
    {
        var ids = query.ReferenceFoodIds.Where(id => id > 0).Distinct().ToList();
        return ids.Count == 0 ? [] : await repository.FindByIdsAsync(ids, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceFood>> Handle(ListVerifiedReferenceFoodsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Max <= 0) return [];
        return await repository.ListVerifiedAsync(query.Max, query.ExcludedIds ?? [], cancellationToken);
    }

    /// <summary>FC-2. Upper bound of the names one resolution looks up.</summary>
    public const int MaxNamesPerResolution = 50;

    /// <summary>FC-2. Catalog entries read per name before choosing.</summary>
    private const int CandidatesPerName = 50;
}
