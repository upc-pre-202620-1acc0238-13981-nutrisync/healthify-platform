namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     IA-8/NC-10. The hard rules of a plan adjustment proposed because of a signal. Static domain service: no state,
///     no I/O; the floor comes from <see cref="ICalorieFloorPolicy" />.
/// </summary>
/// <remarks>
///     Business rules (NC-10): Energy Within 25 Percent Of The Version In Force; Calorie Floor; Macros Coherent With
///     The Energy (4 kcal per gram of protein and carbohydrate, 9 per gram of fat, within 2 %). A proposal that breaks
///     one is discarded and PR14 shows the item without AI. A practitioner's edit of a proposal meets only the floor.
/// </remarks>
public static class PlanAdjustmentSafety
{
    /// <summary>"±25 % de la vigente".</summary>
    public const decimal MaximumEnergyChange = 0.25m;

    /// <summary>"deben sumar la energía ±2 %".</summary>
    public const decimal MacroTolerance = 0.02m;

    /// <summary>Business rule: Energy Within 25 Percent Of The Version In Force (NC-10).</summary>
    public static bool IsWithinAdjustmentBand(decimal currentEnergyKcal, decimal proposedEnergyKcal)
    {
        if (currentEnergyKcal <= 0m) return false;
        return Math.Abs(proposedEnergyKcal - currentEnergyKcal) <= currentEnergyKcal * MaximumEnergyChange;
    }

    /// <summary>Business rule: Calorie Floor (NC-10).</summary>
    public static bool IsAtOrAboveFloor(decimal proposedEnergyKcal, decimal floorKcal)
    {
        return proposedEnergyKcal >= floorKcal;
    }

    /// <summary>Business rule: Macros Coherent With The Energy (NC-10).</summary>
    public static bool AreMacrosCoherent(decimal energyKcal, decimal proteinG, decimal carbG, decimal fatG)
    {
        if (energyKcal <= 0m || proteinG < 0m || carbG < 0m || fatG < 0m) return false;
        var fromMacros = 4m * proteinG + 4m * carbG + 9m * fatG;
        return Math.Abs(fromMacros - energyKcal) <= energyKcal * MacroTolerance;
    }

    /// <summary>The lowest energy a proposal may have: the floor, or 25 % below the version in force.</summary>
    public static decimal MinimumEnergyKcal(decimal currentEnergyKcal, decimal floorKcal)
    {
        return Math.Max(floorKcal, decimal.Round(currentEnergyKcal * (1m - MaximumEnergyChange), 0));
    }

    /// <summary>The highest energy a proposal may have: 25 % above the version in force.</summary>
    public static decimal MaximumEnergyKcal(decimal currentEnergyKcal)
    {
        return decimal.Round(currentEnergyKcal * (1m + MaximumEnergyChange), 0);
    }
}
