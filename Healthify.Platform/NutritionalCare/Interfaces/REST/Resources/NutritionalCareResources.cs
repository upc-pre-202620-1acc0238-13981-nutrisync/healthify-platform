namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;

// ---------------------------------------------------------------------------------------------
// Request resources
// ---------------------------------------------------------------------------------------------

/// <summary>Payload of Subflow 3.1 - Record Assessment.</summary>
public record RecordAssessmentResource(
    int PatientId,
    string Habits,
    string MedicalHistory,
    string PhysicalActivity,
    string? Biochemistry,
    int AgeYears,
    string BiologicalSex,
    int? SupersedesAssessmentId,
    string? ActivityLevel = null,
    EatingHabitsResource? EatingHabits = null,
    BiochemistryPanelResource? BiochemistryPanel = null)
{
    /// <summary>Identifier of the patient being assessed. An active care link is required.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Eating habits. Required.</summary>
    public string Habits { get; init; } = Habits;

    /// <summary>Relevant medical history. Required.</summary>
    public string MedicalHistory { get; init; } = MedicalHistory;

    /// <summary>Physical activity as reported. Required.</summary>
    public string PhysicalActivity { get; init; } = PhysicalActivity;

    /// <summary>Laboratory findings, if any.</summary>
    public string? Biochemistry { get; init; } = Biochemistry;

    /// <summary>Age in years. Three of the four supported equations are stratified by it.</summary>
    public int AgeYears { get; init; } = AgeYears;

    /// <summary>Female or Male. Read by the equations, not by anything else.</summary>
    public string BiologicalSex { get; init; } = BiologicalSex;

    /// <summary>
    ///     The assessment this one corrects, if any. A closed assessment is immutable, so a
    ///     correction creates a new one that points back at it.
    /// </summary>
    public int? SupersedesAssessmentId { get; init; } = SupersedesAssessmentId;

    /// <summary>
    ///     Optional (NC-3). Physical activity from the closed list: Sedentary, Light, Moderate, Intense.
    ///     The free text <c>physicalActivity</c> stays required on this endpoint.
    /// </summary>
    public string? ActivityLevel { get; init; } = ActivityLevel;

    /// <summary>Optional (NC-3). Structured eating habits.</summary>
    public EatingHabitsResource? EatingHabits { get; init; } = EatingHabits;

    /// <summary>Optional (NC-3). Structured biochemistry, in mg/dL.</summary>
    public BiochemistryPanelResource? BiochemistryPanel { get; init; } = BiochemistryPanel;
}

/// <summary>NC-3. Eating habits of EV-2. Every value is optional.</summary>
public record EatingHabitsResource(int? MealsPerDay, decimal? WaterLitersPerDay, int? MealsOutPerWeek)
{
    /// <summary>Meals per day, 1 to 10.</summary>
    public int? MealsPerDay { get; init; } = MealsPerDay;

    /// <summary>Water per day in litres, 0 to 10.</summary>
    public decimal? WaterLitersPerDay { get; init; } = WaterLitersPerDay;

    /// <summary>Meals eaten out per week, 0 to 21.</summary>
    public int? MealsOutPerWeek { get; init; } = MealsOutPerWeek;
}

/// <summary>NC-3. Biochemistry of EV-2, in mg/dL. Every value is optional.</summary>
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

/// <summary>Payload of Subflow 3.1 - Take Clinical Measurement.</summary>
public record TakeClinicalMeasurementResource(
    decimal WeightKg,
    decimal HeightCm,
    string Protocol,
    decimal? BodyFatPercentage,
    decimal? WaistCircumferenceCm,
    IReadOnlyList<string>? ProtocolChecks = null)
{
    /// <summary>Measured weight in kilograms.</summary>
    public decimal WeightKg { get; init; } = WeightKg;

    /// <summary>Measured height in centimetres.</summary>
    public decimal HeightCm { get; init; } = HeightCm;

    /// <summary>
    ///     How the measurement was taken. Required: recording the protocol is what gives a clinical
    ///     measurement its authority over a self weigh-in.
    /// </summary>
    public string Protocol { get; init; } = Protocol;

    /// <summary>Body fat percentage. Required only by the Katch-McArdle equation.</summary>
    public decimal? BodyFatPercentage { get; init; } = BodyFatPercentage;

    /// <summary>Waist circumference in centimetres, if measured.</summary>
    public decimal? WaistCircumferenceCm { get; init; } = WaistCircumferenceCm;

    /// <summary>
    ///     Optional (NC-3). The EV-2 protocol checklist: Fasting, NoShoes, LightClothing, EmptyBladder,
    ///     SameScale. When sent, at least one. The free text <c>protocol</c> stays required here.
    /// </summary>
    public IReadOnlyList<string>? ProtocolChecks { get; init; } = ProtocolChecks;
}

/// <summary>Payload of Subflow 3.2 - Issue Diagnosis.</summary>
public record IssueDiagnosisResource(
    int PatientId,
    int AssessmentId,
    string? Statement,
    string? Rationale,
    string? Code = null)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The closed assessment this diagnosis reads.</summary>
    public int AssessmentId { get; init; } = AssessmentId;

    /// <summary>The diagnosis itself, as free text. Optional when <see cref="Code" /> is given.</summary>
    public string? Statement { get; init; } = Statement;

    /// <summary>The reasoning behind it. Required without a code; with one, it is written from the measurement when empty.</summary>
    public string? Rationale { get; init; } = Rationale;

    /// <summary>
    ///     NC-4. Closed list code: Underweight, NormalWeight, OverweightGradeI, ObesityGradeI, ObesityGradeII
    ///     or ObesityGradeIII. Recorded as chosen by the practitioner.
    /// </summary>
    public string? Code { get; init; } = Code;
}

