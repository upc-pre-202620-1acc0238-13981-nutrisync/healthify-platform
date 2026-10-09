namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     IA-8/NC-10. The daily energy a plan adjusted because of a signal never goes below: 1 200 kcal for a woman and
///     1 500 kcal for a man by default, configurable (<c>NutritionalCare:CalorieFloorKcal:Female|Male</c>).
/// </summary>
/// <remarks>
///     Business rule: Calorie Floor (NC-10). It bounds the AI proposal and the practitioner's edits of it alike: the
///     only hard rule a human edit of a proposal meets.
/// </remarks>
public interface ICalorieFloorPolicy
{
    /// <summary>
    ///     The floor for <paramref name="biologicalSex" /> (<c>Female</c> or <c>Male</c>). When the sex is not
    ///     recorded, the higher of the two: the safe side.
    /// </summary>
    decimal FloorFor(string? biologicalSex);
}
