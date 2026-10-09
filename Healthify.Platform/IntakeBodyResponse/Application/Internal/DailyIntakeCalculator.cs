using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal;

/// <summary>What the confirmed entries of a day add up to, rounded to two decimals.</summary>
public sealed record DailyIntakeTotals(
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    decimal OffPlanEnergyKcal);

/// <summary>
///     The day's totals, shared by the Intake facade (what Monitoring and the composers read) and the meal ideas
///     (IA-3, what is left today), so both add up the same way.
/// </summary>
/// <remarks>
///     Computed rather than stored: a stored total would drift the moment a catalog entry is corrected. The catalog
///     holds nutrients per 100 grams, the entry holds the portion. Only confirmed estimates are counted: a proposal
///     the patient has not spoken about is a model's guess. MA-1: every confirmed entry counts, whatever its
///     answer; an off-plan meal logged with a food and a portion is part of the day's intake.
/// </remarks>
public static class DailyIntakeCalculator
{
    private const decimal NutrientBasisGrams = 100m;

    public static async Task<DailyIntakeTotals> SumAsync(IEnumerable<DiaryEntry> entries,
        IFoodCatalogContextFacade foodCatalogContextFacade, CancellationToken ct = default)
    {
        decimal energy = 0m, protein = 0m, carb = 0m, fat = 0m, offPlanEnergy = 0m;

        foreach (var entry in entries.Where(e => e.HasConfirmedEstimate))
        {
            var confirmed = entry.ConfirmedEstimate!;
            var food = await foodCatalogContextFacade.GetReferenceFoodById(confirmed.ReferenceFoodId, ct);
            if (food is null) continue;

            var factor = confirmed.PortionGrams / NutrientBasisGrams;
            energy += food.EnergyKcalPer100g * factor;
            if (entry.PlanAdherence.IsOffPlan) offPlanEnergy += food.EnergyKcalPer100g * factor;
            protein += food.ProteinGPer100g * factor;
            carb += food.CarbGPer100g * factor;
            fat += food.FatGPer100g * factor;
        }

        return new DailyIntakeTotals(decimal.Round(energy, 2), decimal.Round(protein, 2), decimal.Round(carb, 2),
            decimal.Round(fat, 2), decimal.Round(offPlanEnergy, 2));
    }
}
