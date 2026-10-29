namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;

// ---------------------------------------------------------------------------------------------
// Request resources
// ---------------------------------------------------------------------------------------------

/// <summary>Eating habits of step 1. Every value is optional.</summary>
public record EatingHabitsResource(int? MealsPerDay, decimal? WaterLitersPerDay, int? MealsOutPerWeek)
{
    /// <summary>Meals per day, 1 to 10.</summary>
    public int? MealsPerDay { get; init; } = MealsPerDay;

    /// <summary>Water per day in litres, 0 to 10.</summary>
    public decimal? WaterLitersPerDay { get; init; } = WaterLitersPerDay;

    /// <summary>Meals eaten out per week, 0 to 21.</summary>
    public int? MealsOutPerWeek { get; init; } = MealsOutPerWeek;
}

/// <summary>Biochemistry of step 1, in mg/dL. Every value is optional.</summary>
public record BiochemistryPanelResource(
    decimal? FastingGlucoseMgDl,
    decimal? TotalCholesterolMgDl,
    decimal? TriglyceridesMgDl)
{
    /// <summary>Fasting glucose, 20 to 600 mg/dL.</summary>
    public decimal? FastingGlucoseMgDl { get; init; } = FastingGlucoseMgDl;

    /// <summary>Total cholesterol, 50 to 500 mg/dL.</summary>
    public decimal? TotalCholesterolMgDl { get; init; } = TotalCholesterolMgDl;

    /// <summary>Triglycerides, 20 to 2000 mg/dL.</summary>
    public decimal? TriglyceridesMgDl { get; init; } = TriglyceridesMgDl;
}

/// <summary>Payload of Subflow 3.6 - Adjust Nutrition Plan.</summary>
public record AdjustNutritionPlanResource(
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    string ChangeReason,
    IReadOnlyList<string>? CustomGuidelines = null,
    string? PatientMessage = null)
{
    /// <summary>New energy target.</summary>
    public decimal EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>New protein target.</summary>
    public decimal ProteinG { get; init; } = ProteinG;

    /// <summary>New carbohydrate target.</summary>
    public decimal CarbG { get; init; } = CarbG;

    /// <summary>New fat target.</summary>
    public decimal FatG { get; init; } = FatG;

    /// <summary>Guidance for the new version: catalog codes; any other text is kept as a custom guideline.</summary>
    public IReadOnlyList<string> Guidelines { get; init; } = Guidelines;

    /// <summary>
    ///     Restrictions for the new version, as codes. Legacy restrictions of the current version that
    ///     are not mapped to a code here are left out of the new one and recorded on it.
    /// </summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>Why this version exists. Required for every new version, without exception.</summary>
    public string ChangeReason { get; init; } = ChangeReason;

    /// <summary>Custom guidelines: 3 to 140 characters each, at most 5 per version.</summary>
    public IReadOnlyList<string>? CustomGuidelines { get; init; } = CustomGuidelines;

    /// <summary>
    ///     Optional message for the patient with the new version, at most 500 characters. Never the
    ///     diagnosis. Not carried over from the previous version.
    /// </summary>
    public string? PatientMessage { get; init; } = PatientMessage;
}

/// <summary>Payload of Subflow 3.7 - Resolve Review Item.</summary>
public record ResolveReviewItemResource(bool? ResolvedWithAdjustment, string? ResolutionNote)
{
    /// <summary>
    ///     Whether the plan was adjusted because of this signal. Required: closing an item without
    ///     saying what it caused would lose the only record of the decision.
    /// </summary>
    public bool? ResolvedWithAdjustment { get; init; } = ResolvedWithAdjustment;

    /// <summary>Optional note from the practitioner.</summary>
    public string? ResolutionNote { get; init; } = ResolutionNote;
}

/// <summary>Payload to record the patient baseline.</summary>
public record RecordPatientBaselineResource(
    DateOnly BirthDate,
    string BiologicalSex,
    decimal HeightCm,
    IReadOnlyList<string>? Conditions)
{
    /// <summary>Birth date (yyyy-MM-dd), between 1 and 120 years ago. The age is derived from it.</summary>
    public DateOnly BirthDate { get; init; } = BirthDate;

    /// <summary>Female or Male.</summary>
    public string BiologicalSex { get; init; } = BiologicalSex;

    /// <summary>Height in centimetres, 50 to 250, one decimal. Not asked again at each consultation.</summary>
    public decimal HeightCm { get; init; } = HeightCm;

    /// <summary>
    ///     Medical history from the closed list: Type2Diabetes, Hypertension, CeliacDisease,
    ///     Hypothyroidism, ChronicKidneyDisease, Gout. Empty or omitted means none.
    /// </summary>
    public IReadOnlyList<string>? Conditions { get; init; } = Conditions;
}

/// <summary>Payload to edit the patient baseline.</summary>
public record UpdatePatientBaselineResource(
    DateOnly BirthDate,
    string BiologicalSex,
    decimal HeightCm,
    IReadOnlyList<string>? Conditions)
{
    /// <summary>Birth date (yyyy-MM-dd), between 1 and 120 years ago.</summary>
    public DateOnly BirthDate { get; init; } = BirthDate;

    /// <summary>Female or Male.</summary>
    public string BiologicalSex { get; init; } = BiologicalSex;

    /// <summary>Height in centimetres, 50 to 250, one decimal.</summary>
    public decimal HeightCm { get; init; } = HeightCm;

    /// <summary>Medical history from the closed list. Empty or omitted means none.</summary>
    public IReadOnlyList<string>? Conditions { get; init; } = Conditions;
}
