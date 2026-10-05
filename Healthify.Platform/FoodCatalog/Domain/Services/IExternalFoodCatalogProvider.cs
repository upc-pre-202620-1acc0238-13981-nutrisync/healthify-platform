using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;

namespace Healthify.Platform.FoodCatalog.Domain.Services;

/// <summary>
///     One upstream record, already expressed in this platform vocabulary.
/// </summary>
/// <remarks>
///     Business rule: No External Id Enters The Domain (Food Catalog, Subflow 6.1). The absence of an
///     identifier field here is the rule, compiled: an adapter has nowhere to put one even if it
///     wanted to. What remains of the upstream identity is the opaque digest.
/// </remarks>
public record ExternalFoodRecord(LocalName LocalName, NutrientsPer100g Nutrients, SourceHash SourceHash);

/// <summary>
///     What one provider returned for one request: the records that translated, and one reason per
///     record that did not.
/// </summary>
/// <remarks>
///     Business rule: Taxonomy Translation Mandatory (Subflow 6.1). A record either translates
///     completely or it is reported as a failure and dropped. There is no partial third state,
///     because a food with a name and no nutrients would be logged as a meal worth nothing.
/// </remarks>
public record ExternalCatalogSnapshot(
    string ProviderName,
    IReadOnlyList<ExternalFoodRecord> TranslatedFoods,
    IReadOnlyList<string> TranslationFailures);

/// <summary>
///     The anti-corruption layer of this bounded context, seen from the inside.
/// </summary>
/// <remarks>
///     Everything an external food database calls things stops at the implementations of this
///     interface, which live in Infrastructure/External. The domain knows that snapshots arrive and
///     that some records do not translate; it does not know that anyone speaks HTTP.
/// </remarks>
public interface IExternalFoodCatalogProvider
{
    /// <summary>The name this platform gives the provider. Used for logging and for the digest.</summary>
    string ProviderName { get; }

    /// <summary>
    ///     Asks the provider for a slice of its catalog. Implementations never throw for a network or
    ///     a payload problem: an unreachable provider returns an empty snapshot with a failure reason.
    /// </summary>
    Task<ExternalCatalogSnapshot> FetchSnapshotAsync(string term, int max,
        CancellationToken cancellationToken = default);
}
