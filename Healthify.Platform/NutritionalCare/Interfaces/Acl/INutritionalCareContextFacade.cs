namespace Healthify.Platform.NutritionalCare.Interfaces.Acl;

/// <summary>
///     Flat DTO exposed to other bounded contexts - primitives only. It mirrors the published
///     contract exactly: targets, guidelines and restrictions.
///     NC-6: <c>Guidelines</c> keeps the original strings (code or custom text), <c>GuidelineItems</c> says
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
///     NC-6. One guideline of the published contract, primitives only: a catalog code or a custom text.
/// </summary>
public record ActiveGuidelineItem(string? Code, string? Custom);

/// <summary>
///     RM-2. The baseline of a patient as PAC-1 shows it ("Mujer · 31 años · 168 cm · hipotiroidismo"),
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
///     RM-2. PAC-1.C "Consulta en curso · Paso 1 de 4 · se guardó hoy", primitives only. Nothing the steps
///     produced (measurement, diagnosis, targets) crosses: only where the practitioner left off.
/// </summary>
/// <param name="ConsultationId">The consultation to resume.</param>
/// <param name="StepNumber">1 to 4.</param>
/// <param name="StepName">Measurement, Diagnosis, Targets or Publication.</param>
/// <param name="LastSavedAt">When a step was last saved.</param>
public record ConsultationInProgressItem(int ConsultationId, int StepNumber, string StepName,
    DateTimeOffset LastSavedAt);

/// <summary>
///     RM-5. One past consultation as the patient sees it (PT25 "ANTERIORES: 3 de septiembre de 2026 · Evaluación y
///     nuevo plan (versión 3) / 12 de marzo de 2026 · Primera consulta"). Primitives only.
/// </summary>
/// <remarks>
///     Deliberately without weight, BMI, BMI category or diagnosis: the patient never receives a diagnosis, and
///     the BMI category is one (§12-#9).
/// </remarks>
/// <param name="ConsultationId">The consultation.</param>
/// <param name="CompletedAt">When it was published.</param>
/// <param name="PlanVersion">The version it published.</param>
/// <param name="IsFirstConsultation">True for "Primera consulta".</param>
public record CompletedConsultationItem(int ConsultationId, DateTimeOffset CompletedAt, int? PlanVersion,
    bool IsFirstConsultation);

/// <summary>
///     RM-1. Where one patient of the roster stands in Nutritional Care, primitives only: whether there is a
///     baseline, the version in force, a consultation to resume and a signal waiting in the inbox.
/// </summary>
/// <param name="PatientId">Whose status.</param>
/// <param name="HasBaseline">False means the roster shows "Nueva" and the summary PAC-0.</param>
/// <param name="ActivePlanVersion">The published version in force, or null ("sin plan").</param>
/// <param name="HasConsultationInProgress">PAC-1.C, "Consulta en curso".</param>
/// <param name="HasOpenReviewItem">A signal of this patient waits in the practitioner inbox.</param>
public record PatientCareStatusItem(
    int PatientId,
    bool HasBaseline,
    int? ActivePlanVersion,
    bool HasConsultationInProgress,
    bool HasOpenReviewItem);

/// <summary>
///     RM-3/RM-4. The latest clinical measurement (PAC-2 "Última medición clínica · 3 sept. · en ayunas, sin
///     zapatos · 74.2 kg", PT20 "Peso clínico 74.2 kg"). Primitives only, and deliberately without BMI or BMI
///     category, so a patient-facing composer can use it.
/// </summary>
/// <param name="TakenAt">When it was taken.</param>
/// <param name="WeightKg">Weight in kilograms.</param>
/// <param name="ProtocolChecks">EV-2 checklist codes (Fasting, NoShoes…); empty for measurements before NC-3.</param>
/// <param name="Protocol">The protocol as recorded (free text before NC-3, the readable summary since).</param>
public record ClinicalMeasurementItem(
    DateTimeOffset TakenAt,
    decimal WeightKg,
    IReadOnlyList<string> ProtocolChecks,
    string Protocol);

/// <summary>
///     RM-4. One evaluation of PAC-3 "EVALUACIONES: 3 sept. 2026 · 74.2 kg · IMC 26.3 · cintura 88 cm". Primitives
///     only. PRACTITIONER ONLY: the BMI category is, in effect, a diagnosis (§12-#9) and must never reach a
///     patient-facing resource.
/// </summary>
/// <param name="AssessmentId">The assessment it belongs to.</param>
/// <param name="TakenAt">When the measurement was taken.</param>
/// <param name="WeightKg">Weight in kilograms.</param>
/// <param name="BmiKgM2">BMI.</param>
/// <param name="BmiCategory">BMI category code (practitioner only).</param>
/// <param name="WaistCircumferenceCm">Waist, or null.</param>
/// <param name="IsFirst">The first evaluation of the patient ("primera evaluación").</param>
public record ClinicalEvaluationItem(
    int AssessmentId,
    DateTimeOffset TakenAt,
    decimal WeightKg,
    decimal BmiKgM2,
    string BmiCategory,
    decimal? WaistCircumferenceCm,
    bool IsFirst);

