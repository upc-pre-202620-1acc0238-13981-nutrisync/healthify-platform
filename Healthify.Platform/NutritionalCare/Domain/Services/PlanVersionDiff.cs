using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     NC-8 - "Qué cambió en esta versión" (PT4). Compares two published versions of the same plan through
///     what the patient receives: the daily targets, the guidelines and the restrictions.
/// </summary>
/// <remarks>
///     Pure and deterministic: it reads nothing else, so the same two versions always give the same list.
///     Business rule: Diagnosis And Basis Never Leave The Context (Subflow 3.5). The diagnosis, the
///     calculation basis and the override reason are not compared and never appear in a change.
/// </remarks>
public static class PlanVersionDiff
{
    /// <summary>
    ///     The changes of <paramref name="current" /> against <paramref name="previous" />, in a stable order:
    ///     targets first, then guidelines, then restrictions. The first version has nothing to compare against,
    ///     so its list is empty.
    /// </summary>
    public static IReadOnlyList<PlanChange> Compare(NutritionPlan? previous, NutritionPlan current)
    {
        if (previous is null) return [];

        var changes = new List<PlanChange>();

        var before = previous.PrescribedTargets;
        var after = current.PrescribedTargets;
        if (before is not null && after is not null)
        {
            if (before.EnergyKcal != after.EnergyKcal)
                changes.Add(PlanChange.EnergyWasChanged(before.EnergyKcal, after.EnergyKcal));
            if (before.ProteinG != after.ProteinG)
                changes.Add(PlanChange.MacroWasChanged(PlanChange.Protein, before.ProteinG, after.ProteinG));
            if (before.CarbG != after.CarbG)
                changes.Add(PlanChange.MacroWasChanged(PlanChange.Carb, before.CarbG, after.CarbG));
            if (before.FatG != after.FatG)
                changes.Add(PlanChange.MacroWasChanged(PlanChange.Fat, before.FatG, after.FatG));
        }

        // PT4 "Las metas de energía y macronutrientes no cambiaron."
        if (changes.Count == 0) changes.Add(PlanChange.NoTargetsChanged());

        changes.AddRange(current.Guidelines.Where(g => !previous.Guidelines.Contains(g))
            .Select(PlanChange.GuidelineWasAdded));
        changes.AddRange(previous.Guidelines.Where(g => !current.Guidelines.Contains(g))
            .Select(PlanChange.GuidelineWasRemoved));

        changes.AddRange(current.Restrictions.Where(r => !previous.Restrictions.Contains(r))
            .Select(PlanChange.RestrictionWasAdded));
        changes.AddRange(previous.Restrictions.Where(r => !current.Restrictions.Contains(r))
            .Select(PlanChange.RestrictionWasRemoved));
        // NC-6: a free text restriction the new version did not carry over is, for the patient, a removed one.
        changes.AddRange(previous.LegacyRestrictions.Where(r => !current.LegacyRestrictions.Contains(r))
            .Select(PlanChange.LegacyRestrictionWasRemoved));

        return changes;
    }
}
