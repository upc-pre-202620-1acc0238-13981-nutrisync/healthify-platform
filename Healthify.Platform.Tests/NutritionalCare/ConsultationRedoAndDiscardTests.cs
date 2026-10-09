using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-2/NC-7. What stays active when the practitioner goes back and repeats a step, or discards the
///     consultation. The diagnosis of step 2 is pending until step 4, so the active diagnosis and version of the
///     first consultation change only when the second one publishes, and never when it is discarded.
/// </summary>
public class ConsultationRedoAndDiscardTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly InMemoryNutritionalCare _care = new(Today);
    private readonly ConsultationPublicationOutcome _first;

    public ConsultationRedoAndDiscardTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
        _first = ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication").GetAwaiter().GetResult();
    }

    /// <summary>Case (a): the practitioner repeats step 1 and diagnoses again.</summary>
    [Fact]
    public async Task Repeating_step_one_discards_the_pending_diagnosis_and_the_first_stays_active_until_publication()
    {
        var id = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, id, PractitionerId, 75m);
        var d2 = await ConsultationFlow.DiagnoseAsync(_care.Consultation, id, PractitionerId,
            DiagnosisCode.OverweightGradeI);

        await ConsultationFlow.MeasureAsync(_care.Consultation, id, PractitionerId, 74.2m);

        Assert.True(d2.IsDiscarded);
        Assert.False(d2.IsActive);
        Assert.Null(Consultation(id).DiagnosisId);
        Assert.Equal(ConsultationStep.Diagnosis, Consultation(id).CurrentStep.Value);
        Assert.Same(_first.Diagnosis, ActiveDiagnosis());

        var d3 = await ConsultationFlow.DiagnoseAsync(_care.Consultation, id, PractitionerId,
            DiagnosisCode.OverweightGradeI);
        Assert.True(d3.IsPendingFor(id));
        Assert.Same(_first.Diagnosis, ActiveDiagnosis());

        await ConsultationFlow.TargetsAsync(_care.Consultation, id, PractitionerId);
        var published = ConsultationFlow.Ok(
            await ConsultationFlow.PublishAsync(_care.Consultation, id, PractitionerId, "second"), "publication");

        Assert.Same(d3, published.Diagnosis);
        Assert.Same(d3, ActiveDiagnosis());
        Assert.NotNull(_first.Diagnosis.SupersededAt);
        Assert.Null(d2.SupersededAt);
        Assert.True(d2.IsDiscarded);
        Assert.Empty(_care.Violations);
    }

    /// <summary>Case (a), with targets already prescribed on the diagnosis that was issued again.</summary>
    [Fact]
    public async Task A_draft_prescribed_on_a_replaced_diagnosis_is_discarded_and_rebuilt_with_the_same_version()
    {
        var id = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, id, PractitionerId, 75m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, id, PractitionerId, DiagnosisCode.OverweightGradeI);
        var staleDraft = await ConsultationFlow.TargetsAsync(_care.Consultation, id, PractitionerId);

        await ConsultationFlow.MeasureAsync(_care.Consultation, id, PractitionerId, 74.2m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, id, PractitionerId, DiagnosisCode.OverweightGradeI);

        // The targets read the replaced diagnosis: the publication sends the practitioner back to step 3.
        AssertFailure(await ConsultationFlow.PublishAsync(_care.Consultation, id, PractitionerId, "second"),
            NutritionalCareError.ConsultationStepOutOfOrder);

        var draft = await ConsultationFlow.TargetsAsync(_care.Consultation, id, PractitionerId);

        Assert.True(staleDraft.IsDiscarded);
        Assert.NotSame(staleDraft, draft);
        Assert.Equal(2, staleDraft.Version);
        Assert.Equal(2, draft.Version);
        Assert.Equal(draft.Id.Value, Consultation(id).PlanId);

        var published = ConsultationFlow.Ok(
            await ConsultationFlow.PublishAsync(_care.Consultation, id, PractitionerId, "second"), "publication");
        Assert.Same(draft, published.Plan);
        Assert.Same(draft, Assert.Single(_care.Plans, p => p.IsActive));
        Assert.False(staleDraft.IsPublished);
        Assert.Empty(_care.Violations);
    }

    /// <summary>Case (b): the practitioner discards the consultation after step 2.</summary>
    [Fact]
    public async Task Discarding_after_step_two_leaves_the_first_diagnosis_and_version_active()
    {
        var id = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, id, PractitionerId, 74.2m);
        var d2 = await ConsultationFlow.DiagnoseAsync(_care.Consultation, id, PractitionerId,
            DiagnosisCode.OverweightGradeI);

        ConsultationFlow.Ok(await _care.Consultation.Handle(new AbandonConsultationCommand(id, PractitionerId)),
            "discard");

        Assert.Equal(ConsultationState.Abandoned, Consultation(id).State.Value);
        Assert.NotNull(Consultation(id).AbandonedAt);
        Assert.True(d2.IsDiscarded);
        Assert.Same(_first.Diagnosis, ActiveDiagnosis());
        Assert.Same(_first.Plan, Assert.Single(_care.Plans, p => p.IsActive));
        // The assessment of step 1 stays as history.
        Assert.Equal(2, _care.Assessments.Count);
        Assert.Equal(1, Assert.Single(_care.Caches).PlanVersion);
        Assert.Empty(_care.Violations);
    }

    [Fact]
    public async Task Discarding_after_step_three_discards_the_draft_and_the_next_consultation_reuses_its_version()
    {
        var id = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, id, PractitionerId, 74.2m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, id, PractitionerId, DiagnosisCode.OverweightGradeI);
        var draft = await ConsultationFlow.TargetsAsync(_care.Consultation, id, PractitionerId);

        ConsultationFlow.Ok(await _care.Consultation.Handle(new AbandonConsultationCommand(id, PractitionerId)),
            "discard");
        Assert.True(draft.IsDiscarded);
        Assert.Same(_first.Plan, Assert.Single(_care.Plans, p => p.IsActive));

        var next = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 74m,
            DiagnosisCode.OverweightGradeI, "third");

        Assert.Equal(2, next.Plan.Version);
        Assert.False(next.Consultation.IsFirstConsultation);
        Assert.Empty(_care.Violations);
    }

    [Fact]
    public async Task A_completed_or_discarded_consultation_cannot_be_discarded()
    {
        var id = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        ConsultationFlow.Ok(await _care.Consultation.Handle(new AbandonConsultationCommand(id, PractitionerId)),
            "discard");

        AssertFailure(await _care.Consultation.Handle(new AbandonConsultationCommand(id, PractitionerId)),
            NutritionalCareError.ConsultationNotInProgress);
        AssertFailure(await _care.Consultation.Handle(
                new AbandonConsultationCommand(_first.Consultation.Id.Value, PractitionerId)),
            NutritionalCareError.ConsultationNotInProgress);
    }

    [Fact]
    public async Task Only_the_practitioner_leading_it_discards_a_consultation()
    {
        var id = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);

        AssertFailure(await _care.Consultation.Handle(new AbandonConsultationCommand(id, 99)),
            NutritionalCareError.PractitionerOnly);
        AssertFailure(await _care.Consultation.Handle(new AbandonConsultationCommand(999, PractitionerId)),
            NutritionalCareError.ConsultationNotFound);
        Assert.True(Consultation(id).IsInProgress);
    }

    private Consultation Consultation(int id)
    {
        return _care.Consultations.Single(c => c.Id.Value == id);
    }

    private NutritionalDiagnosis ActiveDiagnosis()
    {
        return Assert.Single(_care.Diagnoses, d => d.IsActive);
    }

    private static void AssertFailure<T>(Result<T, NutritionalCareError> result, NutritionalCareError expected)
    {
        var failure = Assert.IsType<Result<T, NutritionalCareError>.Failure>(result);
        Assert.Equal(expected, failure.Error);
    }
}
