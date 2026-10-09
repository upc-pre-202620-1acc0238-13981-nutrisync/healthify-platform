using Healthify.Platform.FoodCatalog.Application.Internal;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.FoodCatalog.Application.CommandServices;

public interface IReferenceFoodCommandService
{
    /// <summary>
    ///     Subflow 6.1 - Import Catalog Snapshot. Issued by the scheduled import policy and by the
    ///     on-demand endpoint. It translates and announces; it does not write the catalog itself.
    /// </summary>
    Task<Result<CatalogImportSummary, FoodCatalogError>> Handle(ImportCatalogSnapshotCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Subflow 6.2 - Cache Food Locally. Invoked by the policy that reacts to Reference Food
    ///     Translated, never by an endpoint.
    /// </summary>
    Task<Result<ReferenceFood, FoodCatalogError>> Handle(CacheFoodLocallyCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 6.3 - Search Food. Local catalog first, external providers only to top up.</summary>
    Task<Result<IReadOnlyList<ReferenceFood>, FoodCatalogError>> Handle(SearchFoodCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 6.4 - Create Local Override.</summary>
    Task<Result<ReferenceFood, FoodCatalogError>> Handle(CreateLocalOverrideCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     IN-7 - Create AI Estimated Food. Reached only through the ACL. Idempotent by the normalized name, also
    ///     under a race: the existing food is returned.
    /// </summary>
    Task<Result<ReferenceFood, FoodCatalogError>> Handle(CreateAiEstimatedFoodCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     IN-7 - Search Food with the external providers, then the entry the name stands for (FC-2 matching).
    ///     The resolution carries no food when nothing matches.
    /// </summary>
    Task<Result<FoodNameResolution, FoodCatalogError>> Handle(ResolveFoodWithProvidersCommand command,
        CancellationToken cancellationToken = default);
}
