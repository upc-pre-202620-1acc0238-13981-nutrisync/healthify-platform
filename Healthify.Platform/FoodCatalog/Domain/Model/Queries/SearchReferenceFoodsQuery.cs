namespace Healthify.Platform.FoodCatalog.Domain.Model.Queries;

/// <summary>Read model Food Results List.</summary>
public record SearchReferenceFoodsQuery(string Term, int Max);
