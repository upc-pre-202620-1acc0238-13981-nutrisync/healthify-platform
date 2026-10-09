using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-2/NC-7 acceptance test: a patient goes through a first and a second complete consultation with the real
///     command services, the real publication policy and the real Intake cache. The second one leaves diagnosis 2
///     active and 1 superseded, version 2 active and 1 superseded, the cache on version 2, and at no save were
///     there two active at once. Repeating the publication with the same Idempotency-Key does not create v3.
/// </summary>
/// <remarks>
///     The repositories are in memory; <see cref="SecondConsultationMySqlTests" /> runs the same flow against
///     MySQL.
/// </remarks>
public class SecondConsultationInMemoryTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly InMemoryNutritionalCare _care = new(Today);

    public SecondConsultationInMemoryTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
    }

    [Fact]
    public async Task The_second_complete_consultation_replaces_diagnosis_and_version_never_two_active_at_once()
    {
        var first = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        Assert.Equal(1, Assert.Single(_care.Caches).PlanVersion);

        var second = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 74.2m,
            DiagnosisCode.OverweightGradeI, "second-publication");

        // Diagnosis 2 active, diagnosis 1 superseded.
        Assert.True(second.Diagnosis.IsActive);
        Assert.False(first.Diagnosis.IsActive);
        Assert.NotNull(first.Diagnosis.SupersededAt);
        Assert.Same(second.Diagnosis, Assert.Single(_care.Diagnoses, d => d.IsActive));

        // Version 2 active, version 1 superseded and kept.
        Assert.Equal((1, 2), (first.Plan.Version, second.Plan.Version));
        Assert.True(second.Plan.IsActive);
        Assert.False(first.Plan.IsActive);
        Assert.True(first.Plan.IsPublished);
        Assert.NotNull(first.Plan.SupersededAt);
        Assert.Same(second.Plan, Assert.Single(_care.Plans, p => p.IsActive));
        Assert.Null(first.Plan.ChangeReason);
        Assert.Equal("Nueva consulta del 18 sept. 2026", second.Plan.ChangeReason!.Value);

        // The patient received version 2.
        var cache = Assert.Single(_care.Caches);
        Assert.Equal(2, cache.PlanVersion);
        Assert.Equal(second.Plan.PrescribedTargets!.EnergyKcal, cache.EnergyKcal);

        // Never two active at once, at any save.
        Assert.Empty(_care.Violations);
        Assert.True(first.Consultation.IsFirstConsultation);
        Assert.False(second.Consultation.IsFirstConsultation);
        Assert.All(_care.Consultations, c => Assert.Equal(ConsultationState.Completed, c.State.Value));
        Assert.Equal([2, 1], _care.Consultations.Select(c => c.PublishedPlanVersion!.Value).OrderDescending());
    }

    [Fact]
    public async Task Until_step_four_the_first_consultation_keeps_the_active_diagnosis_and_version()
    {
        var first = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");

        var consultationId = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 74.2m);
        var pending = await ConsultationFlow.DiagnoseAsync(_care.Consultation, consultationId, PractitionerId,
            DiagnosisCode.OverweightGradeI);
        var draft = await ConsultationFlow.TargetsAsync(_care.Consultation, consultationId, PractitionerId);

        Assert.True(pending.IsPendingFor(consultationId));
        Assert.Same(first.Diagnosis, Assert.Single(_care.Diagnoses, d => d.IsActive));
        Assert.Same(first.Plan, Assert.Single(_care.Plans, p => p.IsActive));
        Assert.False(draft.IsPublished);
        Assert.Equal(1, Assert.Single(_care.Caches).PlanVersion);
        Assert.Empty(_care.Violations);
    }

    [Fact]
    public async Task Repeating_the_publication_with_the_same_key_does_not_create_v3()
    {
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        var second = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 74.2m,
            DiagnosisCode.OverweightGradeI, "second-publication");
        var events = Fakes.Published(_care.Mediator).Count;
        var saves = _care.UnitOfWork.Saves;

        var replay = ConsultationFlow.Ok(await ConsultationFlow.PublishAsync(_care.Consultation,
            second.Consultation.Id.Value, PractitionerId, "second-publication"), "replay");

        Assert.True(replay.Replayed);
        Assert.Same(second.Plan, replay.Plan);
        Assert.Same(second.Diagnosis, replay.Diagnosis);
        Assert.Equal([1, 2], _care.Plans.Select(p => p.Version).Order());
        Assert.Equal(saves, _care.UnitOfWork.Saves);
        Assert.Equal(events, Fakes.Published(_care.Mediator).Count);
        Assert.Equal(2, Assert.Single(_care.Caches).PlanVersion);
        Assert.Empty(_care.Violations);
    }

    [Fact]
    public async Task Another_key_on_a_completed_consultation_is_a_conflict_and_publishes_nothing()
    {
        var first = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        var events = Fakes.Published(_care.Mediator).Count;

        var withAnotherKey = await ConsultationFlow.PublishAsync(_care.Consultation, first.Consultation.Id.Value,
            PractitionerId, "another-key");
        var withoutKey = await ConsultationFlow.PublishAsync(_care.Consultation, first.Consultation.Id.Value,
            PractitionerId, null);

        AssertFailure(withAnotherKey, NutritionalCareError.ConsultationNotInProgress);
        AssertFailure(withoutKey, NutritionalCareError.ConsultationNotInProgress);
        Assert.Single(_care.Plans);
        Assert.Equal(events, Fakes.Published(_care.Mediator).Count);
    }

    [Fact]
    public async Task The_publication_reaches_the_patient_through_the_existing_fan_out_after_the_commit()
    {
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");

        var published = Fakes.Published(_care.Mediator);
        var targets = Assert.Single(published.OfType<ActiveTargetsUpdated>());
        Assert.Equal(1, targets.PlanVersion);
        Assert.Contains(targets.GuidelineItems!, g => g.Code == Guideline.ReduceSalt);
        Assert.Contains(targets.GuidelineItems!, g => g.Custom == "Caminar 20 minutos");
        Assert.Equal([DietaryRestriction.LactoseFree], targets.Restrictions);
        Assert.Single(published.OfType<ConsultationCompleted>());
    }

    private static void AssertFailure(Result<ConsultationPublicationOutcome, NutritionalCareError> result,
        NutritionalCareError expected)
    {
        var failure = Assert.IsType<Result<ConsultationPublicationOutcome, NutritionalCareError>.Failure>(result);
        Assert.Equal(expected, failure.Error);
    }
}
