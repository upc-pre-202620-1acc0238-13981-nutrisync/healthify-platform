namespace Healthify.Platform.NutritionalCare.Interfaces.Acl;

/// <summary>
///     Flat DTO exposed to other bounded contexts - primitives only. It mirrors the published
///     contract exactly: targets, guidelines and restrictions.
///     <c>Guidelines</c> keeps the original strings (code or custom text), <c>GuidelineItems</c> says
///     which is which, <c>Restrictions</c> are codes and <c>LegacyRestrictions</c> the free text ones from
///     before the closed list.
/// </summary>
public record ActiveTargetsItem(
    int PatientId,
    int PlanVersion,
    DateTimeOffset ValidFrom,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<ActiveGuidelineItem>? GuidelineItems = null,
    IReadOnlyList<string>? LegacyRestrictions = null);

/// <summary>
///     One guideline of the published contract, primitives only: a catalog code or a custom text.
/// </summary>
public record ActiveGuidelineItem(string? Code, string? Custom);

/// <summary>
///     The baseline of a patient as the patient record shows it (sex, age, height, conditions),
///     primitives only. Read by practitioner-only composers.
/// </summary>
/// <param name="PatientId">Whose baseline.</param>
/// <param name="BiologicalSex">Female or Male.</param>
/// <param name="AgeYears">Completed years on the practice's today.</param>
/// <param name="HeightCm">Height in centimetres.</param>
/// <param name="Conditions">Medical history codes of the closed list; empty means none.</param>
public record PatientBaselineSummaryItem(
    int PatientId,
    string BiologicalSex,
    int AgeYears,
    decimal HeightCm,
    IReadOnlyList<string> Conditions);

/// <summary>
///     The consultation in progress banner (current step and when it was last saved), primitives only. Nothing the steps
///     produced (measurement, diagnosis, targets) crosses: only where the practitioner left off.
/// </summary>
/// <param name="ConsultationId">The consultation to resume.</param>
/// <param name="StepNumber">1 to 4.</param>
/// <param name="StepName">Measurement, Diagnosis, Targets or Publication.</param>
/// <param name="LastSavedAt">When a step was last saved.</param>
public record ConsultationInProgressItem(int ConsultationId, int StepNumber, string StepName,
    DateTimeOffset LastSavedAt);

/// <summary>
///     One past consultation as the patient sees it in their history: its date and either the plan version it
///     published or that it was the first consultation. Primitives only.
/// </summary>
/// <remarks>
///     Deliberately without weight, BMI, BMI category or diagnosis: the patient never receives a diagnosis, and
///     the BMI category is one.
/// </remarks>
/// <param name="ConsultationId">The consultation.</param>
/// <param name="CompletedAt">When it was published.</param>
/// <param name="PlanVersion">The version it published.</param>
/// <param name="IsFirstConsultation">True for the first consultation.</param>
public record CompletedConsultationItem(int ConsultationId, DateTimeOffset CompletedAt, int? PlanVersion,
    bool IsFirstConsultation);

/// <summary>
///     Where one patient of the roster stands in Nutritional Care, primitives only: whether there is a
///     baseline, the version in force, a consultation to resume and a signal waiting in the inbox.
/// </summary>
/// <param name="PatientId">Whose status.</param>
/// <param name="HasBaseline">False means the roster shows the patient as new and the summary is empty.</param>
/// <param name="ActivePlanVersion">The published version in force, or null (no plan).</param>
/// <param name="HasConsultationInProgress">A consultation is in progress.</param>
/// <param name="HasOpenReviewItem">A signal of this patient waits in the practitioner inbox.</param>
public record PatientCareStatusItem(
    int PatientId,
    bool HasBaseline,
    int? ActivePlanVersion,
    bool HasConsultationInProgress,
    bool HasOpenReviewItem);

/// <summary>
///     The latest clinical measurement (date, protocol and weight, as the patient record and the patient's
///     clinical weight show it). Primitives only, and deliberately without BMI or BMI
///     category, so a patient-facing composer can use it.
/// </summary>
/// <param name="TakenAt">When it was taken.</param>
/// <param name="WeightKg">Weight in kilograms.</param>
/// <param name="ProtocolChecks">Protocol checklist codes (Fasting, NoShoes…); empty for older free text measurements.</param>
/// <param name="Protocol">The protocol as recorded (free text on older measurements, otherwise the readable summary).</param>
public record ClinicalMeasurementItem(
    DateTimeOffset TakenAt,
    decimal WeightKg,
    IReadOnlyList<string> ProtocolChecks,
    string Protocol);

/// <summary>
///     One evaluation of the practitioner's evaluations list (date, weight, BMI, waist). Primitives
///     only. PRACTITIONER ONLY: the BMI category is, in effect, a diagnosis and must never reach a
///     patient-facing resource.
/// </summary>
/// <param name="AssessmentId">The assessment it belongs to.</param>
/// <param name="TakenAt">When the measurement was taken.</param>
/// <param name="WeightKg">Weight in kilograms.</param>
/// <param name="BmiKgM2">BMI.</param>
/// <param name="BmiCategory">BMI category code (practitioner only).</param>
/// <param name="WaistCircumferenceCm">Waist, or null.</param>
/// <param name="IsFirst">The first evaluation of the patient.</param>
public record ClinicalEvaluationItem(
    int AssessmentId,
    DateTimeOffset TakenAt,
    decimal WeightKg,
    decimal BmiKgM2,
    string BmiCategory,
    decimal? WaistCircumferenceCm,
    bool IsFirst);

