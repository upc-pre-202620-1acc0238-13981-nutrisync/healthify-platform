namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;

// ---------------------------------------------------------------------------------------------
// NC-2. Payloads of the guided consultation (EV-2 to EV-5)
// ---------------------------------------------------------------------------------------------

/// <summary>Payload of PAC-1 "Iniciar consulta". The body may be empty.</summary>
public record StartConsultationResource(int? ScheduledFollowUpId)
{
    /// <summary>The appointment of the agenda the consultation starts from, if any (MA-2).</summary>
    public int? ScheduledFollowUpId { get; init; } = ScheduledFollowUpId;
}

/// <summary>Payload of step 1, EV-2 "Medición de hoy". The height comes from the baseline.</summary>
public record RecordConsultationMeasurementResource(
    decimal WeightKg,
    decimal? WaistCm,
    decimal? BodyFatPercentage,
    IReadOnlyList<string>? ProtocolChecks,
    string? ActivityLevel,
    EatingHabitsResource? Habits,
    BiochemistryPanelResource? Biochemistry)
{
    /// <summary>Weight in kilograms, 20 to 350.</summary>
    public decimal WeightKg { get; init; } = WeightKg;

    /// <summary>Waist circumference in centimetres, 40 to 200. Optional.</summary>
    public decimal? WaistCm { get; init; } = WaistCm;

    /// <summary>Body fat percentage, 3 to 70. Optional.</summary>
    public decimal? BodyFatPercentage { get; init; } = BodyFatPercentage;

    /// <summary>"Protocolo utilizado": Fasting, NoShoes, LightClothing, EmptyBladder, SameScale. At least one.</summary>
    public IReadOnlyList<string>? ProtocolChecks { get; init; } = ProtocolChecks;

    /// <summary>"Actividad física actual": Sedentary, Light, Moderate or Intense.</summary>
    public string? ActivityLevel { get; init; } = ActivityLevel;

    /// <summary>OPTIONAL "Hábitos alimentarios".</summary>
    public EatingHabitsResource? Habits { get; init; } = Habits;

    /// <summary>OPTIONAL "Datos bioquímicos".</summary>
    public BiochemistryPanelResource? Biochemistry { get; init; } = Biochemistry;
}

/// <summary>Payload of step 2, EV-3: "Usar sugerencia" or "Elegir otro".</summary>
public record IssueConsultationDiagnosisResource(
    string Code,
    string Source,
    long? AiGenerationId,
    string? Rationale)
{
    /// <summary>
    ///     Underweight, NormalWeight, OverweightGradeI, ObesityGradeI, ObesityGradeII or ObesityGradeIII.
    /// </summary>
    public string Code { get; init; } = Code;

    /// <summary>AiSuggestionAccepted or PractitionerSelected.</summary>
    public string Source { get; init; } = Source;

    /// <summary>The AI generation whose suggestion was accepted. Required with AiSuggestionAccepted.</summary>
    public long? AiGenerationId { get; init; } = AiGenerationId;

    /// <summary>
    ///     The rationale. Required with AiSuggestionAccepted; when the practitioner picks the code without one, it
    ///     is written from the measurement of step 1.
    /// </summary>
    public string? Rationale { get; init; } = Rationale;
}

/// <summary>The parameters "Cambiar parámetros" changes in EV-4. Each one is optional: the rest keep their default.</summary>
public record ConsultationTargetParametersResource(
    string? Equation,
    string? ReferenceWeightKind,
    decimal? ReferenceWeightKg,
    decimal? ActivityFactor,
    string? DeficitKind,
    decimal? DeficitValue,
    decimal? ProteinGramsPerKg,
    decimal? FatPercentOfEnergy)
{
    /// <summary>MifflinStJeor, HarrisBenedict, FaoWhoUnu or KatchMcArdle.</summary>
    public string? Equation { get; init; } = Equation;

    /// <summary>Actual, Ideal or Adjusted.</summary>
    public string? ReferenceWeightKind { get; init; } = ReferenceWeightKind;

    /// <summary>The weight the calculation runs on, in kilograms. Required for Ideal and Adjusted.</summary>
    public decimal? ReferenceWeightKg { get; init; } = ReferenceWeightKg;

    /// <summary>Between 1.0 and 2.5. By default, the factor of the activity level of step 1.</summary>
    public decimal? ActivityFactor { get; init; } = ActivityFactor;

    /// <summary>FixedKcal or PercentOfTdee.</summary>
    public string? DeficitKind { get; init; } = DeficitKind;

    /// <summary>Kilocalories (FixedKcal) or percentage points (PercentOfTdee).</summary>
    public decimal? DeficitValue { get; init; } = DeficitValue;

    /// <summary>Protein in grams per kilogram of the reference weight.</summary>
    public decimal? ProteinGramsPerKg { get; init; } = ProteinGramsPerKg;

    /// <summary>Share of the energy from fat, in percent.</summary>
    public decimal? FatPercentOfEnergy { get; init; } = FatPercentOfEnergy;
}

