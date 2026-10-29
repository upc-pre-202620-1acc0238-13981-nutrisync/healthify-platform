namespace Healthify.Platform.ReadModels.Interfaces.REST.Resources;

/// <summary>
///     The record as the practitioner reads it: every field of <see cref="PatientRecordResource" />
///     plus the active diagnosis, the evaluations with their BMI, the clinical weight and the compliance.
/// </summary>
/// <remarks>
///     Practitioner only. It is a separate type from <see cref="PatientOwnRecordResource" /> so the diagnosis
///     cannot reach a patient by accident: nothing the patient receives is built from this type.
/// </remarks>
public record PractitionerPatientRecordResource : PatientRecordResource
{
    public PractitionerPatientRecordResource(
        PatientRecordResource record,
        RecordActiveDiagnosisResource? activeDiagnosis,
        IReadOnlyList<RecordEvaluationResource> evaluations,
        RecordClinicalWeightResource? clinicalWeight,
        RecordBmiResource? bmi,
        RecordComplianceResource? compliance) : base(record)
    {
        ActiveDiagnosis = activeDiagnosis;
        Evaluations = evaluations;
        ClinicalWeight = clinicalWeight;
        Bmi = bmi;
        Compliance = compliance;
    }

    /// <summary>The active diagnosis with its date, shown to the practitioner only, or null.</summary>
    public RecordActiveDiagnosisResource? ActiveDiagnosis { get; init; }

    /// <summary>The evaluations, most recent first.</summary>
    public IReadOnlyList<RecordEvaluationResource> Evaluations { get; init; }

    /// <summary>The clinical weight and its change since the first evaluation, or null without evaluations.</summary>
    public RecordClinicalWeightResource? ClinicalWeight { get; init; }

    /// <summary>BMI and its category of the latest evaluation, or null.</summary>
    public RecordBmiResource? Bmi { get; init; }

    /// <summary>Compliance as "5 of 7 days", or null.</summary>
    public RecordComplianceResource? Compliance { get; init; }
}

/// <summary>
///     The record as the patient reads it: every field of <see cref="PatientRecordResource" /> plus
///     their practitioner, next consultation, numbers, plan and referrals.
/// </summary>
/// <remarks>
///     Business rule: Diagnosis Never Leaves The Context, for the patient. No diagnosis, calculation basis, BMI or BMI
///     category, and none must ever be added here.
/// </remarks>
public record PatientOwnRecordResource : PatientRecordResource
{
    public PatientOwnRecordResource(
        PatientRecordResource record,
        RecordPractitionerResource? practitioner,
        RecordNextFollowUpResource? nextFollowUp,
        RecordMyNumbersResource myNumbers,
        RecordPlanResource? plan,
        IReadOnlyList<RecordReferralResource> referrals) : base(record)
    {
        Practitioner = practitioner;
        NextFollowUp = nextFollowUp;
        MyNumbers = myNumbers;
        Plan = plan;
        Referrals = referrals;
    }

    /// <summary>Their nutritionist, since when and the link status, or null.</summary>
    public RecordPractitionerResource? Practitioner { get; init; }

    /// <summary>The next consultation, or null.</summary>
    public RecordNextFollowUpResource? NextFollowUp { get; init; }

    /// <summary>The "my numbers" section.</summary>
    public RecordMyNumbersResource MyNumbers { get; init; }

    /// <summary>The "my plan" section with guidelines and restrictions, or null without a published plan.</summary>
    public RecordPlanResource? Plan { get; init; }

    /// <summary>The referrals, most recent first; the app shows "no active referrals" when none is Open.</summary>
    public IReadOnlyList<RecordReferralResource> Referrals { get; init; }
}

/// <summary>The active diagnosis. Practitioner only.</summary>
/// <param name="DiagnosisId">Identifier of the diagnosis.</param>
/// <param name="Code">Closed list code; the client translates it. Null for diagnoses written before the closed list.</param>
/// <param name="Statement">The statement as recorded.</param>
/// <param name="IssuedAt">When it was issued.</param>
public record RecordActiveDiagnosisResource(int DiagnosisId, string? Code, string Statement, DateTimeOffset IssuedAt)
{
    /// <summary>Identifier of the diagnosis.</summary>
    public int DiagnosisId { get; init; } = DiagnosisId;

    /// <summary>Closed list code, or null for diagnoses written before it.</summary>
    public string? Code { get; init; } = Code;

    /// <summary>The statement as the practitioner recorded it.</summary>
    public string Statement { get; init; } = Statement;

