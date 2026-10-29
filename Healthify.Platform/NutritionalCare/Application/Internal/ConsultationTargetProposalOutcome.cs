using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     The "Calculated with" line (sex, age, height, weight, activity level): the inputs the
///     equations read, all from the assessment of the consultation.
/// </summary>
public record TargetInputsSummary(string Sex, int AgeYears, decimal HeightCm, decimal WeightKg, string ActivityLevel);

/// <summary>What the target proposal of step 3 produced: the draft plan version and its inputs.</summary>
/// <param name="Consultation">The consultation, now on step 4.</param>
/// <param name="Plan">The draft plan version, proposed and not yet prescribed.</param>
/// <param name="Inputs">"Calculated with": what the equations read.</param>
/// <param name="ActivePlanLegacyRestrictions">
///     Free text restrictions of the version in force that match no code. The practitioner chooses the
///     equivalent code for the new version; what is not mapped is left out of it and recorded.
/// </param>
public record ConsultationTargetProposalOutcome(
    Consultation Consultation,
    NutritionPlan Plan,
    TargetInputsSummary Inputs,
    IReadOnlyList<string> ActivePlanLegacyRestrictions);

/// <summary>What the prescription of step 3 produced: the prescribed draft plan version.</summary>
public record ConsultationTargetsOutcome(Consultation Consultation, NutritionPlan Plan);