/// <summary>Payload of the target proposal of step 3. An empty body uses the defaults.</summary>
public record ProposeConsultationTargetsResource(ConsultationTargetParametersResource? Parameters)
{
    /// <summary>"Cambiar parámetros"; null keeps every default.</summary>
    public ConsultationTargetParametersResource? Parameters { get; init; } = Parameters;
}

/// <summary>Payload of step 3, EV-4: "Aceptar metas" or "Escribir mis propios valores".</summary>
public record PrescribeConsultationTargetsResource(
    string Outcome,
    decimal? EnergyKcal,
    decimal? ProteinG,
    decimal? CarbG,
    decimal? FatG,
    string? OverrideReason)
{
    /// <summary>AcceptedAsProposed or Overridden.</summary>
    public string Outcome { get; init; } = Outcome;

    /// <summary>Energy target when overriding.</summary>
    public decimal? EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Protein target when overriding.</summary>
    public decimal? ProteinG { get; init; } = ProteinG;

    /// <summary>Carbohydrate target when overriding.</summary>
    public decimal? CarbG { get; init; } = CarbG;

    /// <summary>Fat target when overriding.</summary>
    public decimal? FatG { get; init; } = FatG;

    /// <summary>Why the calculated numbers were replaced. Required when overriding.</summary>
    public string? OverrideReason { get; init; } = OverrideReason;
}

/// <summary>Payload of step 4, EV-5: the publication, and the draft saved before it.</summary>
public record ConsultationPublicationResource(
    IReadOnlyList<string>? Restrictions,
    IReadOnlyList<string>? Guidelines,
    IReadOnlyList<string>? CustomGuidelines,
    string? PatientMessage = null)
{
    /// <summary>
    ///     LactoseFree, GlutenFree, Vegan, Vegetarian, TreeNutFree, ShellfishFree, Kosher, Halal. An unknown code
    ///     is rejected.
    /// </summary>
    public IReadOnlyList<string>? Restrictions { get; init; } = Restrictions;

    /// <summary>
    ///     Catalog codes only: PrioritizeVegetables, Drink2LWater, AvoidSugaryDrinks, ProteinAtBreakfast, ReduceSalt,
    ///     EatEvery3To4Hours, ProteinAndVegetablesAtDinner.
    /// </summary>
    public IReadOnlyList<string>? Guidelines { get; init; } = Guidelines;

    /// <summary>"Otra indicación": 3 to 140 characters each, at most 5.</summary>
    public IReadOnlyList<string>? CustomGuidelines { get; init; } = CustomGuidelines;

    /// <summary>
    ///     NC-9. Optional message for the patient with this version (PT4), at most 500 characters. Never the
    ///     diagnosis. The publication draft keeps it too, and EV-5 reads it back from <c>publicationDraft</c>.
    /// </summary>
    public string? PatientMessage { get; init; } = PatientMessage;
}

// ---------------------------------------------------------------------------------------------
// NC-2. Read resources of the guided consultation. Practitioner only.
// ---------------------------------------------------------------------------------------------

/// <summary>MA-4. One question of the check in, with where it came from.</summary>
/// <param name="Text">The question.</param>
/// <param name="Origin">Patient or AiSuggested.</param>
/// <param name="Language">X-2. es or en for an AI suggestion; null for the patient's own questions.</param>
public record ConsultationCheckInQuestionResource(string Text, string Origin, string? Language = null)
{
    /// <summary>"Pregunta: ¿Puedo comer fuera los viernes?".</summary>
    public string Text { get; init; } = Text;

