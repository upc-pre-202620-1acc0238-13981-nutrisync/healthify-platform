namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;

/// <summary>One anthropometric reading. Read model: Anthropometry Series.</summary>
public record ClinicalMeasurementResource(
    int MeasurementId,
    decimal WeightKg,
    decimal HeightCm,
    string Protocol,
    decimal? BodyFatPercentage,
    decimal? WaistCircumferenceCm,
    DateTimeOffset TakenAt,
    decimal Bmi,
    string BmiCategory,
    IReadOnlyList<string>? ProtocolChecks)
{
    /// <summary>Identifier of the measurement.</summary>
    public int MeasurementId { get; init; } = MeasurementId;

    /// <summary>Weight in kilograms.</summary>
    public decimal WeightKg { get; init; } = WeightKg;

    /// <summary>Height in centimetres.</summary>
    public decimal HeightCm { get; init; } = HeightCm;

    /// <summary>How it was taken. Always shown: it is what makes the reading clinical.</summary>
    public string Protocol { get; init; } = Protocol;

    /// <summary>Body fat percentage, if measured.</summary>
    public decimal? BodyFatPercentage { get; init; } = BodyFatPercentage;

    /// <summary>Waist circumference in centimetres, if measured.</summary>
    public decimal? WaistCircumferenceCm { get; init; } = WaistCircumferenceCm;

    /// <summary>When it was taken.</summary>
    public DateTimeOffset TakenAt { get; init; } = TakenAt;

    /// <summary>Body mass index in kg/m², one decimal, with the height of that day.</summary>
    public decimal Bmi { get; init; } = Bmi;

    /// <summary>
    ///     WHO category: Underweight, NormalWeight, OverweightGradeI, ObesityGradeI, ObesityGradeII,
    ///     ObesityGradeIII. Professional information only.
    /// </summary>
    public string BmiCategory { get; init; } = BmiCategory;

    /// <summary>The protocol checklist of step 1, or null when only the free text protocol was recorded.</summary>
    public IReadOnlyList<string>? ProtocolChecks { get; init; } = ProtocolChecks;
}

/// <summary>Read models: Assessment Form, Anthropometry Series, Assessment Timeline.</summary>
public record NutritionalAssessmentResource(
    int AssessmentId,
    int PatientId,
    int PractitionerId,
    string? Habits,
    string? MedicalHistory,
    string? PhysicalActivity,
    string? Biochemistry,
    int AgeYears,
    string BiologicalSex,
    int? SupersedesAssessmentId,
    bool IsClosed,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<ClinicalMeasurementResource> Anthropometry,
    string? ActivityLevel,
    EatingHabitsResource? EatingHabits,
    BiochemistryPanelResource? BiochemistryPanel,
    IReadOnlyList<string>? ConditionsSnapshot,
    int? ConsultationId)
{
    /// <summary>Identifier of the assessment.</summary>
    public int AssessmentId { get; init; } = AssessmentId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Identifier of the practitioner who recorded it.</summary>
    public int PractitionerId { get; init; } = PractitionerId;

    /// <summary>Eating habits as free text. Null on assessments recorded in a guided consultation.</summary>
    public string? Habits { get; init; } = Habits;

    /// <summary>
    ///     Relevant medical history as free text. Null on assessments recorded in a guided consultation,
    ///     which carry <c>conditionsSnapshot</c> instead.
    /// </summary>
    public string? MedicalHistory { get; init; } = MedicalHistory;

    /// <summary>Physical activity as free text. Null on assessments recorded in a guided consultation.</summary>
    public string? PhysicalActivity { get; init; } = PhysicalActivity;

    /// <summary>Laboratory findings, if any.</summary>
    public string? Biochemistry { get; init; } = Biochemistry;

    /// <summary>Age in years.</summary>
    public int AgeYears { get; init; } = AgeYears;

    /// <summary>Female or Male.</summary>
    public string BiologicalSex { get; init; } = BiologicalSex;

    /// <summary>The assessment this one corrects, if any.</summary>
    public int? SupersedesAssessmentId { get; init; } = SupersedesAssessmentId;

    /// <summary>Once closed, the assessment is immutable.</summary>
    public bool IsClosed { get; init; } = IsClosed;

    /// <summary>When it was closed, or null.</summary>
    public DateTimeOffset? ClosedAt { get; init; } = ClosedAt;

    /// <summary>The clinical measurements taken during this assessment.</summary>
    public IReadOnlyList<ClinicalMeasurementResource> Anthropometry { get; init; } = Anthropometry;

    /// <summary>Sedentary, Light, Moderate or Intense, if recorded.</summary>
    public string? ActivityLevel { get; init; } = ActivityLevel;

    /// <summary>Structured eating habits, if recorded.</summary>
    public EatingHabitsResource? EatingHabits { get; init; } = EatingHabits;

    /// <summary>Structured biochemistry in mg/dL, if recorded.</summary>
    public BiochemistryPanelResource? BiochemistryPanel { get; init; } = BiochemistryPanel;

    /// <summary>
    ///     The baseline medical history as it was when this assessment was recorded. Null when the
    ///     assessment predates the baseline; empty when it listed none.
    /// </summary>
    public IReadOnlyList<string>? ConditionsSnapshot { get; init; } = ConditionsSnapshot;

    /// <summary>The guided consultation this assessment belongs to, if any.</summary>
    public int? ConsultationId { get; init; } = ConsultationId;
}

