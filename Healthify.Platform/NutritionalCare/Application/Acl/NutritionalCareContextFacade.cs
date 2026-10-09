using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;

namespace Healthify.Platform.NutritionalCare.Application.Acl;

/// <inheritdoc cref="INutritionalCareContextFacade" />
public class NutritionalCareContextFacade(
    INutritionPlanQueryService planQueryService,
    IReviewItemQueryService reviewItemQueryService,
    IPatientBaselineQueryService baselineQueryService,
    IConsultationQueryService consultationQueryService,
    IClinicalDateProvider clinicalDate,
    INutritionalAssessmentQueryService assessmentQueryService,
    INutritionalDiagnosisQueryService diagnosisQueryService) : INutritionalCareContextFacade
{
    private static readonly ConsultationState CompletedState = new(ConsultationState.Completed);

    public async Task<ActiveTargetsItem?> GetActiveTargetsByPatientId(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var plan = await planQueryService.Handle(new GetActivePlanByPatientIdQuery(patientId), ct);
            if (plan?.PrescribedTargets is null || !plan.IsPublished) return null;

            var targets = plan.PrescribedTargets;

            // Only the published contract crosses. No diagnosis, no calculation basis.
            return new ActiveTargetsItem(
                plan.PatientId,
                plan.Version,
                plan.PublishedAt!.Value,
                targets.EnergyKcal,
                targets.ProteinG,
                targets.CarbG,
                targets.FatG,
                plan.Guidelines.Select(g => g.ToString()).ToList(),
                plan.Restrictions,
                plan.Guidelines.Select(g => new ActiveGuidelineItem(g.Code, g.Custom)).ToList(),
                plan.LegacyRestrictions.ToList());
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> GetOpenReviewItemCount(int practitionerId, CancellationToken ct = default)
    {
        try
        {
            return await reviewItemQueryService.CountOpen(practitionerId, ct);
        }
        catch
        {
            return 0;
        }
    }

    public async Task<bool> HasConsistencyEscalation(int patientId, CancellationToken ct = default)
    {
        try
        {
            return await reviewItemQueryService.Handle(
                new GetReviewItemExistenceQuery(patientId, SignalType.ConsistencyEscalation), ct);
        }
        catch
        {
            return false;
        }
    }

    public async Task<PatientBaselineSummaryItem?> GetBaselineSummary(int patientId, CancellationToken ct = default)
    {
        try
        {
            var baseline = await baselineQueryService.Handle(new GetPatientBaselineByPatientIdQuery(patientId), ct);
            return baseline is null
                ? null
                : new PatientBaselineSummaryItem(baseline.PatientId, baseline.BiologicalSex.Value,
                    baseline.AgeAt(clinicalDate.Today()), baseline.Height.Value,
                    baseline.Conditions.Select(c => c.Value).ToList());
        }
        catch
        {
            return null;
        }
    }

    public async Task<ConsultationInProgressItem?> GetConsultationInProgress(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var details = await consultationQueryService.Handle(
                new GetInProgressConsultationByPatientIdQuery(patientId), ct);
            if (details is null) return null;

            // Only where the practitioner left off. What the steps produced stays here.
            var consultation = details.Consultation;
            return new ConsultationInProgressItem(consultation.Id.Value, consultation.CurrentStep.Number,
                consultation.CurrentStep.Value, consultation.LastSavedAt);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<int, PatientCareStatusItem>> GetCareStatusByPatientIds(
        int practitionerId, IEnumerable<int> patientIds, CancellationToken ct = default)
    {
        try
        {
            var ids = patientIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, PatientCareStatusItem>();

            var withBaseline = (await baselineQueryService.Handle(new GetPatientBaselinesByPatientIdsQuery(ids), ct))
                .Select(b => b.PatientId).ToHashSet();
            // Only a published contract counts as a plan, as in GetActiveTargetsByPatientId.
            var activeVersions = (await planQueryService.Handle(new GetActivePlansByPatientIdsQuery(ids), ct))
                .Where(p => p.IsPublished && p.PrescribedTargets is not null)
                .GroupBy(p => p.PatientId)
                .ToDictionary(g => g.Key, g => g.Max(p => p.Version));
            var inProgress = (await consultationQueryService.Handle(
                    new GetInProgressConsultationsByPatientIdsQuery(ids), ct))
                .Select(c => c.PatientId).ToHashSet();
            var withOpenItem = (await reviewItemQueryService.Handle(
                    new GetOpenReviewItemsByPractitionerIdQuery(practitionerId), ct))
                .Select(r => r.PatientId).ToHashSet();

            return ids.ToDictionary(id => id, id => new PatientCareStatusItem(id, withBaseline.Contains(id),
                activeVersions.TryGetValue(id, out var version) ? version : null, inProgress.Contains(id),
                withOpenItem.Contains(id)));
        }
        catch
        {
            return new Dictionary<int, PatientCareStatusItem>();
        }
    }

    public async Task<DateTimeOffset?> GetLastCompletedConsultationAt(int patientId, CancellationToken ct = default)
    {
        try
        {
            var completed = await consultationQueryService.Handle(
                new GetConsultationsByPatientIdQuery(patientId, CompletedState), ct);
            return completed.Max(c => c.CompletedAt);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<CompletedConsultationItem>> GetCompletedConsultations(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var completed = await consultationQueryService.Handle(
                new GetConsultationsByPatientIdQuery(patientId, CompletedState), ct);
            return completed
                .Where(c => c.CompletedAt is not null)
                .OrderByDescending(c => c.CompletedAt)
                .Select(c => new CompletedConsultationItem(c.ConsultationId, c.CompletedAt!.Value, c.PlanVersion,
                    c.IsFirstConsultation))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<ClinicalMeasurementItem?> GetLatestClinicalMeasurement(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var latest = (await EvaluatedMeasurements(patientId, ct)).LastOrDefault();
            return latest is null
                ? null
                : new ClinicalMeasurementItem(latest.Measurement.TakenAt, latest.Measurement.WeightKg,
                    latest.Measurement.ProtocolChecks ?? [], latest.Measurement.Protocol.Value);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ClinicalEvaluationItem>> GetClinicalEvaluations(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var evaluated = await EvaluatedMeasurements(patientId, ct);
            return evaluated
                .Select((e, i) => new ClinicalEvaluationItem(e.AssessmentId, e.Measurement.TakenAt,
                    e.Measurement.WeightKg, e.Measurement.BmiKgM2, e.Measurement.BmiCategory,
                    e.Measurement.WaistCircumferenceCm, i == 0))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<ActiveDiagnosisItem?> GetActiveDiagnosis(int patientId, CancellationToken ct = default)
    {
        try
        {
            var diagnosis = await diagnosisQueryService.Handle(new GetActiveDiagnosisByPatientIdQuery(patientId), ct);
            // Pending and discarded diagnoses are not active (NC-7); the query already leaves them out.
            return diagnosis is null || !diagnosis.IsActive
                ? null
                : new ActiveDiagnosisItem(diagnosis.Id.Value, diagnosis.Code?.Value, diagnosis.Statement,
                    diagnosis.IssuedAt);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     RM-4. The evaluations in force, oldest first: a closed stand-alone assessment, or the assessment of a
    ///     completed consultation (one in progress or discarded is not an evaluation yet), without the ones a later
    ///     assessment corrected; each with its latest measurement.
    /// </summary>
    private async Task<List<EvaluatedMeasurement>> EvaluatedMeasurements(int patientId, CancellationToken ct)
    {
        var assessments = (await assessmentQueryService.Handle(new GetAssessmentsByPatientIdQuery(patientId), ct))
            .ToList();
        var completed = (await consultationQueryService.Handle(
                new GetConsultationsByPatientIdQuery(patientId, CompletedState), ct))
            .Select(c => c.ConsultationId)
            .ToHashSet();
        var corrected = assessments.Where(a => a.SupersedesAssessmentId is not null)
            .Select(a => a.SupersedesAssessmentId!.Value)
            .ToHashSet();

        return assessments
            .Where(a => a.ConsultationId is { } consultationId ? completed.Contains(consultationId) : a.IsClosed)
            .Where(a => !corrected.Contains(a.Id.Value))
            .Where(a => a.LatestMeasurement is not null)
            .Select(a => new EvaluatedMeasurement(a.Id.Value, a.LatestMeasurement!))
            .OrderBy(e => e.Measurement.TakenAt)
            .ToList();
    }

    private sealed record EvaluatedMeasurement(int AssessmentId, ClinicalMeasurement Measurement);
}
