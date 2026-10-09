using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

/// <summary>
///     NC-3. Step 1 of the command services that receive the structured assessment of EV-2: builds each
///     value object and maps its failure to its own error, before anything is loaded.
/// </summary>
internal static class StructuredAssessmentInput
{
    public static NutritionalCareError? TryActivityLevel(string? code, out ActivityLevel? level)
    {
        level = null;
        if (code is null) return null;
        try
        {
            level = new ActivityLevel(code);
            return null;
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidActivityLevel;
        }
    }

    public static NutritionalCareError? TryEatingHabits(EatingHabitsDto? dto, out EatingHabits? habits)
    {
        habits = null;
        if (dto is null) return null;
        try
        {
            habits = EatingHabits.From(dto.MealsPerDay, dto.WaterLitersPerDay, dto.MealsOutPerWeek);
            return null;
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidEatingHabits;
        }
    }

    public static NutritionalCareError? TryBiochemistry(BiochemistryDto? dto, out BiochemistryPanel? panel)
    {
        panel = null;
        if (dto is null) return null;
        try
        {
            panel = BiochemistryPanel.From(dto.FastingGlucoseMgDl, dto.TotalCholesterolMgDl, dto.TriglyceridesMgDl);
            return null;
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.ImplausibleBiochemistry;
        }
    }

    public static NutritionalCareError? TryProtocolChecks(IReadOnlyList<string>? checks,
        out MeasurementProtocolChecklist? checklist)
    {
        checklist = null;

        // An empty list and an unknown code are different mistakes, each with its own error.
        if (checks is null || checks.Count == 0) return NutritionalCareError.ProtocolChecklistEmpty;
        if (!checks.All(MeasurementProtocolChecklist.IsKnown)) return NutritionalCareError.UnknownProtocolCheck;

        try
        {
            checklist = new MeasurementProtocolChecklist(checks);
            return null;
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.UnknownProtocolCheck;
        }
    }

    public static NutritionalCareError? CheckPlausibleMeasurement(decimal weightKg, decimal? waistCm,
        decimal? bodyFatPercentage)
    {
        try
        {
            ClinicalMeasurement.EnsurePlausible(weightKg, waistCm, bodyFatPercentage);
            return null;
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.ImplausibleMeasurement;
        }
    }
}