/// <summary>Read model: Active Diagnosis. Never leaves this bounded context.</summary>
public record NutritionalDiagnosisResource(
    int DiagnosisId,
    int PatientId,
    int AssessmentId,
    string Statement,
    string Rationale,
    DateTimeOffset IssuedAt,
    bool IsActive,
    string? Code = null,
    string? Source = null,
    decimal? BmiAtIssue = null)
{
    /// <summary>Identifier of the diagnosis.</summary>
    public int DiagnosisId { get; init; } = DiagnosisId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The closed assessment it reads.</summary>
    public int AssessmentId { get; init; } = AssessmentId;

    /// <summary>The diagnosis itself.</summary>
    public string Statement { get; init; } = Statement;

    /// <summary>The reasoning behind it.</summary>
    public string Rationale { get; init; } = Rationale;

    /// <summary>When it was issued.</summary>
    public DateTimeOffset IssuedAt { get; init; } = IssuedAt;

    /// <summary>Whether it is the one currently grounding the plan.</summary>
    public bool IsActive { get; init; } = IsActive;

    /// <summary>Closed list code, or null on historic free text diagnoses.</summary>
    public string? Code { get; init; } = Code;

    /// <summary>AiSuggestionAccepted or PractitionerSelected; null on historic rows.</summary>
    public string? Source { get; init; } = Source;

    /// <summary>Body mass index of the measurement it read, kg/m². Practitioner only.</summary>
    public decimal? BmiAtIssue { get; init; } = BmiAtIssue;
}

/// <summary>The calculation behind a proposal. Read model: Target Proposal View, practitioner only.</summary>
public record CalculationBasisResource(
    string Equation,
    string ReferenceWeightKind,
    decimal ReferenceWeightKg,
    decimal ActivityFactor,
    string DeficitKind,
    decimal DeficitValue,
    decimal ComputedBmr,
    decimal ComputedTdee)
{
    /// <summary>The published equation that produced the basal metabolic rate.</summary>
    public string Equation { get; init; } = Equation;

    /// <summary>Actual, Ideal or Adjusted.</summary>
    public string ReferenceWeightKind { get; init; } = ReferenceWeightKind;

    /// <summary>The weight the calculation ran on.</summary>
    public decimal ReferenceWeightKg { get; init; } = ReferenceWeightKg;

    /// <summary>The activity factor the practitioner assigned.</summary>
    public decimal ActivityFactor { get; init; } = ActivityFactor;

    /// <summary>FixedKcal or PercentOfTdee.</summary>
    public string DeficitKind { get; init; } = DeficitKind;

    /// <summary>The size of the deficit, in the unit its kind implies.</summary>
    public decimal DeficitValue { get; init; } = DeficitValue;

    /// <summary>Basal metabolic rate, recomputable by hand from the equation and the inputs.</summary>
    public decimal ComputedBmr { get; init; } = ComputedBmr;

    /// <summary>Total daily energy expenditure: the rate times the activity factor.</summary>
    public decimal ComputedTdee { get; init; } = ComputedTdee;
}

/// <summary>Four daily numbers.</summary>
public record TargetsResource(decimal EnergyKcal, decimal ProteinG, decimal CarbG, decimal FatG)
{
    /// <summary>Daily energy target in kilocalories.</summary>
    public decimal EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Daily protein target in grams.</summary>
    public decimal ProteinG { get; init; } = ProteinG;

    /// <summary>Daily carbohydrate target in grams.</summary>
    public decimal CarbG { get; init; } = CarbG;

    /// <summary>Daily fat target in grams.</summary>
    public decimal FatG { get; init; } = FatG;
}