/// <summary>
///     The active diagnosis as the practitioner's evaluations list shows it (code and date). Primitives only.
///     PRACTITIONER ONLY: never mapped to a patient-facing resource (Diagnosis Never Leaves The Context, for the
///     patient). No rationale and no calculation basis.
/// </summary>
/// <param name="DiagnosisId">The active diagnosis.</param>
/// <param name="Code">Closed list code, or null for diagnoses written before it.</param>
/// <param name="Statement">The statement as the practitioner recorded it.</param>
/// <param name="IssuedAt">When it was issued.</param>
public record ActiveDiagnosisItem(int DiagnosisId, string? Code, string Statement, DateTimeOffset IssuedAt);

/// <summary>
///     Data export. One published version of the plan as the patient downloads it (plan history). Primitives only.
/// </summary>
/// <remarks>
///     Business rule: Diagnosis And Basis Never Leave The Context (Subflow 3.5). No diagnosis, rationale, calculation
///     basis or BMI. The change reason crosses only as its code and parameters, which the reader words in their
///     language: a <c>Custom</c> reason is the practitioner's clinical text and stays here (only the code crosses).
/// </remarks>
/// <param name="Version">Version number.</param>
/// <param name="PublishedAt">When it was published: the version is in force from then.</param>
/// <param name="IsActive">The version in force now.</param>
/// <param name="EnergyKcal">Daily energy target.</param>
/// <param name="ProteinG">Daily protein target.</param>
/// <param name="CarbG">Daily carbohydrate target.</param>
/// <param name="FatG">Daily fat target.</param>
/// <param name="ChangeReasonCode">NewConsultation, SignalAdjustment or Custom; null for the first version.</param>
/// <param name="ChangeReasonDate">The date parameter of the code, or null.</param>
/// <param name="ChangeReasonSignalType">The signal type of SignalAdjustment, or null.</param>
public record PlanVersionHistoryItem(
    int Version,
    DateTimeOffset PublishedAt,
    bool IsActive,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    string? ChangeReasonCode,
    DateOnly? ChangeReasonDate,
    string? ChangeReasonSignalType);

/// <summary>
///     Public ACL contract of the Nutritional Care bounded context.
/// </summary>
/// <remarks>
///     It never exposes a clinical rationale or a calculation basis. Those are professional information and
///     they do not leave this context, by the same rule that keeps them out of the published event. Every
///     method degrades gracefully.
///     The clinical summary publishes, explicitly and for the practitioner's record only, the active diagnosis
///     (<see cref="GetActiveDiagnosis" />) and the evaluations with their BMI category
///     (<see cref="GetClinicalEvaluations" />). Patient-facing composers must not call them.
/// </remarks>
public interface INutritionalCareContextFacade
{
    /// <summary>The published contract for a patient, or null when there is none or the lookup fails.</summary>
    Task<ActiveTargetsItem?> GetActiveTargetsByPatientId(int patientId, CancellationToken ct = default);

    /// <summary>How many signals are waiting in a practitioner inbox. Zero when the lookup fails.</summary>
    Task<int> GetOpenReviewItemCount(int practitionerId, CancellationToken ct = default);

    /// <summary>The baseline of a patient, or null when there is none or the lookup fails.</summary>
    Task<PatientBaselineSummaryItem?> GetBaselineSummary(int patientId, CancellationToken ct = default);

    /// <summary>The consultation in progress of a patient, or null when there is none or the lookup fails.</summary>
    Task<ConsultationInProgressItem?> GetConsultationInProgress(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     When the last completed consultation of a patient was published ("since the last consultation"), or
    ///     null when there is none yet or the lookup fails.
    /// </summary>
    Task<DateTimeOffset?> GetLastCompletedConsultationAt(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     The status of every given patient, keyed by patient, from one read per kind (baselines, active
    ///     versions, consultations in progress, open items of the practitioner) instead of one per patient. Empty
    ///     when the lookup fails.
    /// </summary>
    Task<IReadOnlyDictionary<int, PatientCareStatusItem>> GetCareStatusByPatientIds(int practitionerId,
        IEnumerable<int> patientIds, CancellationToken ct = default);

    /// <summary>
    ///     The completed consultations of a patient, most recent first, without anything clinical. Empty when
    ///     there are none or the lookup fails.
    /// </summary>
    Task<IReadOnlyList<CompletedConsultationItem>> GetCompletedConsultations(int patientId,
        CancellationToken ct = default);

    /// <summary>
    ///     The latest clinical measurement of the evaluations in force (see
    ///     <see cref="GetClinicalEvaluations" />), without BMI. Null when there is none or the lookup fails.
    /// </summary>
    Task<ClinicalMeasurementItem?> GetLatestClinicalMeasurement(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     PRACTITIONER ONLY. The evaluations of the patient, oldest first: the latest measurement of each
    ///     closed assessment recorded outside a consultation and of each completed consultation, leaving out assessments corrected by a
    ///     later one. Empty when there are none or the lookup fails.
    /// </summary>
    Task<IReadOnlyList<ClinicalEvaluationItem>> GetClinicalEvaluations(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     PRACTITIONER ONLY. The active diagnosis (never a pending or discarded one), or null when there is
    ///     none or the lookup fails.
    /// </summary>
    Task<ActiveDiagnosisItem?> GetActiveDiagnosis(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     Whether a ConsistencyEscalation review item was ever opened for the patient (open or resolved): the
    ///     practitioner already heard about the consistency index. False when there is none or the lookup fails.
    /// </summary>
    Task<bool> HasConsistencyEscalation(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     Data export. The published versions of the patient's plan, oldest first, without anything clinical. Empty
    ///     when there are none or the lookup fails.
    /// </summary>
    Task<IReadOnlyList<PlanVersionHistoryItem>> GetPlanVersionHistory(int patientId, CancellationToken ct = default);
}