/// <summary>
///     Payload of Subflow 3.3 - Propose Targets. These are the five decisions the system never makes.
/// </summary>
public record ProposeTargetsResource(
    int PatientId,
    string Equation,
    string ReferenceWeightKind,
    decimal ReferenceWeightKg,
    decimal ActivityFactor,
    string DeficitKind,
    decimal DeficitValue,
    decimal ProteinGramsPerKg,
    decimal FatPercentOfEnergy)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>MifflinStJeor, HarrisBenedict, FaoWhoUnu or KatchMcArdle.</summary>
    public string Equation { get; init; } = Equation;

    /// <summary>Actual, Ideal or Adjusted. Clinically different answers.</summary>
    public string ReferenceWeightKind { get; init; } = ReferenceWeightKind;

    /// <summary>The weight the calculation runs on, in kilograms.</summary>
    public decimal ReferenceWeightKg { get; init; } = ReferenceWeightKg;

    /// <summary>
    ///     Between 1.0 and 2.5. The single largest source of error in the whole calculation, and it
    ///     comes out of a conversation rather than a sensor.
    /// </summary>
    public decimal ActivityFactor { get; init; } = ActivityFactor;

    /// <summary>FixedKcal or PercentOfTdee.</summary>
    public string DeficitKind { get; init; } = DeficitKind;

    /// <summary>Kilocalories when the kind is FixedKcal, percentage points when it is PercentOfTdee.</summary>
    public decimal DeficitValue { get; init; } = DeficitValue;

    /// <summary>Protein target in grams per kilogram of the reference weight.</summary>
    public decimal ProteinGramsPerKg { get; init; } = ProteinGramsPerKg;

    /// <summary>Share of the target energy that comes from fat, in percent. Carbohydrate is the rest.</summary>
    public decimal FatPercentOfEnergy { get; init; } = FatPercentOfEnergy;
}

/// <summary>Payload of Subflow 3.4 - Prescribe Targets.</summary>
public record PrescribeTargetsResource(
    string Outcome,
    decimal? EnergyKcal,
    decimal? ProteinG,
    decimal? CarbG,
    decimal? FatG,
    string? OverrideReason)
{
    /// <summary>AcceptedAsProposed or Overridden.</summary>
    public string Outcome { get; init; } = Outcome;

    /// <summary>Energy target when overriding. Ignored when accepting the proposal.</summary>
    public decimal? EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Protein target when overriding.</summary>
    public decimal? ProteinG { get; init; } = ProteinG;

    /// <summary>Carbohydrate target when overriding.</summary>
    public decimal? CarbG { get; init; } = CarbG;

    /// <summary>Fat target when overriding.</summary>
    public decimal? FatG { get; init; } = FatG;

    /// <summary>Why the calculated numbers were replaced. Required whenever the outcome is Overridden.</summary>
    public string? OverrideReason { get; init; } = OverrideReason;
}

/// <summary>Payload of Subflow 3.5 - Publish Nutrition Plan.</summary>
public record PublishNutritionPlanResource(
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<string>? CustomGuidelines = null,
    string? PatientMessage = null)
{
    /// <summary>
    ///     Guidance for the patient: catalog codes (PrioritizeVegetables, Drink2LWater, AvoidSugaryDrinks,
    ///     ProteinAtBreakfast, ReduceSalt, EatEvery3To4Hours, ProteinAndVegetablesAtDinner). Any other text is
    ///     kept as a custom guideline (compatibility; new clients use <see cref="CustomGuidelines" />). Menus and
    ///     food equivalences are not generated: that would replace clinical judgement, which is out of scope.
    /// </summary>
    public IReadOnlyList<string> Guidelines { get; init; } = Guidelines;

    /// <summary>
    ///     What the patient should avoid, as codes (NC-6): LactoseFree, GlutenFree, Vegan, Vegetarian,
    ///     TreeNutFree, ShellfishFree, Kosher, Halal. An unknown code is rejected.
    /// </summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>NC-6. "Otra indicación": 3 to 140 characters each, at most 5 per version.</summary>
    public IReadOnlyList<string>? CustomGuidelines { get; init; } = CustomGuidelines;

    /// <summary>
    ///     NC-9. Optional message for the patient with this version (PT4), at most 500 characters. Never the diagnosis.
    /// </summary>
    public string? PatientMessage { get; init; } = PatientMessage;
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
    ///     Restrictions for the new version, as codes (NC-6). Legacy restrictions of the current version that
    ///     are not mapped to a code here are left out of the new one and recorded on it.
    /// </summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>Why this version exists. Required for every new version, without exception.</summary>
    public string ChangeReason { get; init; } = ChangeReason;

    /// <summary>NC-6. "Otra indicación": 3 to 140 characters each, at most 5 per version.</summary>
    public IReadOnlyList<string>? CustomGuidelines { get; init; } = CustomGuidelines;

    /// <summary>
    ///     NC-9. Optional message for the patient with the new version (PT4), at most 500 characters. Never the
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

/// <summary>Payload of NC-1 - Record the patient baseline (EV-1).</summary>
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

/// <summary>Payload of NC-1 - Edit the patient baseline.</summary>
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
