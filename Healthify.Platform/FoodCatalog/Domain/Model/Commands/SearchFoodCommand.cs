namespace Healthify.Platform.FoodCatalog.Domain.Model.Commands;

/// <summary>Subflow 6.3 - Search Food.</summary>
public record SearchFoodCommand(string Term, int Max);
