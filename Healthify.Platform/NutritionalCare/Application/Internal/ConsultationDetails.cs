using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     NC-2. A consultation with what each saved step produced, to rehydrate EV-2 to EV-5 (PAC-1.C "Continuar
///     consulta"). Practitioner only: the diagnosis may be pending and is professional information.
/// </summary>
/// <param name="Consultation">The consultation.</param>
/// <param name="Assessment">Step 1: the assessment with its measurement, if saved.</param>
/// <param name="Diagnosis">Step 2: the diagnosis of the consultation, if saved.</param>
/// <param name="Plan">Step 3: the draft (or, once completed, the published version), if saved.</param>
/// <param name="ActivePlanLegacyRestrictions">
///     NC-6. Free text restrictions of the version in force while the consultation is in progress, so EV-5 can map
///     them to a code.
/// </param>
/// <param name="PatientCheckIn">
///     MA-4. What the patient told before the visit (EV-2 "Antes de la consulta, … contó"), read from Monitoring
///     through its ACL while the consultation is in progress; null otherwise or when there is none.
/// </param>
public record ConsultationDetails(
    Consultation Consultation,
    NutritionalAssessment? Assessment,
    NutritionalDiagnosis? Diagnosis,
    NutritionPlan? Plan,
    IReadOnlyList<string> ActivePlanLegacyRestrictions,
    PreVisitCheckInItem? PatientCheckIn = null);

/// <summary>
///     NC-2. One past consultation of PT25 "Anteriores" and PAC-3 "EVALUACIONES": "3 de septiembre de 2026 ·
///     Evaluación y nuevo plan (versión 3)".
/// </summary>
public record ConsultationSummary(
    int ConsultationId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? PlanVersion,
    bool IsFirstConsultation,
    decimal? WeightKg,
    decimal? Bmi,
    decimal? WaistCm);