/// <summary>
///     Read models: Target Proposal View, Prescribed Targets, Active Plan, Plan Version History,
///     Published Contract. Practitioner only: the calculation basis is on it.
/// </summary>
public record NutritionPlanResource(
    int PlanId,
    int PatientId,
    int PractitionerId,
    int DiagnosisId,
    int Version,
    CalculationBasisResource CalculationBasis,
    TargetsResource Proposal,
    TargetsResource? Prescribed,
    string? PrescriptionOutcome,
    string? OverrideReason,
    string? ChangeReason,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    bool IsActive,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? SupersededAt,
    IReadOnlyList<GuidelineItemResource>? GuidelineItems = null,
    IReadOnlyList<string>? LegacyRestrictions = null,
    IReadOnlyList<string>? DroppedLegacyRestrictions = null,
    string? PatientMessage = null,
    string? ChangeReasonCode = null,
    ChangeReasonDataResource? ChangeReasonData = null)
{
    /// <summary>Identifier of this plan version.</summary>
    public int PlanId { get; init; } = PlanId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Identifier of the prescribing practitioner.</summary>
    public int PractitionerId { get; init; } = PractitionerId;

    /// <summary>The diagnosis that grounds this plan. No plan exists without one.</summary>
    public int DiagnosisId { get; init; } = DiagnosisId;

    /// <summary>Version number. Superseded versions are kept, never deleted.</summary>
    public int Version { get; init; } = Version;

    /// <summary>What the calculation ran on. Never crosses to the patient.</summary>
    public CalculationBasisResource CalculationBasis { get; init; } = CalculationBasis;

    /// <summary>What the arithmetic produced.</summary>
    public TargetsResource Proposal { get; init; } = Proposal;

    /// <summary>What the practitioner signed, or null while nothing has been prescribed yet.</summary>
    public TargetsResource? Prescribed { get; init; } = Prescribed;

    /// <summary>AcceptedAsProposed or Overridden, or null before prescription.</summary>
    public string? PrescriptionOutcome { get; init; } = PrescriptionOutcome;

    /// <summary>Why the numbers were replaced. Present whenever the outcome is Overridden.</summary>
    public string? OverrideReason { get; init; } = OverrideReason;

    /// <summary>Why this version exists. Present on every version after the first.</summary>
    public string? ChangeReason { get; init; } = ChangeReason;

    /// <summary>Guidance: a catalog code or the custom text of each guideline (see <see cref="GuidelineItems" />).</summary>
    public IReadOnlyList<string> Guidelines { get; init; } = Guidelines;

    /// <summary>What the patient should avoid: DietaryRestriction codes.</summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>Whether this is the one active version for the patient.</summary>
    public bool IsActive { get; init; } = IsActive;

    /// <summary>When it was published, or null.</summary>
    public DateTimeOffset? PublishedAt { get; init; } = PublishedAt;

    /// <summary>When a newer version replaced it, or null.</summary>
    public DateTimeOffset? SupersededAt { get; init; } = SupersededAt;

    /// <summary>The guidelines, each a catalog code or a custom text.</summary>
    public IReadOnlyList<GuidelineItemResource> GuidelineItems { get; init; } = GuidelineItems ?? [];

    /// <summary>
    ///     Free text restrictions from before the closed list that match no code. Map them to a
    ///     code in the next version; whatever is not mapped is left out of it.
    /// </summary>
    public IReadOnlyList<string> LegacyRestrictions { get; init; } = LegacyRestrictions ?? [];

    /// <summary>Legacy restrictions of the previous version that this version left out.</summary>
    public IReadOnlyList<string> DroppedLegacyRestrictions { get; init; } = DroppedLegacyRestrictions ?? [];

    /// <summary>The message the patient received with this version, or null.</summary>
    public string? PatientMessage { get; init; } = PatientMessage;

    /// <summary>
    ///     Custom (the practitioner wrote <c>ChangeReason</c>: show it as written), NewConsultation or
    ///     SignalAdjustment (word it in the reader's language from <c>ChangeReasonData</c>; <c>ChangeReason</c> is the
    ///     Spanish fallback). Null on a first version, which has no reason.
    /// </summary>
    public string? ChangeReasonCode { get; init; } = ChangeReasonCode;

    /// <summary>Parameters of <c>ChangeReasonCode</c>; null for Custom and on a first version.</summary>
    public ChangeReasonDataResource? ChangeReasonData { get; init; } = ChangeReasonData;
}

