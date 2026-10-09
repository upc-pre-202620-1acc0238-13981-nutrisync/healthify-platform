using Healthify.Platform.FoodCatalog.Application.Internal;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Queries;

namespace Healthify.Platform.FoodCatalog.Application.QueryServices;

/// <summary>Read models Food Results List and Local Food Catalog.</summary>
public interface IReferenceFoodQueryService
{
    Task<ReferenceFood?> Handle(GetReferenceFoodByIdQuery query,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<ReferenceFood>> Handle(SearchReferenceFoodsQuery query,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<ReferenceFood>> Handle(GetLocalFoodCatalogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     FC-2. One resolution per name, in the order given (a repeated name resolves once and is answered for
    ///     each occurrence). Local catalog only.
    /// </summary>
    Task<IReadOnlyList<FoodNameResolution>> Handle(ResolveReferenceFoodsByNamesQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>IN-7. Verified entries, local overrides first, then by name.</summary>
    Task<IReadOnlyList<ReferenceFood>> Handle(ListVerifiedReferenceFoodsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>IN-7. The entries that exist among the identifiers, in no particular order.</summary>
    Task<IReadOnlyList<ReferenceFood>> Handle(GetReferenceFoodsByIdsQuery query,
        CancellationToken cancellationToken = default);
}