    /// <summary>When it was issued.</summary>
    public DateTimeOffset IssuedAt { get; init; } = IssuedAt;
}

/// <summary>One evaluation of the patient record. Practitioner only.</summary>
/// <param name="AssessmentId">The assessment.</param>
/// <param name="TakenAt">When the measurement was taken.</param>
/// <param name="WeightKg">Weight in kilograms.</param>
/// <param name="Bmi">BMI.</param>
/// <param name="BmiCategory">BMI category code.</param>
/// <param name="WaistCm">Waist in centimetres, or null.</param>
/// <param name="IsFirst">Whether it is the first evaluation.</param>
public record RecordEvaluationResource(
    int AssessmentId,
    DateTimeOffset TakenAt,
    decimal WeightKg,
    decimal Bmi,
    string BmiCategory,
    decimal? WaistCm,
    bool IsFirst)
{
    /// <summary>The assessment.</summary>
    public int AssessmentId { get; init; } = AssessmentId;

    /// <summary>When the measurement was taken.</summary>
    public DateTimeOffset TakenAt { get; init; } = TakenAt;

    /// <summary>Weight in kilograms.</summary>
    public decimal WeightKg { get; init; } = WeightKg;

    /// <summary>BMI in kg/m².</summary>
    public decimal Bmi { get; init; } = Bmi;

    /// <summary>BMI category code (the client translates it).</summary>
    public string BmiCategory { get; init; } = BmiCategory;

    /// <summary>Waist in centimetres, or null.</summary>
    public decimal? WaistCm { get; init; } = WaistCm;

    /// <summary>The first evaluation of the patient.</summary>
    public bool IsFirst { get; init; } = IsFirst;
}

/// <summary>The clinical weight and its change since the first evaluation. Practitioner only.</summary>
/// <param name="LatestKg">Weight of the latest evaluation.</param>
/// <param name="LatestDate">When it was taken.</param>
/// <param name="DeltaKgSinceFirst">Latest minus first.</param>
/// <param name="FirstDate">When the first evaluation was taken.</param>
public record RecordClinicalWeightResource(
    decimal LatestKg,
    DateTimeOffset LatestDate,
    decimal DeltaKgSinceFirst,
    DateTimeOffset FirstDate)
{
    /// <summary>Weight of the latest evaluation.</summary>
    public decimal LatestKg { get; init; } = LatestKg;

    /// <summary>When it was taken.</summary>
    public DateTimeOffset LatestDate { get; init; } = LatestDate;

    /// <summary>Latest minus first: negative when the weight went down.</summary>
    public decimal DeltaKgSinceFirst { get; init; } = DeltaKgSinceFirst;

    /// <summary>When the first evaluation was taken ("since March").</summary>
    public DateTimeOffset FirstDate { get; init; } = FirstDate;
}

/// <summary>BMI and its category. Practitioner only.</summary>
/// <param name="Value">BMI of the latest evaluation.</param>
/// <param name="Category">Its category code.</param>
public record RecordBmiResource(decimal Value, string Category)
{
    /// <summary>BMI of the latest evaluation.</summary>
    public decimal Value { get; init; } = Value;

    /// <summary>Its category code (the client translates it).</summary>
    public string Category { get; init; } = Category;
}

/// <summary>Compliance as "5 of 7 days" over the last 7 calendar days.</summary>
/// <param name="From">First of the days.</param>
/// <param name="To">Last of the days.</param>
/// <param name="Met">Days within the targets.</param>
/// <param name="Total">Calendar days.</param>
public record RecordComplianceResource(DateOnly From, DateOnly To, int Met, int Total)
{
    /// <summary>First of the days counted.</summary>
    public DateOnly From { get; init; } = From;

    /// <summary>Last of the days counted (today).</summary>
    public DateOnly To { get; init; } = To;

    /// <summary>Days within the targets: the "5".</summary>
    public int Met { get; init; } = Met;

    /// <summary>Calendar days, unlogged ones included: the "7".</summary>
    public int Total { get; init; } = Total;
}

