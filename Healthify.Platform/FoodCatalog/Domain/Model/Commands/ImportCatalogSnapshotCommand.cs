namespace Healthify.Platform.FoodCatalog.Domain.Model.Commands;

/// <summary>
///     Subflow 6.1 - Import Catalog Snapshot. Issued by the scheduled import policy, and available to
///     a practitioner who wants to pull a slice of the external catalog on demand.
/// </summary>
/// <param name="Term">What to ask the external providers for.</param>
/// <param name="Max">Upper bound on the records taken from each provider in one snapshot.</param>
public record ImportCatalogSnapshotCommand(string Term, int Max);