    /// <summary>Patient or AiSuggested.</summary>
    public string Origin { get; init; } = Origin;

    /// <summary>X-2. es or en: the language an AI suggestion was generated in. Null for the patient's own questions.</summary>
    public string? Language { get; init; } = Language;
}

/// <summary>
///     MA-4. EV-2 "Antes de la consulta, Ana contó: Se sintió regular con el plan. Le costaron las cenas…". Same
///     shape as the check in resource of Monitoring, which owns it.
/// </summary>
/// <param name="FollowUpId">The visit it was sent for.</param>
/// <param name="Feeling">Good, Fair or Hard.</param>
/// <param name="Difficulties">Dinners, Weekends, EatingOut, Schedules, Cravings; may be empty.</param>
/// <param name="Questions">The questions; may be empty.</param>
/// <param name="SubmittedAt">"Enviado el 15 sept. desde su app".</param>
/// <param name="EditedAt">When it was last edited, if it was.</param>
/// <param name="IsLocked">True once the patient can no longer edit it.</param>
public record ConsultationPatientCheckInResource(
    int FollowUpId,
    string Feeling,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<ConsultationCheckInQuestionResource> Questions,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? EditedAt,
    bool IsLocked)
{
    /// <summary>The visit it was sent for.</summary>
    public int FollowUpId { get; init; } = FollowUpId;

    /// <summary>Good, Fair or Hard.</summary>
    public string Feeling { get; init; } = Feeling;

    /// <summary>Dinners, Weekends, EatingOut, Schedules, Cravings. Empty when none.</summary>
    public IReadOnlyList<string> Difficulties { get; init; } = Difficulties;

    /// <summary>The questions for the practitioner. Empty when none.</summary>
    public IReadOnlyList<ConsultationCheckInQuestionResource> Questions { get; init; } = Questions;

    /// <summary>When it was first sent.</summary>
    public DateTimeOffset SubmittedAt { get; init; } = SubmittedAt;

    /// <summary>When it was last edited; null when never.</summary>
    public DateTimeOffset? EditedAt { get; init; } = EditedAt;

    /// <summary>True once the hour of the visit arrived.</summary>
    public bool IsLocked { get; init; } = IsLocked;
}

/// <summary>
///     A consultation with what each saved step produced: PAC-1.C "Consulta en curso · Paso 1 de 4 · se guardó
///     hoy", and the data to rehydrate EV-2 to EV-5.
/// </summary>
public record ConsultationResource(
    int ConsultationId,
    int PatientId,
    int PractitionerId,
    string State,
    string CurrentStep,
    int StepNumber,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSavedAt,
    ConsultationMeasurementResource? Measurement,
    ConsultationDiagnosisResource? Diagnosis,
    ConsultationTargetsResource? Targets,
    ConsultationPublicationDraftResource? PublicationDraft,
    ConsultationPatientCheckInResource? PatientCheckIn,
    DateTimeOffset? CompletedAt,
    int? PublishedPlanVersion,
    bool IsFirstConsultation,
    int? ScheduledFollowUpId)
{
    /// <summary>Identifier of the consultation.</summary>
    public int ConsultationId { get; init; } = ConsultationId;

    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The practitioner leading it.</summary>
    public int PractitionerId { get; init; } = PractitionerId;

    /// <summary>InProgress, Completed or Abandoned.</summary>
    public string State { get; init; } = State;

    /// <summary>Measurement, Diagnosis, Targets or Publication: the step to continue on.</summary>
    public string CurrentStep { get; init; } = CurrentStep;

    /// <summary>1 to 4, as in "PASO n DE 4".</summary>
    public int StepNumber { get; init; } = StepNumber;

    /// <summary>When it started.</summary>
    public DateTimeOffset StartedAt { get; init; } = StartedAt;

    /// <summary>"Se guardó hoy": the last time a step was saved.</summary>
    public DateTimeOffset LastSavedAt { get; init; } = LastSavedAt;

    /// <summary>Step 1, once saved.</summary>
    public ConsultationMeasurementResource? Measurement { get; init; } = Measurement;

    /// <summary>Step 2, once saved. Professional information: pending until the publication.</summary>
    public ConsultationDiagnosisResource? Diagnosis { get; init; } = Diagnosis;

    /// <summary>Step 3, once a proposal exists.</summary>
    public ConsultationTargetsResource? Targets { get; init; } = Targets;

    /// <summary>Step 4: the restrictions and guidelines saved before publishing, if any.</summary>
    public ConsultationPublicationDraftResource? PublicationDraft { get; init; } = PublicationDraft;

    /// <summary>
    ///     EV-2 "Antes de la consulta, … contó" (MA-4): what the patient sent before the visit, while the
    ///     consultation is in progress. Null when the patient did not answer, or once the consultation is closed.
    /// </summary>
    public ConsultationPatientCheckInResource? PatientCheckIn { get; init; } = PatientCheckIn;

    /// <summary>When the publication closed it.</summary>
    public DateTimeOffset? CompletedAt { get; init; } = CompletedAt;

    /// <summary>"Evaluación y nuevo plan (versión 3)": the version it published.</summary>
    public int? PublishedPlanVersion { get; init; } = PublishedPlanVersion;

    /// <summary>"Primera consulta".</summary>
    public bool IsFirstConsultation { get; init; } = IsFirstConsultation;

    /// <summary>The appointment it started from, if any (MA-2).</summary>
    public int? ScheduledFollowUpId { get; init; } = ScheduledFollowUpId;
}

