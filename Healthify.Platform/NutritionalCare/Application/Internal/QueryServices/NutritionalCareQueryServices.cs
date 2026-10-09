using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Application.Internal.QueryServices;

public class NutritionalAssessmentQueryService(INutritionalAssessmentRepository repository)
    : INutritionalAssessmentQueryService
{
    public async Task<NutritionalAssessment?> Handle(GetAssessmentByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByIdAsync(query.AssessmentId, cancellationToken);
    }

    public async Task<IEnumerable<NutritionalAssessment>> Handle(GetAssessmentsByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListByPatientIdAsync(query.PatientId, cancellationToken);
    }
}

public class PatientBaselineQueryService(IPatientBaselineRepository repository) : IPatientBaselineQueryService
{
    public async Task<PatientBaseline?> Handle(GetPatientBaselineByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByPatientIdAsync(query.PatientId, cancellationToken);
    }

    public async Task<IReadOnlyList<PatientBaseline>> Handle(GetPatientBaselinesByPatientIdsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListByPatientIdsAsync(query.PatientIds, cancellationToken);
    }
}

public class NutritionalDiagnosisQueryService(INutritionalDiagnosisRepository repository)
    : INutritionalDiagnosisQueryService
{
    public async Task<NutritionalDiagnosis?> Handle(GetActiveDiagnosisByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindActiveByPatientIdAsync(query.PatientId, cancellationToken);
    }
}

public class NutritionPlanQueryService(INutritionPlanRepository repository) : INutritionPlanQueryService
{
    public async Task<NutritionPlan?> Handle(GetPlanByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByIdAsync(query.PlanId, cancellationToken);
    }

    public async Task<NutritionPlan?> Handle(GetActivePlanByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindActiveByPatientIdAsync(query.PatientId, cancellationToken);
    }

    public async Task<IReadOnlyList<NutritionPlan>> Handle(GetActivePlansByPatientIdsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListActiveByPatientIdsAsync(query.PatientIds, cancellationToken);
    }

    public async Task<IEnumerable<NutritionPlan>> Handle(GetPlansByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListByPatientIdAsync(query.PatientId, cancellationToken);
    }

    public async Task<IReadOnlyList<PatientPlanVersion>> Handle(GetPatientPlanVersionsQuery query,
        CancellationToken cancellationToken = default)
    {
        // Drafts never reached the patient: only published versions, oldest first to compare each with the one
        // before it.
        var published = (await repository.ListByPatientIdAsync(query.PatientId, cancellationToken))
            .Where(p => p.IsPublished && p.PrescribedTargets is not null)
            .OrderBy(p => p.Version)
            .ToList();

        var versions = new List<PatientPlanVersion>(published.Count);
        for (var i = 0; i < published.Count; i++)
        {
            var plan = published[i];
            var targets = plan.PrescribedTargets!;
            // DECISIÓN NC-8: versions published before NC-8 have no stored summary; it is calculated here with the
            // same domain service, on read and without writing it, so their rows are not rewritten.
            var changes = plan.ChangesFromPrevious ?? PlanVersionDiff.Compare(i == 0 ? null : published[i - 1], plan);
            versions.Add(new PatientPlanVersion(plan.Version, plan.PublishedAt!.Value, plan.IsActive,
                targets.EnergyKcal, targets.ProteinG, targets.CarbG, targets.FatG, plan.Guidelines, plan.Restrictions,
                plan.LegacyRestrictions, changes, plan.PatientMessage));
        }

        versions.Reverse();
        return versions;
    }
}

