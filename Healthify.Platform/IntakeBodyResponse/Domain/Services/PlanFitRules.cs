using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Services;

/// <summary>
///     How a food is suggested in or off the plan, the same for a photo and for a meal logged by hand. Pure: the
///     caller brings the restrictions, what is left today and the energy of the portion.
/// </summary>
/// <remarks>
///     Restrictions first: a food that seems to break one is suggested off the plan whatever its energy, and the
///     lexicon errs on the side of the patient (<see cref="IRestrictionLexicon" />). Then the energy, with a margin so
///     that a portion a little above what is left still reads as in the plan. Starting point for the MVP, to be
///     reviewed with the nutritionist; a change here needs a test.
/// </remarks>
public static class PlanFitRules
{
    /// <summary>How far above what is left a portion may go and still be suggested in the plan.</summary>
    public const decimal ExceedsRemainingToleranceKcal = 100m;

    /// <param name="names">Every name the food goes by: the catalog's and, from a photo, the AI's.</param>
    /// <param name="energyKcal">The energy of the portion.</param>
    /// <param name="remaining">What is left today, or null when the day is unknown (the energy is then not judged).</param>
    /// <param name="restrictions">The restriction codes of the plan.</param>
    /// <param name="lexicon">The ingredient map.</param>
    public static PlanFit Evaluate(IEnumerable<string> names, decimal energyKcal, RemainingTargets? remaining,
        IReadOnlyCollection<string> restrictions, IRestrictionLexicon lexicon)
    {
        var energy = decimal.Round(Math.Max(0m, energyKcal), 0);

        // Business rule: Restrictions Of The Plan Come First.
        var violated = names.Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => lexicon.ViolatedRestrictions(n, restrictions))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (violated.Count > 0)
            return new PlanFit(PlanAdherence.OffPlan, violated, PlanFitReason.BreaksRestriction, energy,
                remaining?.EnergyKcal);

        if (remaining is not null && energy > remaining.EnergyKcal + ExceedsRemainingToleranceKcal)
            return new PlanFit(PlanAdherence.OffPlan, [], PlanFitReason.ExceedsRemaining, energy, remaining.EnergyKcal);

        return new PlanFit(PlanAdherence.InPlan, [], PlanFitReason.Fits, energy, remaining?.EnergyKcal);
    }
}
