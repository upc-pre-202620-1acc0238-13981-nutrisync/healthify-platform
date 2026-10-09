namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-5. "Cambiar parámetros" in EV-4: the same shape as a target proposal. Every value is optional;
///     what is left out keeps its default (<c>IDefaultTargetParametersPolicy</c>).
/// </summary>
/// <param name="Equation">MifflinStJeor, HarrisBenedict, FaoWhoUnu or KatchMcArdle.</param>
/// <param name="ReferenceWeightKind">Actual, Ideal or Adjusted. Ideal and Adjusted need <paramref name="ReferenceWeightKg" />.</param>
/// <param name="ReferenceWeightKg">Defaults to the weight measured in step 1.</param>
/// <param name="ActivityFactor">Defaults to the factor of the activity level of step 1.</param>
/// <param name="DeficitKind">FixedKcal or PercentOfTdee.</param>
/// <param name="DeficitValue">kcal or percentage, by <paramref name="DeficitKind" />.</param>
/// <param name="ProteinGramsPerKg">Grams of protein per kilogram of the reference weight.</param>
/// <param name="FatPercentOfEnergy">Share of the target energy that comes from fat.</param>
public record TargetParametersDto(
    string? Equation = null,
    string? ReferenceWeightKind = null,
    decimal? ReferenceWeightKg = null,
    decimal? ActivityFactor = null,
    string? DeficitKind = null,
    decimal? DeficitValue = null,
    decimal? ProteinGramsPerKg = null,
    decimal? FatPercentOfEnergy = null);
