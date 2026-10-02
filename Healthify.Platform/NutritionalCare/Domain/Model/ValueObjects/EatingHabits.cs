namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-3. Optional eating habits of EV-2: meals per day, water per day and meals out per week.
///     Every value is optional on its own; with none of them there are no habits at all.
/// </summary>
public sealed record EatingHabits
{
    public EatingHabits(int? mealsPerDay, decimal? waterLitersPerDay, int? mealsOutPerWeek)
    {
        if (mealsPerDay is < 1 or > 10)
            throw new ArgumentException("Meals per day must be between 1 and 10.", nameof(mealsPerDay));
        if (waterLitersPerDay is < 0m or > 10m)
            throw new ArgumentException("Water per day must be between 0 and 10 litres.",
                nameof(waterLitersPerDay));
        if (mealsOutPerWeek is < 0 or > 21)
            throw new ArgumentException("Meals out per week must be between 0 and 21.", nameof(mealsOutPerWeek));
        if (mealsPerDay is null && waterLitersPerDay is null && mealsOutPerWeek is null)
            throw new ArgumentException("Eating habits need at least one value.", nameof(mealsPerDay));

        MealsPerDay = mealsPerDay;
        WaterLitersPerDay = waterLitersPerDay is null ? null : decimal.Round(waterLitersPerDay.Value, 2);
        MealsOutPerWeek = mealsOutPerWeek;
    }

    public int? MealsPerDay { get; }
    public decimal? WaterLitersPerDay { get; }
    public int? MealsOutPerWeek { get; }

    /// <summary>Null when nothing was recorded, which is what "optional" means in EV-2.</summary>
    public static EatingHabits? From(int? mealsPerDay, decimal? waterLitersPerDay, int? mealsOutPerWeek)
    {
        return mealsPerDay is null && waterLitersPerDay is null && mealsOutPerWeek is null
            ? null
            : new EatingHabits(mealsPerDay, waterLitersPerDay, mealsOutPerWeek);
    }
}