/// <summary>Step 1 of a consultation: the measurement and the assessment around it.</summary>
public record ConsultationMeasurementResource(
    int AssessmentId,
    decimal WeightKg,
    decimal HeightCm,
    decimal? WaistCm,
    decimal? BodyFatPercentage,
    decimal Bmi,
    string BmiCategory,
    IReadOnlyList<string> ProtocolChecks,
    string? ActivityLevel,
    EatingHabitsResource? Habits,
    BiochemistryPanelResource? Biochemistry,
    int AgeYears,
    string BiologicalSex)
{
    /// <summary>The closed assessment of step 1.</summary>
    public int AssessmentId { get; init; } = AssessmentId;

    /// <summary>Weight in kilograms.</summary>
    public decimal WeightKg { get; init; } = WeightKg;

    /// <summary>Height in centimetres, from the baseline.</summary>
    public decimal HeightCm { get; init; } = HeightCm;

    /// <summary>Waist circumference in centimetres, if measured.</summary>
    public decimal? WaistCm { get; init; } = WaistCm;

    /// <summary>Body fat percentage, if measured.</summary>
    public decimal? BodyFatPercentage { get; init; } = BodyFatPercentage;

    /// <summary>"IMC calculado 26.3 kg/m²".</summary>
    public decimal Bmi { get; init; } = Bmi;

    /// <summary>WHO category of the index. Professional information only.</summary>
    public string BmiCategory { get; init; } = BmiCategory;

    /// <summary>The protocol checklist.</summary>
    public IReadOnlyList<string> ProtocolChecks { get; init; } = ProtocolChecks;

    /// <summary>Sedentary, Light, Moderate or Intense.</summary>
    public string? ActivityLevel { get; init; } = ActivityLevel;

    /// <summary>Eating habits, if recorded.</summary>
    public EatingHabitsResource? Habits { get; init; } = Habits;

    /// <summary>Biochemistry, if recorded.</summary>
    public BiochemistryPanelResource? Biochemistry { get; init; } = Biochemistry;

    /// <summary>Age on the day of the consultation, from the baseline.</summary>
    public int AgeYears { get; init; } = AgeYears;

    /// <summary>Female or Male, from the baseline.</summary>
    public string BiologicalSex { get; init; } = BiologicalSex;
}

