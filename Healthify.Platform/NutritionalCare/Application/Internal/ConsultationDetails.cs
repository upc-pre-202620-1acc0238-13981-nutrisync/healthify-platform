using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     A consultation with what each saved step produced, to rehydrate the four steps when the practitioner
///     resumes it. Practitioner only: the diagnosis may be pending and is professional information.
/// </summary>
/// <param name="Consultation">The consultation.</param>
/// <param name="Assessment">Step 1: the assessment with its measurement, if saved.</param>
/// <param name="Diagnosis">Step 2: the diagnosis of the consultation, if saved.</param>
/// <param name="Plan">Step 3: the draft (or, once completed, the published version), if saved.</param>
/// <param name="ActivePlanLegacyRestrictions">
///     Free text restrictions of the version in force while the consultation is in progress, so step 4 can map
///     them to a code.
/// </param>
/// <param name="PatientCheckIn">
///     What the patient told before the visit (the pre-visit check in shown in step 1), read from Monitoring
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
///     One past consultation in the patient's history and the practitioner's evaluations list: its date and the
///     plan version it published.
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
