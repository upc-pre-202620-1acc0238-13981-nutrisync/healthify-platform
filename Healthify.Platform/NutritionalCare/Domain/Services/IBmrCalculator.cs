using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>The facts an equation reads. Facts, not clinical choices: those arrive in the command.</summary>
public record BmrInputs(
    decimal ReferenceWeightKg,
    decimal HeightCm,
    int AgeYears,
    BiologicalSex BiologicalSex,
    decimal? BodyFatPercentage);

/// <summary>
///     Computes the basal metabolic rate from one of the four published equations.
/// </summary>
/// <remarks>
///     This is not an external service and it is not a model. It is deterministic arithmetic with
///     published equations: nothing leaves the application, and the result can be recomputed by hand
///     from the calculation basis that is stored with the plan. It sits behind an interface only so
///     that the four equations can be tested and swapped independently of the aggregate.
/// </remarks>
public interface IBmrCalculator
{
    /// <summary>Business rule: BMR From Selected Equation (Subflow 3.3).</summary>
    decimal ComputeBmr(Equation equation, BmrInputs inputs);
}