/// <summary>
///     RM-4. PAC-3 "Diagnóstico activo · Sobrepeso grado I · 3 mar. 2026 · solo profesional". Primitives only.
///     PRACTITIONER ONLY: never mapped to a patient-facing resource (Diagnosis Never Leaves The Context, for the
///     patient). No rationale and no calculation basis.
/// </summary>
/// <param name="DiagnosisId">The active diagnosis.</param>
/// <param name="Code">Closed list code (NC-4), or null for diagnoses written before it.</param>
/// <param name="Statement">The statement as the practitioner recorded it.</param>
/// <param name="IssuedAt">When it was issued.</param>
public record ActiveDiagnosisItem(int DiagnosisId, string? Code, string Statement, DateTimeOffset IssuedAt);

/// <summary>
///     Public ACL contract of the Nutritional Care bounded context.
/// </summary>
/// <remarks>
///     It never exposes a clinical rationale or a calculation basis. Those are professional information and
///     they do not leave this context, by the same rule that keeps them out of the published event. Every
///     method degrades gracefully.
///     RM-4 publishes, explicitly and for the practitioner's record only (PAC-3), the active diagnosis
///     (<see cref="GetActiveDiagnosis" />) and the evaluations with their BMI category
///     (<see cref="GetClinicalEvaluations" />). Patient-facing composers must not call them.
/// </remarks>
public interface INutritionalCareContextFacade
{
    /// <summary>The published contract for a patient, or null when there is none or the lookup fails.</summary>
    Task<ActiveTargetsItem?> GetActiveTargetsByPatientId(int patientId, CancellationToken ct = default);

    /// <summary>How many signals are waiting in a practitioner inbox. Zero when the lookup fails.</summary>
    Task<int> GetOpenReviewItemCount(int practitionerId, CancellationToken ct = default);

    /// <summary>RM-2. The baseline of a patient, or null when there is none (PAC-0) or the lookup fails.</summary>
    Task<PatientBaselineSummaryItem?> GetBaselineSummary(int patientId, CancellationToken ct = default);

    /// <summary>RM-2. The consultation in progress of a patient, or null when there is none or the lookup fails.</summary>
    Task<ConsultationInProgressItem?> GetConsultationInProgress(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     RM-2. When the last completed consultation of a patient was published ("desde la última consulta"), or
    ///     null when there is none yet or the lookup fails.
    /// </summary>
    Task<DateTimeOffset?> GetLastCompletedConsultationAt(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     RM-1. The status of every given patient, keyed by patient, from one read per kind (baselines, active
    ///     versions, consultations in progress, open items of the practitioner) instead of one per patient. Empty
    ///     when the lookup fails.
    /// </summary>
    Task<IReadOnlyDictionary<int, PatientCareStatusItem>> GetCareStatusByPatientIds(int practitionerId,
        IEnumerable<int> patientIds, CancellationToken ct = default);

    /// <summary>
    ///     RM-5. The completed consultations of a patient, most recent first, without anything clinical. Empty when
    ///     there are none or the lookup fails.
    /// </summary>
    Task<IReadOnlyList<CompletedConsultationItem>> GetCompletedConsultations(int patientId,
        CancellationToken ct = default);

    /// <summary>
    ///     RM-3/RM-4. The latest clinical measurement of the evaluations in force (see
    ///     <see cref="GetClinicalEvaluations" />), without BMI. Null when there is none or the lookup fails.
    /// </summary>
    Task<ClinicalMeasurementItem?> GetLatestClinicalMeasurement(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     RM-4. PRACTITIONER ONLY. The evaluations of the patient, oldest first: the latest measurement of each
    ///     closed stand-alone assessment and of each completed consultation, leaving out assessments corrected by a
    ///     later one. Empty when there are none or the lookup fails.
    /// </summary>
    Task<IReadOnlyList<ClinicalEvaluationItem>> GetClinicalEvaluations(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     RM-4. PRACTITIONER ONLY. The active diagnosis (never a pending or discarded one), or null when there is
    ///     none or the lookup fails.
    /// </summary>
    Task<ActiveDiagnosisItem?> GetActiveDiagnosis(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     IA-5. Whether a ConsistencyEscalation review item was ever opened for the patient (open or resolved): the
    ///     practitioner already heard about the consistency index. False when there is none or the lookup fails.
    /// </summary>
    Task<bool> HasConsistencyEscalation(int patientId, CancellationToken ct = default);
}