/// <summary>The patient's nutritionist, since when and the link status.</summary>
/// <param name="PractitionerId">Identifier of the practitioner.</param>
/// <param name="FullName">Their full name.</param>
/// <param name="LinkedSince">When the link was established, or null.</param>
/// <param name="LinkStatus">Active or PendingConsent.</param>
public record RecordPractitionerResource(int PractitionerId, string FullName, DateTimeOffset? LinkedSince,
    string LinkStatus)
{
    /// <summary>Identifier of the practitioner.</summary>
    public int PractitionerId { get; init; } = PractitionerId;

    /// <summary>Their full name.</summary>
    public string FullName { get; init; } = FullName;

    /// <summary>When the link was established, or null.</summary>
    public DateTimeOffset? LinkedSince { get; init; } = LinkedSince;

    /// <summary>Active or PendingConsent.</summary>
    public string LinkStatus { get; init; } = LinkStatus;
}

/// <summary>The patient's next consultation: day and time.</summary>
/// <param name="FollowUpId">Identifier of the visit.</param>
/// <param name="ScheduledFor">When it is.</param>
/// <param name="Modality">InPerson or Remote.</param>
/// <param name="Preparation">Preparation codes; empty when none.</param>
public record RecordNextFollowUpResource(int FollowUpId, DateTimeOffset ScheduledFor, string Modality,
    IReadOnlyList<string> Preparation)
{
    /// <summary>Identifier of the visit.</summary>
    public int FollowUpId { get; init; } = FollowUpId;

    /// <summary>When it is.</summary>
    public DateTimeOffset ScheduledFor { get; init; } = ScheduledFor;

    /// <summary>InPerson or Remote.</summary>
    public string Modality { get; init; } = Modality;

    /// <summary>Preparation codes (the client translates them); empty when none.</summary>
    public IReadOnlyList<string> Preparation { get; init; } = Preparation;
}

/// <summary>The clinical weight and the date of its evaluation. Weight and date only: no BMI.</summary>
/// <param name="Kg">Weight in kilograms.</param>
/// <param name="TakenAt">When it was taken.</param>
public record RecordPatientClinicalWeightResource(decimal Kg, DateTimeOffset TakenAt)
{
    /// <summary>Weight in kilograms.</summary>
    public decimal Kg { get; init; } = Kg;

    /// <summary>When the practitioner took it.</summary>
    public DateTimeOffset TakenAt { get; init; } = TakenAt;
}

/// <summary>The patient's "my numbers" section. No BMI, no category, no diagnosis.</summary>
/// <param name="EnergyTargetKcal">The daily energy target in kcal, or null without a plan.</param>
/// <param name="PlanVersion">The plan version, or null.</param>
/// <param name="Compliance">Their compliance as "5 of 7 days", or null.</param>
/// <param name="ClinicalWeight">The clinical weight, or null.</param>
/// <param name="WeightSlopeKgPerWeek">Their weight trend in kg per week, or null.</param>
public record RecordMyNumbersResource(
    decimal? EnergyTargetKcal,
    int? PlanVersion,
    RecordComplianceResource? Compliance,
    RecordPatientClinicalWeightResource? ClinicalWeight,
    decimal? WeightSlopeKgPerWeek)
{
    /// <summary>Daily energy target, or null without a published plan.</summary>
    public decimal? EnergyTargetKcal { get; init; } = EnergyTargetKcal;

    /// <summary>Version of the plan in force, or null.</summary>
    public int? PlanVersion { get; init; } = PlanVersion;

    /// <summary>The last seven calendar days, or null.</summary>
    public RecordComplianceResource? Compliance { get; init; } = Compliance;

    /// <summary>The latest clinical weight, or null.</summary>
    public RecordPatientClinicalWeightResource? ClinicalWeight { get; init; } = ClinicalWeight;

    /// <summary>Kilograms per week of the home series (4 weeks), or null. Never today's weight.</summary>
    public decimal? WeightSlopeKgPerWeek { get; init; } = WeightSlopeKgPerWeek;
}

/// <summary>The patient's "my plan" section: guidelines and restrictions.</summary>
/// <param name="Guidelines">A catalog code or a custom text each.</param>
/// <param name="Restrictions">Restriction codes.</param>
/// <param name="LegacyRestrictions">Free text restrictions from before the closed list.</param>
public record RecordPlanResource(
    IReadOnlyList<RecordGuidelineResource> Guidelines,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<string> LegacyRestrictions)
{
    /// <summary>A catalog code (translate it) or a custom text (show it as written) each.</summary>
    public IReadOnlyList<RecordGuidelineResource> Guidelines { get; init; } = Guidelines;

    /// <summary>Restriction codes.</summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>Free text restrictions from before the closed list, shown as written.</summary>
    public IReadOnlyList<string> LegacyRestrictions { get; init; } = LegacyRestrictions;
}
