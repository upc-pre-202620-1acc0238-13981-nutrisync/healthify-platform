using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.FoodCatalog.Domain.Repositories;

public interface IReferenceFoodRepository : IBaseRepository<ReferenceFood>
{
    /// <summary>The upstream fingerprint is what makes importing and seeding idempotent.</summary>
    Task<ReferenceFood?> FindBySourceHashAsync(SourceHash sourceHash,
        CancellationToken cancellationToken = default);

    /// <summary>Read model Food Results List. Matches on the local name, never on a provider label.</summary>
    Task<IEnumerable<ReferenceFood>> SearchByLocalNameAsync(string term, int max,
        CancellationToken cancellationToken = default);

    /// <summary>Read model Local Food Catalog.</summary>
    Task<IEnumerable<ReferenceFood>> ListLocalCatalogAsync(int max,
        CancellationToken cancellationToken = default);

    /// <summary>Rule: one local override per local name (Subflow 6.4).</summary>
    Task<bool> ExistsLocalOverrideWithNameAsync(string localName,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>IN-7. Verified entries but the excluded ones, local overrides first, then by name, in one query.</summary>
    Task<IReadOnlyList<ReferenceFood>> ListVerifiedAsync(int max, IReadOnlyCollection<int> excludedIds,
        CancellationToken cancellationToken = default);

    /// <summary>IN-7. The entries among these identifiers, in one query.</summary>
    Task<IReadOnlyList<ReferenceFood>> FindByIdsAsync(IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default);
}
