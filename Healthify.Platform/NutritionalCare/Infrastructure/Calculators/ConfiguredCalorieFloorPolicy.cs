using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Calculators;

/// <summary>
///     <see cref="ICalorieFloorPolicy" /> from <c>NutritionalCare:CalorieFloorKcal:Female</c> (1 200) and
///     <c>NutritionalCare:CalorieFloorKcal:Male</c> (1 500), defaults in code.
/// </summary>
public class ConfiguredCalorieFloorPolicy(IConfiguration configuration) : ICalorieFloorPolicy
{
    public const decimal DefaultFemaleFloorKcal = 1200m;
    public const decimal DefaultMaleFloorKcal = 1500m;

    public decimal FloorFor(string? biologicalSex)
    {
        var female = configuration.GetValue<decimal?>("NutritionalCare:CalorieFloorKcal:Female") ??
                     DefaultFemaleFloorKcal;
        var male = configuration.GetValue<decimal?>("NutritionalCare:CalorieFloorKcal:Male") ?? DefaultMaleFloorKcal;

        if (string.Equals(biologicalSex, BiologicalSex.Female, StringComparison.OrdinalIgnoreCase)) return female;
        if (string.Equals(biologicalSex, BiologicalSex.Male, StringComparison.OrdinalIgnoreCase)) return male;
        // Without a recorded sex, the higher floor (the design gives one per sex and none for unknown).
        return Math.Max(female, male);
    }
}