/// <summary>Step 2 of a consultation. Professional information: never reaches the patient.</summary>
public record ConsultationDiagnosisResource(
    int DiagnosisId,
    string? Code,
    string? Source,
    string Rationale,
    decimal? BmiAtIssue,
    long? AiGenerationId,
    DateTimeOffset IssuedAt,
    bool IsPending)
{
    /// <summary>Identifier of the diagnosis.</summary>
    public int DiagnosisId { get; init; } = DiagnosisId;

    /// <summary>The closed list code.</summary>
    public string? Code { get; init; } = Code;

    /// <summary>AiSuggestionAccepted or PractitionerSelected.</summary>
    public string? Source { get; init; } = Source;

    /// <summary>The clinical rationale.</summary>
    public string Rationale { get; init; } = Rationale;

    /// <summary>The body mass index it read.</summary>
    public decimal? BmiAtIssue { get; init; } = BmiAtIssue;

    /// <summary>The AI generation whose suggestion was accepted, if any.</summary>
    public long? AiGenerationId { get; init; } = AiGenerationId;

    /// <summary>When it was issued in step 2.</summary>
    public DateTimeOffset IssuedAt { get; init; } = IssuedAt;

    /// <summary>True until the publication makes it the active diagnosis.</summary>
    public bool IsPending { get; init; } = IsPending;
}

/// <summary>Step 3 of a consultation: the draft version, or the published one once completed.</summary>
public record ConsultationTargetsResource(
    int PlanId,
    int Version,
    CalculationBasisResource CalculationBasis,
    TargetsResource Proposal,
    TargetsResource? Prescribed,
    string? PrescriptionOutcome,
    string? OverrideReason,
    bool IsPublished,
    IReadOnlyList<string> ActivePlanLegacyRestrictions)
{
    /// <summary>Identifier of the plan version.</summary>
    public int PlanId { get; init; } = PlanId;

    /// <summary>The version number it will have, or has.</summary>
    public int Version { get; init; } = Version;

    /// <summary>"Calculado con": equation, factors and results. Never leaves this context.</summary>
    public CalculationBasisResource CalculationBasis { get; init; } = CalculationBasis;

    /// <summary>The calculated proposal.</summary>
    public TargetsResource Proposal { get; init; } = Proposal;

    /// <summary>The prescribed targets, once accepted or overridden.</summary>
    public TargetsResource? Prescribed { get; init; } = Prescribed;

    /// <summary>AcceptedAsProposed or Overridden.</summary>
    public string? PrescriptionOutcome { get; init; } = PrescriptionOutcome;

    /// <summary>Why the proposal was overridden, if it was.</summary>
    public string? OverrideReason { get; init; } = OverrideReason;

    /// <summary>True once step 4 published it.</summary>
    public bool IsPublished { get; init; } = IsPublished;

    /// <summary>
    ///     NC-6. Free text restrictions of the version in force that match no code; EV-5 maps them before
    ///     publishing.
    /// </summary>
    public IReadOnlyList<string> ActivePlanLegacyRestrictions { get; init; } = ActivePlanLegacyRestrictions;
}

/// <summary>Step 4 before publishing: what EV-5 had chosen.</summary>
public record ConsultationPublicationDraftResource(
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> CustomGuidelines,
    string? PatientMessage = null)
{
    /// <summary>NC-9. The message for the patient being written, or null.</summary>
    public string? PatientMessage { get; init; } = PatientMessage;

    /// <summary>Restriction codes.</summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>Guideline catalog codes.</summary>
    public IReadOnlyList<string> Guidelines { get; init; } = Guidelines;

    /// <summary>Custom guidelines ("Otra indicación").</summary>
    public IReadOnlyList<string> CustomGuidelines { get; init; } = CustomGuidelines;
}

/// <summary>The suggestion card of EV-3. Professional information: never reaches the patient.</summary>
public record DiagnosisSuggestionResource(
    long? AiGenerationId,
    string Code,
    string Rationale,
    string Source,
    string Disclaimer)
{
    /// <summary>The AI generation, or null for the deterministic suggestion.</summary>
    public long? AiGenerationId { get; init; } = AiGenerationId;

    /// <summary>The suggested code.</summary>
    public string Code { get; init; } = Code;

    /// <summary>"Fundamento clínico".</summary>
    public string Rationale { get; init; } = Rationale;

    /// <summary>"Ai" (IA-6) or "Rule" for the category of the body mass index.</summary>
    public string Source { get; init; } = Source;

    /// <summary>The decision stays with the practitioner.</summary>
    public string Disclaimer { get; init; } = Disclaimer;
}