/// <summary>
///     Parameters of a plan change reason code: <c>{ date: "2026-09-18" }</c> for NewConsultation ("New
///     consultation on Sep 18, 2026"), <c>{ date, signalType: "SustainedDeviation" }</c> for
///     SignalAdjustment ("Signal adjustment: sustained deviation on Sep 8, 2026").
/// </summary>
public record ChangeReasonDataResource(DateOnly? Date, string? SignalType)
{
    /// <summary>NewConsultation: the clinical day of the consultation. SignalAdjustment: the day of the signal.</summary>
    public DateOnly? Date { get; init; } = Date;

    /// <summary>SignalAdjustment: the signal type (SustainedDeviation).</summary>
    public string? SignalType { get; init; } = SignalType;
}

/// <summary>One guideline of a plan version: a catalog code or a custom text, never both.</summary>
public record GuidelineItemResource(string? Code, string? Custom)
{
    /// <summary>Catalog code (the client translates it), or null for a custom guideline.</summary>
    public string? Code { get; init; } = Code;

    /// <summary>The custom guideline as written. Clinical data: never translated.</summary>
    public string? Custom { get; init; } = Custom;
}

/// <summary>Read model: Practitioner Review Inbox.</summary>
public record ReviewItemResource(
    int ReviewItemId,
    int PatientId,
    int PractitionerId,
    string SignalType,
    string Evidence,
    string State,
    bool? ResolvedWithAdjustment,
    string? ResolutionNote,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? CreatedAt,
    string? PatientFullName = null,
    bool HasPlanProposal = false,
    ReviewItemEvidenceResource? EvidenceData = null,
    string? ResolutionNoteCode = null,
    ResolutionNoteDataResource? ResolutionNoteData = null)
{
    /// <summary>Identifier of the review item.</summary>
    public int ReviewItemId { get; init; } = ReviewItemId;

    /// <summary>Identifier of the patient the signal is about.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Identifier of the practitioner whose inbox it landed in.</summary>
    public int PractitionerId { get; init; } = PractitionerId;

    /// <summary>SustainedDeviation or ConsistencyEscalation.</summary>
    public string SignalType { get; init; } = SignalType;

    /// <summary>What Monitoring observed. Evidence, not a verdict.</summary>
    public string Evidence { get; init; } = Evidence;

    /// <summary>Open or Resolved. A signal never resolves itself.</summary>
    public string State { get; init; } = State;

    /// <summary>Whether the plan was adjusted because of this signal.</summary>
    public bool? ResolvedWithAdjustment { get; init; } = ResolvedWithAdjustment;

    /// <summary>
    ///     The note: the practitioner's text when <c>ResolutionNoteCode</c> is Custom, the Spanish fallback of a system
    ///     code otherwise, or null.
    /// </summary>
    public string? ResolutionNote { get; init; } = ResolutionNote;

    /// <summary>When it was resolved, or null.</summary>
    public DateTimeOffset? ResolvedAt { get; init; } = ResolvedAt;

    /// <summary>When the signal arrived.</summary>
    public DateTimeOffset? CreatedAt { get; init; } = CreatedAt;

    /// <summary>"Ana Flores", or null when the name could not be read.</summary>
    public string? PatientFullName { get; init; } = PatientFullName;

    /// <summary>Whether an AI plan proposal is attached: the client opens the proposal instead of the plain item.</summary>
    public bool HasPlanProposal { get; init; } = HasPlanProposal;

    /// <summary>
    ///     The evidence as numbers, for the client to word in its language. Null for items opened before numeric
    ///     evidence existed (and, for a consistency escalation, before localized evidence). <c>Evidence</c> keeps the original English sentence as the
    ///     legacy fallback.
    /// </summary>
    public ReviewItemEvidenceResource? EvidenceData { get; init; } = EvidenceData;

    /// <summary>
    ///     Custom (show <c>ResolutionNote</c> as written), PlanAssignedAsIs or PlanAssignedWithEdits (word it from
    ///     <c>ResolutionNoteData.PlanVersion</c>), ProposalDiscarded (no data). Null while open, or when resolved without
    ///     a note and without a proposal.
    /// </summary>
    public string? ResolutionNoteCode { get; init; } = ResolutionNoteCode;

    /// <summary>Parameters of <c>ResolutionNoteCode</c>, or null.</summary>
    public ResolutionNoteDataResource? ResolutionNoteData { get; init; } = ResolutionNoteData;
}

