namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>NC-3. Optional eating habits as they arrive with a command; validated by EatingHabits.</summary>
public record EatingHabitsDto(int? MealsPerDay, decimal? WaterLitersPerDay, int? MealsOutPerWeek);
