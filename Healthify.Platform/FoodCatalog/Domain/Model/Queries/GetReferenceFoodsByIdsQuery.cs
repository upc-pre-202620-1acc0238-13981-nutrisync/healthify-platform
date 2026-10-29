namespace Healthify.Platform.FoodCatalog.Domain.Model.Queries;

/// <summary>Several catalog entries at once (by lot, to avoid N+1). Unknown identifiers are left out.</summary>
public record GetReferenceFoodsByIdsQuery(IReadOnlyList<int> ReferenceFoodIds);
