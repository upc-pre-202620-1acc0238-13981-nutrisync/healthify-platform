namespace Healthify.Platform.FoodCatalog.Application.Internal;

/// <summary>
///     What one run of Import Catalog Snapshot did, across every provider it consulted.
/// </summary>
/// <remarks>
///     An import is accepted rather than completed: the records are translated and announced here,
///     and the policy of Subflow 6.2 is what actually writes them into the catalog. The endpoint
///     answers 202 for that reason.
/// </remarks>
public record CatalogImportSummary(
    string Term,
    int ProvidersConsulted,
    int TranslatedCount,
    int FailedCount);