/// <summary>
///     IA-7. The guideline chips EV-5 pre-selects for the diagnosis of step 2. Professional information: never
///     reaches the patient until the practitioner publishes the guidelines they choose.
/// </summary>
public record GuidelineSuggestionsResource(IReadOnlyList<string> Suggested, long? AiGenerationId, string Source)
{
    /// <summary>Codes of the guideline catalog (NC-6), in the order of EV-5; never free text.</summary>
    public IReadOnlyList<string> Suggested { get; init; } = Suggested;

    /// <summary>The AI generation, or null for the fixed table.</summary>
    public long? AiGenerationId { get; init; } = AiGenerationId;

    /// <summary>"Ai" (IA-7) or "Rule" (NutritionalCare:DefaultGuidelinesByDiagnosis).</summary>
    public string Source { get; init; } = Source;
}

/// <summary>"Calculado con: Mujer · 31 años · 168 cm · 74.2 kg · actividad moderada".</summary>
public record TargetInputsSummaryResource(string Sex, int AgeYears, decimal HeightCm, decimal WeightKg,
    string ActivityLevel)
{
    /// <summary>Female or Male.</summary>
    public string Sex { get; init; } = Sex;

    /// <summary>Age on the day of the consultation.</summary>
    public int AgeYears { get; init; } = AgeYears;

    /// <summary>Height in centimetres.</summary>
    public decimal HeightCm { get; init; } = HeightCm;

    /// <summary>Weight of step 1 in kilograms.</summary>
    public decimal WeightKg { get; init; } = WeightKg;

    /// <summary>Sedentary, Light, Moderate or Intense.</summary>
    public string ActivityLevel { get; init; } = ActivityLevel;
}

/// <summary>The target proposal of step 3 (EV-4).</summary>
public record ConsultationTargetProposalResource(
    int PlanId,
    CalculationBasisResource CalculationBasis,
    TargetsResource Proposal,
    TargetInputsSummaryResource InputsSummary,
    IReadOnlyList<string> ActivePlanLegacyRestrictions)
{
    /// <summary>The draft version.</summary>
    public int PlanId { get; init; } = PlanId;

    /// <summary>Equation, factors and results. Never leaves this context.</summary>
    public CalculationBasisResource CalculationBasis { get; init; } = CalculationBasis;

    /// <summary>The calculated proposal.</summary>
    public TargetsResource Proposal { get; init; } = Proposal;

    /// <summary>What the equations read.</summary>
    public TargetInputsSummaryResource InputsSummary { get; init; } = InputsSummary;

    /// <summary>NC-6. Free text restrictions of the version in force that match no code.</summary>
    public IReadOnlyList<string> ActivePlanLegacyRestrictions { get; init; } = ActivePlanLegacyRestrictions;
}

/// <summary>One past consultation: PT25 "Anteriores" and PAC-3 "EVALUACIONES".</summary>
public record ConsultationSummaryResource(
    int ConsultationId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? PlanVersion,
    bool IsFirstConsultation,
    decimal? WeightKg,
    decimal? Bmi,
    decimal? WaistCm)
{
    /// <summary>Identifier of the consultation.</summary>
    public int ConsultationId { get; init; } = ConsultationId;

    /// <summary>InProgress, Completed or Abandoned.</summary>
    public string State { get; init; } = State;

    /// <summary>When it started.</summary>
    public DateTimeOffset StartedAt { get; init; } = StartedAt;

    /// <summary>When it closed, if completed.</summary>
    public DateTimeOffset? CompletedAt { get; init; } = CompletedAt;

    /// <summary>The version it published, if completed.</summary>
    public int? PlanVersion { get; init; } = PlanVersion;

    /// <summary>"Primera consulta".</summary>
    public bool IsFirstConsultation { get; init; } = IsFirstConsultation;

    /// <summary>Weight of step 1, if saved.</summary>
    public decimal? WeightKg { get; init; } = WeightKg;

    /// <summary>Body mass index of step 1, if saved.</summary>
    public decimal? Bmi { get; init; } = Bmi;

    /// <summary>Waist of step 1, if measured.</summary>
    public decimal? WaistCm { get; init; } = WaistCm;
}
