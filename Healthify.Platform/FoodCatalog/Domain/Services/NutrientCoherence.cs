namespace Healthify.Platform.FoodCatalog.Domain.Services;

/// <summary>
///     IN-7. How far the declared energy may be from 4P + 4C + 9G: the larger of a share of the declared energy and a
///     fixed number of kilocalories per 100 g. The fixed part keeps foods with much fibre (vegetables, whose energy is
///     low and whose carbohydrate counts fibre) from being refused for a few kilocalories.
/// </summary>
/// <param name="Percent">Share of the declared energy (<c>FoodCatalog:AiEstimatedNutrients:EnergyTolerancePercent</c>, 15).</param>
/// <param name="Kcal">Kilocalories per 100 g (<c>FoodCatalog:AiEstimatedNutrients:EnergyToleranceKcal</c>, 20).</param>
public sealed record NutrientTolerance(decimal Percent, decimal Kcal)
{
    public const decimal DefaultPercent = 15m;
    public const decimal DefaultKcal = 20m;

    public static NutrientTolerance Default { get; } = new(DefaultPercent, DefaultKcal);

    /// <summary>The allowed difference for this declared energy.</summary>
    public decimal AllowedFor(decimal energyKcal)
    {
        return Math.Max(Math.Max(0m, Percent) / 100m * energyKcal, Math.Max(0m, Kcal));
    }
}

/// <summary>
///     IN-7. Business rule: AI Estimated Nutrients Coherent. Nutrients per 100 g that a model estimated become a food of
///     the shared catalog only if they could describe real food.
/// </summary>
/// <remarks>
///     - energy between 0 and 900 kcal per 100 g (pure fat is about 900);
///     - protein, carbohydrate and fat between 0 and 100 g each, and together at most 100 g in 100 g;
///     - the energy agrees with the macronutrients (Atwater, 4/4/9 kcal per gram) within
///     <see cref="NutrientTolerance" />: <c>|kcal − (4P + 4C + 9G)| ≤ max(15 % × kcal, 20 kcal)</c> by default.
///     Pure and deterministic, so the same estimate is always accepted or always refused.
/// </remarks>
public static class NutrientCoherence
{
    public const decimal MaxEnergyKcal = 900m;
    public const decimal MaxMacroGrams = 100m;

    /// <summary>What makes these nutrients incoherent; empty when they are coherent.</summary>
    public static IReadOnlyList<string> Violations(decimal energyKcal, decimal proteinG, decimal carbG, decimal fatG,
        NutrientTolerance? tolerance = null)
    {
        tolerance ??= NutrientTolerance.Default;
        var violations = new List<string>();
        if (energyKcal is < 0m or > MaxEnergyKcal)
            violations.Add($"energy {energyKcal} kcal is outside 0–{MaxEnergyKcal} per 100 g");
        if (proteinG is < 0m or > MaxMacroGrams) violations.Add($"protein {proteinG} g is outside 0–100");
        if (carbG is < 0m or > MaxMacroGrams) violations.Add($"carbohydrate {carbG} g is outside 0–100");
        if (fatG is < 0m or > MaxMacroGrams) violations.Add($"fat {fatG} g is outside 0–100");
        if (proteinG + carbG + fatG > MaxMacroGrams)
            violations.Add($"protein, carbohydrate and fat add up to {proteinG + carbG + fatG} g in 100 g");

        var atwater = AtwaterEnergy(proteinG, carbG, fatG);
        var allowed = tolerance.AllowedFor(energyKcal);
        if (Math.Abs(energyKcal - atwater) > allowed)
            violations.Add($"energy {energyKcal} kcal disagrees with its macronutrients ({atwater} kcal) by more " +
                           $"than {allowed} kcal");

        return violations;
    }

    public static bool IsCoherent(decimal energyKcal, decimal proteinG, decimal carbG, decimal fatG,
        NutrientTolerance? tolerance = null)
    {
        return Violations(energyKcal, proteinG, carbG, fatG, tolerance).Count == 0;
    }

    /// <summary>4 kcal per gram of protein and of carbohydrate, 9 per gram of fat.</summary>
    public static decimal AtwaterEnergy(decimal proteinG, decimal carbG, decimal fatG)
    {
        return 4m * proteinG + 4m * carbG + 9m * fatG;
    }
}