/// <summary>
///     Parameters of a resolution note code: <c>{ planVersion: 2 }</c> for PlanAssignedAsIs ("Plan v2
///     assigned (AI proposal accepted as is)") and PlanAssignedWithEdits.
/// </summary>
public record ResolutionNoteDataResource(int? PlanVersion)
{
    /// <summary>The plan version assigned from the proposal.</summary>
    public int? PlanVersion { get; init; } = PlanVersion;
}

/// <summary>
///     Structured evidence of a review item: <c>{ averagePercentFromTarget: -40, deviatedDays: 5,
///     loggedDaysConsidered: 9, direction: "Below" }</c>. Days without a record are not counted.
/// </summary>
public record ReviewItemEvidenceResource(
    decimal? AveragePercentFromTarget,
    int? DeviatedDays,
    int? LoggedDaysConsidered,
    string? Direction,
    DateOnly? AdjustedOn,
    int? AdjustedPlanVersion,
    decimal? AverageEnergyKcalFromTarget = null,
    decimal? ConsistencyKgPerWeek = null,
    string? ConsistencyState = null,
    DateOnly? AlertSinceOn = null,
    int? WeeksInAlert = null,
    DateOnly? ShownToPatientOn = null)
{
    /// <summary>Mean distance from the energy target, signed: -40 is 40 % below it.</summary>
    public decimal? AveragePercentFromTarget { get; init; } = AveragePercentFromTarget;

    /// <summary>Logged days that deviated in the direction.</summary>
    public int? DeviatedDays { get; init; } = DeviatedDays;

    /// <summary>Logged days read ("of their last 9 logged days"). Unlogged days are not counted.</summary>
    public int? LoggedDaysConsidered { get; init; } = LoggedDaysConsidered;

    /// <summary>Above or Below the target.</summary>
    public string? Direction { get; init; } = Direction;

    /// <summary>Scheduled recheck: the day the plan was adjusted ("after the adjustment of Sep 8").</summary>
    public DateOnly? AdjustedOn { get; init; } = AdjustedOn;

    /// <summary>Scheduled recheck: the version assigned that day.</summary>
    public int? AdjustedPlanVersion { get; init; } = AdjustedPlanVersion;

    /// <summary>Sustained deviation: kcal per day from the target, signed like the percent (-220 = 220 kcal below).</summary>
    public decimal? AverageEnergyKcalFromTarget { get; init; } = AverageEnergyKcalFromTarget;

    /// <summary>Consistency escalation: unexplained weight movement, kg per week.</summary>
    public decimal? ConsistencyKgPerWeek { get; init; } = ConsistencyKgPerWeek;

    /// <summary>Consistency escalation: the state of the index (Alert).</summary>
    public string? ConsistencyState { get; init; } = ConsistencyState;

    /// <summary>Consistency escalation: the day the alert began.</summary>
    public DateOnly? AlertSinceOn { get; init; } = AlertSinceOn;

    /// <summary>Consistency escalation: whole weeks in alert when it was escalated.</summary>
    public int? WeeksInAlert { get; init; } = WeeksInAlert;

    /// <summary>Consistency escalation: the day the patient acknowledged the prompt.</summary>
    public DateOnly? ShownToPatientOn { get; init; } = ShownToPatientOn;
}

/// <summary>Read model Patient Baseline. Practitioner only.</summary>
public record PatientBaselineResource(
    int PatientId,
    DateOnly BirthDate,
    int AgeYears,
    string BiologicalSex,
    decimal HeightCm,
    IReadOnlyList<string> Conditions,
    DateTimeOffset? UpdatedAt,
    bool BirthDateEstimated)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Birth date.</summary>
    public DateOnly BirthDate { get; init; } = BirthDate;

    /// <summary>Completed years today. Derived on every read, so it updates itself.</summary>
    public int AgeYears { get; init; } = AgeYears;

    /// <summary>Female or Male.</summary>
    public string BiologicalSex { get; init; } = BiologicalSex;

    /// <summary>Height in centimetres.</summary>
    public decimal HeightCm { get; init; } = HeightCm;

    /// <summary>Medical history codes from the closed list. Empty means none.</summary>
    public IReadOnlyList<string> Conditions { get; init; } = Conditions;

    /// <summary>When the baseline was last saved.</summary>
    public DateTimeOffset? UpdatedAt { get; init; } = UpdatedAt;

    /// <summary>
    ///     True when the birth date was estimated from the age stored on an older assessment. The
    ///     first edit by a practitioner confirms it.
    /// </summary>
    public bool BirthDateEstimated { get; init; } = BirthDateEstimated;
}
