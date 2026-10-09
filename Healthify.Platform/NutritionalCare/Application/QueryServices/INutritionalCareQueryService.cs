using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;

namespace Healthify.Platform.NutritionalCare.Application.QueryServices;

/// <summary>Read models Assessment Form, Anthropometry Series and Assessment Timeline.</summary>
public interface INutritionalAssessmentQueryService
{
    Task<NutritionalAssessment?> Handle(GetAssessmentByIdQuery query,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<NutritionalAssessment>> Handle(GetAssessmentsByPatientIdQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>NC-1 - Read model Patient Baseline. Visible to the practitioner only.</summary>
public interface IPatientBaselineQueryService
{
    Task<PatientBaseline?> Handle(GetPatientBaselineByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>RM-1. Empty, never null.</summary>
    Task<IReadOnlyList<PatientBaseline>> Handle(GetPatientBaselinesByPatientIdsQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Read model Active Diagnosis. Visible to the practitioner only.</summary>
public interface INutritionalDiagnosisQueryService
{
    Task<NutritionalDiagnosis?> Handle(GetActiveDiagnosisByPatientIdQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Read models Target Proposal View, Prescribed Targets, Active Plan, Plan Version History.</summary>
public interface INutritionPlanQueryService
{
    Task<NutritionPlan?> Handle(GetPlanByIdQuery query, CancellationToken cancellationToken = default);

    Task<NutritionPlan?> Handle(GetActivePlanByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>RM-1. Empty, never null.</summary>
    Task<IReadOnlyList<NutritionPlan>> Handle(GetActivePlansByPatientIdsQuery query,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<NutritionPlan>> Handle(GetPlansByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>NC-8. The published versions for the patient, most recent first. Empty, never null.</summary>
    Task<IReadOnlyList<PatientPlanVersion>> Handle(GetPatientPlanVersionsQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Read model Practitioner Review Inbox.</summary>
public interface IReviewItemQueryService
{
    Task<ReviewItem?> Handle(GetReviewItemByIdQuery query, CancellationToken cancellationToken = default);

    Task<IEnumerable<ReviewItem>> Handle(GetOpenReviewItemsByPractitionerIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>NC-11. Open or resolved items, most recent first, each with the patient's name. Empty, never null.</summary>
    Task<IReadOnlyList<ReviewInboxEntry>> Handle(GetReviewItemsByPractitionerIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>NC-10. The item and whether its proposal is being generated, or null when the item does not exist.</summary>
    Task<PlanProposalLookup?> Handle(GetPlanProposalByReviewItemIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>NC-11. One item with the patient's name, or null when it does not exist.</summary>
    Task<ReviewInboxEntry?> Handle(GetReviewInboxEntryByIdQuery query, CancellationToken cancellationToken = default);

    Task<int> CountOpen(int practitionerId, CancellationToken cancellationToken = default);

    /// <summary>IA-5. Whether an item of the signal type was ever opened for the patient.</summary>
    Task<bool> Handle(GetReviewItemExistenceQuery query, CancellationToken cancellationToken = default);
}

/// <summary>NC-2/NC-4 - The guided consultation, practitioner side.</summary>
public interface IConsultationQueryService
{
    /// <summary>NC-2. The consultation with what each saved step produced, or null.</summary>
    Task<ConsultationDetails?> Handle(GetConsultationByIdQuery query, CancellationToken cancellationToken = default);

    /// <summary>NC-2. PAC-1.C: the consultation in progress of the patient, or null.</summary>
    Task<ConsultationDetails?> Handle(GetInProgressConsultationByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>RM-1. The consultations in progress of several patients, without their steps; empty, never null.</summary>
    Task<IReadOnlyList<Consultation>> Handle(GetInProgressConsultationsByPatientIdsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>NC-2. PT25 "Anteriores" and PAC-3: newest first; empty, never null.</summary>
    Task<IReadOnlyList<ConsultationSummary>> Handle(GetConsultationsByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     The deterministic EV-3 suggestion (source "Rule"), or null when the consultation does not exist or has no
    ///     measurement yet. The endpoint answers through <c>IConsultationAiCommandService</c> (IA-6), which falls
    ///     back to this same suggestion.
    /// </summary>
    Task<DiagnosisSuggestion?> Handle(GetConsultationDiagnosisSuggestionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     NC-6. Guideline codes for the diagnosis of step 2, from the fixed table (IA-7 fallback). Empty when the
    ///     consultation does not exist or has no coded diagnosis yet.
    /// </summary>
    Task<IReadOnlyList<string>> Handle(GetConsultationGuidelineSuggestionsQuery query,
        CancellationToken cancellationToken = default);
}
