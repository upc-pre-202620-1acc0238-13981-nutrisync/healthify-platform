using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     Guard 6 of the plan adjustment proposal. The hard validation, on the server, once it is schema-valid JSON:
///     energy within ±25 % of the version in force, never below the
///     calorie floor, macros that add up to the energy within 2 %, no new restriction, guidelines of the catalog only,
///     and a message for the patient that invites and never names the diagnosis.
/// </summary>
/// <remarks>
///     A rejected proposal is never attached: the item stays without AI. No restriction can come from the model:
///     the schema has no field for one, guidelines must be catalog codes, and the acceptance carries the restrictions
///     of the version in force unchanged.
/// </remarks>
public sealed class PlanAdjustmentProposalOutputValidator(PlanAdjustmentInput input, IPatientMessageLexicon lexicon)
    : IAiOutputValidator<PlanAdjustmentProposalOutput>
{
    /// <summary>The patient message has at most 300 characters.</summary>
    public const int MaximumPatientMessageLength = 300;

    /// <summary>The rationale has at most 600 characters.</summary>
    public const int MaximumRationaleLength = 600;

    public const int MaximumTitleLength = 120;

    /// <summary>"recheckAfterDays 3..30".</summary>
    public const int MinimumRecheckAfterDays = 3;

    public const int MaximumRecheckAfterDays = 30;

    public IReadOnlyList<string> Validate(PlanAdjustmentProposalOutput output)
    {
        var violations = new List<string>();
        var current = input.CurrentPlan;

        // Business rule: Energy Within 25 Percent Of The Version In Force.
        if (!PlanAdjustmentSafety.IsWithinAdjustmentBand(current.EnergyKcal, output.EnergyKcal))
            violations.Add($"$.energyKcal: {output.EnergyKcal} is more than 25 % away from {current.EnergyKcal}.");
        // Business rule: Calorie Floor.
        if (!PlanAdjustmentSafety.IsAtOrAboveFloor(output.EnergyKcal, input.CalorieFloorKcal))
            violations.Add($"$.energyKcal: {output.EnergyKcal} is below the floor of {input.CalorieFloorKcal}.");
        // Business rule: Macros Coherent With The Energy.
        if (!PlanAdjustmentSafety.AreMacrosCoherent(output.EnergyKcal, output.ProteinG, output.CarbG, output.FatG))
            violations.Add("$.proteinG/carbG/fatG: 4P + 4C + 9F is not within 2 % of energyKcal.");

        // Business rule: Closed Guideline Catalog, and no restriction from the AI.
        var added = output.AddedGuidelines ?? [];
        var removed = output.RemovedGuidelines ?? [];
        foreach (var code in added)
            if (!Guideline.Codes.Contains(code, StringComparer.Ordinal))
                violations.Add($"$.addedGuidelines: '{code}' is not a code of the guideline catalog.");
            else if (current.GuidelineCodes.Contains(code, StringComparer.Ordinal))
                violations.Add($"$.addedGuidelines: '{code}' is already in the version in force.");
        foreach (var code in removed)
            if (!current.GuidelineCodes.Contains(code, StringComparer.Ordinal))
                violations.Add($"$.removedGuidelines: '{code}' is not in the version in force.");
        if (added.Intersect(removed, StringComparer.Ordinal).Any())
            violations.Add("$.addedGuidelines/removedGuidelines: a code is both added and removed.");
        if (added.Distinct(StringComparer.Ordinal).Count() != added.Count
            || removed.Distinct(StringComparer.Ordinal).Count() != removed.Count)
            violations.Add("$.addedGuidelines/removedGuidelines: a code appears more than once.");

        // Business rules: Invitation Tone Never Accusation and Diagnosis Never Leaves The Context.
        if (string.IsNullOrWhiteSpace(output.PatientMessage))
        {
            violations.Add("$.patientMessage: empty.");
        }
        else
        {
            if (output.PatientMessage.Trim().Length > MaximumPatientMessageLength)
                violations.Add($"$.patientMessage: longer than {MaximumPatientMessageLength} characters.");
            foreach (var term in PatientMessageRules.TermsIn(output.PatientMessage, lexicon.AccusatoryTerms))
                violations.Add($"$.patientMessage: accusatory term '{term}'.");
            IEnumerable<string> diagnosisTerms = input.DiagnosisCode is null
                ? lexicon.DiagnosisTerms
                : lexicon.DiagnosisTerms.Append(input.DiagnosisCode);
            foreach (var term in PatientMessageRules.TermsIn(output.PatientMessage, diagnosisTerms))
                violations.Add($"$.patientMessage: diagnosis term '{term}'.");
        }

        if (output.RecheckAfterDays is < MinimumRecheckAfterDays or > MaximumRecheckAfterDays)
            violations.Add($"$.recheckAfterDays: between {MinimumRecheckAfterDays} and {MaximumRecheckAfterDays}.");
        if (string.IsNullOrWhiteSpace(output.Title) || output.Title.Trim().Length > MaximumTitleLength)
            violations.Add($"$.title: 1 to {MaximumTitleLength} characters.");
        if (string.IsNullOrWhiteSpace(output.Rationale) || output.Rationale.Trim().Length > MaximumRationaleLength)
            violations.Add($"$.rationale: 1 to {MaximumRationaleLength} characters.");

        // Each part in the language of whoever reads it; the model states which ones it wrote.
        if (!string.Equals(output.PractitionerLanguage, input.PractitionerReadingLanguage, StringComparison.Ordinal))
            violations.Add($"$.practitionerLanguage: expected '{input.PractitionerReadingLanguage}'.");
        if (!string.Equals(output.PatientLanguage, input.PatientLanguage, StringComparison.Ordinal))
            violations.Add($"$.patientLanguage: expected '{input.PatientLanguage}'.");

        return violations;
    }
}