public class ReviewItemQueryService(
    IReviewItemRepository repository,
    IIamContextFacade iamContextFacade,
    IPlanProposalGenerationQueue planProposalGenerationQueue,
    INutritionPlanRepository planRepository) : IReviewItemQueryService
{
    public async Task<ReviewItem?> Handle(GetReviewItemByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByIdAsync(query.ReviewItemId, cancellationToken);
    }

    public async Task<IEnumerable<ReviewItem>> Handle(GetOpenReviewItemsByPractitionerIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListOpenByPractitionerIdAsync(query.PractitionerId, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewInboxEntry>> Handle(GetReviewItemsByPractitionerIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var items = (await repository.ListByPractitionerIdAndStateAsync(query.PractitionerId, query.State,
            cancellationToken)).ToList();
        return await WithNamesAsync(items, cancellationToken);
    }

    public async Task<ReviewInboxEntry?> Handle(GetReviewInboxEntryByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var item = await repository.FindByIdAsync(query.ReviewItemId, cancellationToken);
        return item is null ? null : (await WithNamesAsync([item], cancellationToken))[0];
    }

    /// <summary>NC-11/IAM-1: the names in one read for all the items. The facade degrades to an empty map.</summary>
    private async Task<IReadOnlyList<ReviewInboxEntry>> WithNamesAsync(IReadOnlyList<ReviewItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0) return [];
        var users = await iamContextFacade.GetUsersByIds(items.Select(i => i.PatientId).Distinct(),
            cancellationToken);
        return items
            .Select(i => new ReviewInboxEntry(i,
                users.TryGetValue(i.PatientId, out var user) && user.FullName.Length > 0 ? user.FullName : null))
            .ToList();
    }

    public async Task<PlanProposalLookup?> Handle(GetPlanProposalByReviewItemIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var item = await repository.FindByIdAsync(query.ReviewItemId, cancellationToken);
        if (item is null) return null;
        var current = item.HasPlanProposal
            ? await planRepository.FindActiveByPatientIdAsync(item.PatientId, cancellationToken)
            : null;
        return new PlanProposalLookup(item,
            !item.HasPlanProposal && planProposalGenerationQueue.IsPending(item.Id.Value),
            current?.PrescribedTargets?.EnergyKcal);
    }

    public async Task<int> CountOpen(int practitionerId, CancellationToken cancellationToken = default)
    {
        return await repository.CountOpenByPractitionerIdAsync(practitionerId, cancellationToken);
    }

    public async Task<bool> Handle(GetReviewItemExistenceQuery query, CancellationToken cancellationToken = default)
    {
        return await repository.ExistsForPatientAndSignalTypeAsync(query.PatientId, new SignalType(query.SignalType),
            cancellationToken);
    }
}

public class ConsultationQueryService(
    IConsultationRepository consultationRepository,
    INutritionalAssessmentRepository assessmentRepository,
    INutritionalDiagnosisRepository diagnosisRepository,
    IDefaultGuidelinesProvider defaultGuidelines,
    INutritionPlanRepository planRepository,
    IMonitoringContextFacade monitoringContextFacade) : IConsultationQueryService
{
    public async Task<ConsultationDetails?> Handle(GetConsultationByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var consultation = await consultationRepository.FindByIdAsync(query.ConsultationId, cancellationToken);
        return consultation is null ? null : await DetailsOf(consultation, cancellationToken);
    }

    public async Task<ConsultationDetails?> Handle(GetInProgressConsultationByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var consultation = await consultationRepository.FindInProgressByPatientIdAsync(query.PatientId,
            cancellationToken);
        return consultation is null ? null : await DetailsOf(consultation, cancellationToken);
    }

    public async Task<IReadOnlyList<Consultation>> Handle(GetInProgressConsultationsByPatientIdsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await consultationRepository.ListInProgressByPatientIdsAsync(query.PatientIds, cancellationToken);
    }

    public async Task<IReadOnlyList<ConsultationSummary>> Handle(GetConsultationsByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var consultations = (await consultationRepository.ListByPatientIdAsync(query.PatientId, query.State,
            cancellationToken)).ToList();
        if (consultations.Count == 0) return [];

        // One read for every assessment of the patient instead of one per consultation.
        var assessments = (await assessmentRepository.ListByPatientIdAsync(query.PatientId, cancellationToken))
            .ToDictionary(a => a.Id.Value);
        return consultations.Select(c =>
        {
            var measurement = c.AssessmentId is { } id && assessments.TryGetValue(id, out var assessment)
                ? assessment.LatestMeasurement
                : null;
            return new ConsultationSummary(c.Id.Value, c.State.Value, c.StartedAt, c.CompletedAt,
                c.PublishedPlanVersion, c.IsFirstConsultation, measurement?.WeightKg, measurement?.BmiKgM2,
                measurement?.WaistCircumferenceCm);
        }).ToList();
    }

    private async Task<ConsultationDetails> DetailsOf(Consultation consultation, CancellationToken cancellationToken)
    {
        var assessment = consultation.AssessmentId is { } assessmentId
            ? await assessmentRepository.FindByIdAsync(assessmentId, cancellationToken)
            : null;
        var diagnosis = consultation.DiagnosisId is { } diagnosisId
            ? await diagnosisRepository.FindByIdAsync(diagnosisId, cancellationToken)
            : null;
        var plan = consultation.PlanId is { } planId
            ? await planRepository.FindByIdAsync(planId, cancellationToken)
            : null;

        // NC-6: while the draft waits for EV-5, the practitioner sees the legacy restrictions of the version in
        // force to map them to a code.
        IReadOnlyList<string> legacy = [];
        if (consultation.IsInProgress && plan is not null)
            legacy = (await planRepository.FindActiveByPatientIdAsync(consultation.PatientId, cancellationToken))
                ?.LegacyRestrictions ?? [];

        // MA-4. EV-2 "Antes de la consulta, … contó": the check in of the visit still open between this patient and
        // this practitioner. Only while the consultation is in progress: once published, that visit is completed
        // and its check in belongs to history. The facade degrades to null.
        var checkIn = consultation.IsInProgress
            ? await monitoringContextFacade.GetLatestCheckInForPatient(consultation.PatientId,
                consultation.PractitionerId, cancellationToken)
            : null;

        return new ConsultationDetails(consultation, assessment, diagnosis, plan, legacy, checkIn);
    }

    public async Task<IReadOnlyList<string>> Handle(GetConsultationGuidelineSuggestionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var consultation = await consultationRepository.FindByIdAsync(query.ConsultationId, cancellationToken);
        if (consultation?.DiagnosisId is null) return [];

        var diagnosis = await diagnosisRepository.FindByIdAsync(consultation.DiagnosisId.Value, cancellationToken);
        // IA-7: the AI suggestion is ConsultationAiCommandService; this table is its fallback.
        return diagnosis?.Code is null ? [] : defaultGuidelines.For(diagnosis.Code);
    }

    public async Task<DiagnosisSuggestion?> Handle(GetConsultationDiagnosisSuggestionQuery query,
        CancellationToken cancellationToken = default)
    {
        var consultation = await consultationRepository.FindByIdAsync(query.ConsultationId, cancellationToken);
        if (consultation?.AssessmentId is null) return null;

        var assessment = await assessmentRepository.FindByIdAsync(consultation.AssessmentId.Value, cancellationToken);
        var measurement = assessment?.LatestMeasurement;
        if (measurement is null) return null;

        // NC-4 fallback: the WHO category of the body mass index of step 1, with a rationale written from the
        // same measurement. EV-3 is never left empty. IA-6 (the AI suggestion) is ConsultationAiCommandService,
        // which answers this same suggestion whenever the AI does not.
        var code = DiagnosisCode.FromBodyMassIndex(measurement.BmiKgM2);
        var rationale = ClinicalRationale.FromMeasurement(measurement.BmiKgM2, measurement.WaistCircumferenceCm,
            measurement.BodyFatPercentage);
        return new DiagnosisSuggestion(code.Value, rationale.Value, DiagnosisSuggestion.RuleSource, null);
    }
}
