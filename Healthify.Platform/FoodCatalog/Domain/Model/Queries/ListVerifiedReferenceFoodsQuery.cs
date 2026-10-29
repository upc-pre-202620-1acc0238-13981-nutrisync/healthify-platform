namespace Healthify.Platform.FoodCatalog.Domain.Model.Queries;

/// <summary>
///     Up to <paramref name="Max" /> verified catalog entries, local overrides first, then by name; the ones in
///     <paramref name="ExcludedIds" /> are left out.
/// </summary>
public record ListVerifiedReferenceFoodsQuery(int Max, IReadOnlyCollection<int>? ExcludedIds = null);
