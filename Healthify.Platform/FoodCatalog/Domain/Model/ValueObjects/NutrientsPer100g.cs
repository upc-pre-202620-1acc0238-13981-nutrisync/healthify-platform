namespace Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;

/// <summary>
///     The nutrient content of a reference food, always expressed per 100 grams.
/// </summary>
/// <remarks>
///     Per 100 g and never per portion: the portion is declared by whoever logs the meal, and keeping
///     the two apart is what lets one reference food serve every portion size without the catalog
///     ever having an opinion about how much somebody ate.
/// </remarks>
public sealed record NutrientsPer100g
{
    /// <summary>Nothing edible reaches this density; pure fat is about 900 kcal per 100 g.</summary>
    private const decimal MaxEnergyKcal = 950m;

    public NutrientsPer100g(decimal energyKcal, decimal proteinG, decimal carbG, decimal fatG)
    {
        if (energyKcal < 0m) throw new ArgumentException("Energy cannot be negative.", nameof(energyKcal));
        if (energyKcal > MaxEnergyKcal)
            throw new ArgumentException($"Energy per 100 g cannot exceed {MaxEnergyKcal} kcal.",
                nameof(energyKcal));
        if (proteinG is < 0m or > 100m)
            throw new ArgumentException("Protein per 100 g must be between 0 and 100 grams.", nameof(proteinG));
        if (carbG is < 0m or > 100m)
            throw new ArgumentException("Carbohydrate per 100 g must be between 0 and 100 grams.",
                nameof(carbG));
        if (fatG is < 0m or > 100m)
            throw new ArgumentException("Fat per 100 g must be between 0 and 100 grams.", nameof(fatG));

        EnergyKcal = decimal.Round(energyKcal, 2);
        ProteinG = decimal.Round(proteinG, 2);
        CarbG = decimal.Round(carbG, 2);
        FatG = decimal.Round(fatG, 2);
    }

    public decimal EnergyKcal { get; }
    public decimal ProteinG { get; }
    public decimal CarbG { get; }
    public decimal FatG { get; }
}
