namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     IA-8. What the plan adjustment proposal is generated from (MD IA-8 "Input"). Numbers and codes only: no name,
///     no identifier that reaches the model, no free text the patient wrote, no photo.
/// </summary>
/// <param name="PatientId">Whose plan. Audited by the pipeline; never sent to the provider.</param>
/// <param name="PractitionerId">Whose inbox: the quota of the function is counted per practitioner.</param>
/// <param name="Language"><c>es</c> or <c>en</c>: the language the patient reads the message in.</param>
/// <param name="Evidence">The structured evidence of the deviation (NC-11).</param>
/// <param name="MealSlotsOnDeviatedDays">The slots of the day logged on the deviated days.</param>
/// <param name="WeightTrend">The self-weighing trend (IN-5), or null.</param>
/// <param name="CurrentPlan">The version in force.</param>
/// <param name="DiagnosisCode">The active diagnosis code (a practitioner function may read it), or null.</param>
/// <param name="CalorieFloorKcal">The floor for the patient's sex (<c>ICalorieFloorPolicy</c>).</param>
/// <param name="PractitionerLanguage">
///     X-2. <c>es</c> or <c>en</c>: the language the practitioner reads the title and the rationale in. Null means the
///     same as <paramref name="Language" />.
/// </param>
public record PlanAdjustmentInput(
    int PatientId,
    int PractitionerId,
    string Language,
    PlanAdjustmentEvidence Evidence,
    PlanAdjustmentMealSlots MealSlotsOnDeviatedDays,
    PlanAdjustmentWeightTrend? WeightTrend,
    PlanAdjustmentCurrentPlan CurrentPlan,
    string? DiagnosisCode,
    decimal CalorieFloorKcal,
    string? PractitionerLanguage = null)
{
    /// <summary>X-2. The language of <c>patientMessage</c>: the patient reads it.</summary>
    public string PatientLanguage => Language;

    /// <summary>X-2. The language of <c>title</c> and <c>rationale</c>: the practitioner reads them.</summary>
    public string PractitionerReadingLanguage => PractitionerLanguage ?? Language;
}

/// <summary>IA-8. "porcentaje, días y dirección".</summary>
public record PlanAdjustmentEvidence(
    decimal? AveragePercentFromTarget,
    int? DeviatedDays,
    int? LoggedDaysConsidered,
    string? Direction);

/// <summary>IA-8. On how many of the deviated days something was logged in each slot, out of <c>Days</c>.</summary>
public record PlanAdjustmentMealSlots(int Days, int Breakfast, int Lunch, int Dinner, int Other);

/// <summary>IA-8. Slope and change of the smoothed self-weighing trend.</summary>
public record PlanAdjustmentWeightTrend(decimal? SlopeKgPerWeek, decimal? ChangeKg, int PointCount);

/// <summary>IA-8. The targets, guideline codes and restriction codes of the version in force.</summary>
public record PlanAdjustmentCurrentPlan(
    int Version,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<string> GuidelineCodes,
    IReadOnlyList<string> RestrictionCodes);
