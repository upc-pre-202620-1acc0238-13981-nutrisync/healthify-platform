using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

/// <summary>The value objects of a target proposal, already validated.</summary>
internal sealed record ValidatedTargetParameters(
    Equation Equation,
    ReferenceWeight ReferenceWeight,
    decimal ActivityFactor,
    DeficitStrategy Deficit,
    decimal ProteinGramsPerKg,
    decimal FatPercentOfEnergy);

/// <summary>
///     Subflow 3.3 - the arithmetic of Propose Targets, shared by the stand-alone endpoint and step 3 of
///     the guided consultation (NC-5), so both produce exactly the same numbers from the same parameters.
/// </summary>
internal static class TargetCalculation
{
    private const decimal KcalPerGramProtein = 4m;
    private const decimal KcalPerGramCarbohydrate = 4m;
    private const decimal KcalPerGramFat = 9m;

    /// <summary>
    ///     Business rule: Complete Calculation Basis Required (Subflow 3.3). Each component reports its own
    ///     error, so the practitioner learns which parameter is wrong.
    /// </summary>
    public static NutritionalCareError? TryValidate(ProposeTargetsCommand command,
        out ValidatedTargetParameters? parameters)
    {
        parameters = null;

        Equation equation;
        try
        {
            equation = new Equation(command.Equation);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.UnsupportedEquation;
        }

        ReferenceWeight referenceWeight;
        try
        {
            referenceWeight = new ReferenceWeight(command.ReferenceWeightKind, command.ReferenceWeightKg);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidReferenceWeight;
        }

        DeficitStrategy deficitStrategy;
        try
        {
            deficitStrategy = new DeficitStrategy(command.DeficitKind, command.DeficitValue);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidDeficitStrategy;
        }

        if (command.ActivityFactor is < 1.0m or > 2.5m)
            return NutritionalCareError.InvalidActivityFactor;
        if (command.ProteinGramsPerKg is <= 0m or > 4m || command.FatPercentOfEnergy is < 10m or > 60m)
            return NutritionalCareError.IncompleteCalculationBasis;

        parameters = new ValidatedTargetParameters(equation, referenceWeight, command.ActivityFactor,
            deficitStrategy, command.ProteinGramsPerKg, command.FatPercentOfEnergy);
        return null;
    }

    /// <summary>
    ///     The arithmetic, in the order the rules state it, on the age and sex of
    ///     <paramref name="assessment" /> and the height of <paramref name="measurement" />.
    /// </summary>
    public static NutritionalCareError? TryCalculate(IBmrCalculator bmrCalculator, ValidatedTargetParameters p,
        NutritionalAssessment assessment, ClinicalMeasurement measurement, out CalculationBasis? basis,
        out TargetProposal? proposal)
    {
        basis = null;
        proposal = null;

        if (p.Equation.RequiresBodyFatPercentage && measurement.BodyFatPercentage is null)
            return NutritionalCareError.ClinicalMeasurementRequired;

        // Business rule: BMR From Selected Equation (Subflow 3.3)
        var bmr = bmrCalculator.ComputeBmr(p.Equation, new BmrInputs(
            p.ReferenceWeight.ValueKg,
            measurement.HeightCm,
            assessment.AgeYears,
            assessment.BiologicalSex,
            measurement.BodyFatPercentage));

        // Business rule: TDEE Equals BMR Times Activity Factor (Subflow 3.3)
        var tdee = decimal.Round(bmr * p.ActivityFactor, 2);

        // Business rule: Target Energy Equals TDEE Minus Deficit (Subflow 3.3)
        var targetEnergy = decimal.Round(tdee - p.Deficit.DeficitKcalFor(tdee), 2);
        if (targetEnergy <= 0m)
            return NutritionalCareError.InvalidDeficitStrategy;

        // Business rule: Macros From Protein Per Kg And Fat Percentage (Subflow 3.3)
        // Protein from grams per kilogram of the reference weight, fat from a share of the target
        // energy, and carbohydrate by difference: what is left once the other two are paid for.
        var proteinG = decimal.Round(p.ProteinGramsPerKg * p.ReferenceWeight.ValueKg, 2);
        var fatG = decimal.Round(p.FatPercentOfEnergy / 100m * targetEnergy / KcalPerGramFat, 2);
        var remainingKcal = targetEnergy - proteinG * KcalPerGramProtein - fatG * KcalPerGramFat;
        if (remainingKcal < 0m)
            return NutritionalCareError.IncompleteCalculationBasis;
        var carbG = decimal.Round(remainingKcal / KcalPerGramCarbohydrate, 2);

        basis = CalculationBasis.From(p.Equation, p.ReferenceWeight, p.ActivityFactor, p.Deficit, bmr, tdee);
        proposal = new TargetProposal(targetEnergy, proteinG, carbG, fatG);
        return null;
    }

    /// <summary>
    ///     Business rule: Previous Proposal Required (Subflow 3.4). Accepting means signing the numbers the
    ///     arithmetic produced; overriding means replacing them and saying why.
    /// </summary>
    public static PrescribedTargets BuildPrescription(NutritionPlan plan, PrescriptionOutcome outcome,
        decimal? energyKcal, decimal? proteinG, decimal? carbG, decimal? fatG, string? overrideReason)
    {
        return outcome.IsOverridden
            ? new PrescribedTargets(
                energyKcal ?? plan.TargetProposal.EnergyKcal,
                proteinG ?? plan.TargetProposal.ProteinG,
                carbG ?? plan.TargetProposal.CarbG,
                fatG ?? plan.TargetProposal.FatG,
                outcome,
                new OverrideReason(overrideReason!))
            : new PrescribedTargets(
                plan.TargetProposal.EnergyKcal,
                plan.TargetProposal.ProteinG,
                plan.TargetProposal.CarbG,
                plan.TargetProposal.FatG,
                outcome);
    }
}
