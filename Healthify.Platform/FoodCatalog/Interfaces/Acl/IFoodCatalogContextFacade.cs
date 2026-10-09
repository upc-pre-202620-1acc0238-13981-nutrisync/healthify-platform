namespace Healthify.Platform.FoodCatalog.Interfaces.Acl;

/// <summary>
///     Flat DTO exposed to other bounded contexts - primitives only.
/// </summary>
/// <remarks>
///     There is no source hash on it. The fingerprint of the upstream record is an implementation
///     detail of this context and it is of no use to anybody outside it.
/// </remarks>
public record ReferenceFoodItem(
    int ReferenceFoodId,
    string LocalName,
    decimal EnergyKcalPer100g,
    decimal ProteinGPer100g,
    decimal CarbGPer100g,
    decimal FatGPer100g,
    bool IsLocalOverride);

/// <summary>
///     FC-2. What the local catalog says about one free name (an ingredient of an AI meal idea). Primitives only.
///     Everything but <paramref name="Name" /> is null when the name stands for nothing in the catalog.
/// </summary>
/// <param name="Name">The name as the caller sent it.</param>
/// <param name="ReferenceFoodId">The catalog entry it resolved to, or null.</param>
/// <param name="EnergyKcalPer100g">Energy of that entry per 100 g, or null.</param>
/// <param name="ProteinGPer100g">Protein of that entry per 100 g, or null.</param>
/// <param name="CarbGPer100g">Carbohydrate of that entry per 100 g, or null.</param>
/// <param name="FatGPer100g">Fat of that entry per 100 g, or null.</param>
/// <param name="LocalName">The local name of that entry, or null.</param>
public record ResolvedFoodItem(
    string Name,
    int? ReferenceFoodId,
    decimal? EnergyKcalPer100g,
    decimal? ProteinGPer100g = null,
    decimal? CarbGPer100g = null,
    decimal? FatGPer100g = null,
    string? LocalName = null)
{
    public bool IsResolved => ReferenceFoodId is not null;
}

/// <summary>
///     IN-7. Nutrients per 100 g a caller hands to the catalog (what the AI estimated for a dish). Primitives only.
/// </summary>
public record FoodNutrientsItem(
    decimal EnergyKcalPer100g,
    decimal ProteinGPer100g,
    decimal CarbGPer100g,
    decimal FatGPer100g);

/// <summary>
///     Public ACL contract of the Food Catalog bounded context.
/// </summary>
/// <remarks>
///     This is a query contract, not a publication one: Intake resolves a food while the patient is
///     logging a meal, which means it needs the answer at that moment and not whenever the catalog
///     next refreshes. Every method degrades gracefully.
/// </remarks>
public interface IFoodCatalogContextFacade
{
    /// <summary>One catalog entry, or null when it does not exist or the lookup fails.</summary>
    Task<ReferenceFoodItem?> GetReferenceFoodById(int referenceFoodId, CancellationToken ct = default);

    /// <summary>
    ///     IN-7. Up to <paramref name="max" /> verified entries (local overrides first, then by name) but the excluded
    ///     ones. An empty list when the lookup fails.
    /// </summary>
    Task<IReadOnlyList<ReferenceFoodItem>> ListVerifiedFoods(int max, IReadOnlyCollection<int>? excludedIds = null,
        CancellationToken ct = default);

    /// <summary>IN-7. Several entries at once (by lot). Unknown identifiers are left out; an empty list when the lookup fails.</summary>
    Task<IReadOnlyList<ReferenceFoodItem>> GetReferenceFoodsByIds(IReadOnlyList<int> referenceFoodIds,
        CancellationToken ct = default);

    /// <summary>Matching catalog entries. An empty list when nothing matches or the lookup fails.</summary>
    Task<IReadOnlyList<ReferenceFoodItem>> SearchReferenceFoods(string term, int max,
        CancellationToken ct = default);

    /// <summary>
    ///     FC-2. The catalog entry each name stands for, one item per name in the same order; at most
    ///     <paramref name="max" /> distinct names are looked up (the rest come back unresolved). Searches only the
    ///     local catalog (seeded, cached or local overrides): no external provider is called while the caller waits.
    ///     An empty list when the lookup fails.
    /// </summary>
    Task<IReadOnlyList<ResolvedFoodItem>> ResolveByNames(IReadOnlyList<string> names, int max,
        CancellationToken ct = default);

    /// <summary>
    ///     IN-7. The catalog entry a recognized dish stands for, after asking the external providers (Search Food,
    ///     which caches what they translate, idempotent by source hash). An unresolved item when nothing matches;
    ///     null when the lookup fails. Slower than <see cref="ResolveByNames" />: it may wait for a provider.
    /// </summary>
    Task<ResolvedFoodItem?> ResolveByNameWithProviders(string name, CancellationToken ct = default);

    /// <summary>
    ///     IN-7. Creates (or finds) the food for a dish nothing else carries, with the nutrients the AI estimated.
    ///     Idempotent by the normalized name, also when two callers race. The item looks like any other food: it
    ///     says nothing about where it came from. Null when the nutrients are incoherent or the creation fails.
    /// </summary>
    Task<ReferenceFoodItem?> CreateAiEstimatedFood(string name, FoodNutrientsItem nutrientsPer100g,
        long aiGenerationId, CancellationToken ct = default);
}
