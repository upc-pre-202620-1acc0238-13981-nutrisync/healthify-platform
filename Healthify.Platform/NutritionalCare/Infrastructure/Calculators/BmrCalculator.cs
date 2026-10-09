using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Calculators;

/// <summary>
///     The four published equations, selected by <see cref="CalculationBasis.Equation" />.
/// </summary>
/// <remarks>
///     Every constant below comes from the published literature and every result is reproducible with
///     a pocket calculator. Worked examples for a 30 year old male, 80 kg, 180 cm, 20 percent body
///     fat, are in the README so the arithmetic can be checked by hand.
/// </remarks>
public class BmrCalculator : IBmrCalculator
{
    public decimal ComputeBmr(Equation equation, BmrInputs inputs)
    {
        // Business rule: BMR From Selected Equation (Nutritional Care, Subflow 3.3)
        var bmr = equation.Value switch
        {
            Equation.MifflinStJeor => MifflinStJeor(inputs),
            Equation.HarrisBenedict => HarrisBenedict(inputs),
            Equation.FaoWhoUnu => FaoWhoUnu(inputs),
            Equation.KatchMcArdle => KatchMcArdle(inputs),
            _ => throw new ArgumentOutOfRangeException(nameof(equation), equation.Value,
                "No calculator is registered for this equation.")
        };

        return decimal.Round(bmr, 2);
    }

    /// <summary>
    ///     Mifflin-St Jeor (1990): 10 W + 6.25 H - 5 A + s, where s is +5 for males and -161 for
    ///     females.
    /// </summary>
    private static decimal MifflinStJeor(BmrInputs i)
    {
        var sexConstant = i.BiologicalSex.IsMale ? 5m : -161m;
        return 10m * i.ReferenceWeightKg + 6.25m * i.HeightCm - 5m * i.AgeYears + sexConstant;
    }

    /// <summary>
    ///     Harris-Benedict as revised by Roza and Shizgal (1984). Two separate formulas rather than a
    ///     shared one with a constant.
    /// </summary>
    private static decimal HarrisBenedict(BmrInputs i)
    {
        return i.BiologicalSex.IsMale
            ? 88.362m + 13.397m * i.ReferenceWeightKg + 4.799m * i.HeightCm - 5.677m * i.AgeYears
            : 447.593m + 9.247m * i.ReferenceWeightKg + 3.098m * i.HeightCm - 4.330m * i.AgeYears;
    }

    /// <summary>
    ///     FAO/WHO/UNU (1985): banded by sex and age, and driven by weight alone. Height does not
    ///     appear in it, which is exactly why the practitioner chooses the equation rather than the
    ///     system.
    /// </summary>
    private static decimal FaoWhoUnu(BmrInputs i)
    {
        var w = i.ReferenceWeightKg;

        if (i.BiologicalSex.IsMale)
            return i.AgeYears switch
            {
                < 3 => 60.9m * w - 54m,
                < 10 => 22.7m * w + 495m,
                < 18 => 17.5m * w + 651m,
                < 30 => 15.057m * w + 692.2m,
                < 60 => 11.472m * w + 873.1m,
                _ => 11.711m * w + 587.7m
            };

        return i.AgeYears switch
        {
            < 3 => 61m * w - 51m,
            < 10 => 22.5m * w + 499m,
            < 18 => 12.2m * w + 746m,
            < 30 => 14.818m * w + 486.6m,
            < 60 => 8.126m * w + 845.6m,
            _ => 9.082m * w + 658.5m
        };
    }

    /// <summary>
    ///     Katch-McArdle: 370 + 21.6 LBM, where lean body mass is weight times one minus the body fat
    ///     fraction. The only one of the four that ignores age and sex, and the only one that needs a
    ///     body composition reading.
    /// </summary>
    private static decimal KatchMcArdle(BmrInputs i)
    {
        if (i.BodyFatPercentage is null)
            throw new ArgumentException(
                "The Katch-McArdle equation needs a body fat percentage from a clinical measurement.",
                nameof(i));

        var leanBodyMass = i.ReferenceWeightKg * (1m - i.BodyFatPercentage.Value / 100m);
        return 370m + 21.6m * leanBodyMass;
    }
}
