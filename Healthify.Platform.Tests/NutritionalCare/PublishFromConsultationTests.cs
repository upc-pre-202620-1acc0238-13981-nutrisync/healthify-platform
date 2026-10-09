using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Model.Events;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-7. Step 4 of the guided consultation, PublishFromConsultation: in one transaction the pending diagnosis
///     of the consultation replaces the active one, the prescribed draft replaces the version in force, and the
///     consultation closes. Never two active at once; events only after the commit.
/// </summary>
public class PublishFromConsultationTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int ConsultationId = 41;
    private const int AssessmentId = 101;
    private const int PendingDiagnosisId = 2;
    private const int DraftPlanId = 401;
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly IConsultationRepository _consultations = Substitute.For<IConsultationRepository>();
    private readonly INutritionalDiagnosisRepository _diagnoses = Substitute.For<INutritionalDiagnosisRepository>();
    private readonly INutritionPlanRepository _plans = Substitute.For<INutritionPlanRepository>();
    private readonly TransactionalUnitOfWork _unitOfWork = new();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();

    private readonly ICareRelationshipContextFacade _careRelationship =
        Substitute.For<ICareRelationshipContextFacade>();

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private readonly NutritionalDiagnosis _activeDiagnosis;
    private readonly NutritionalDiagnosis _pending;
    private readonly NutritionPlan _activePlan;
    private readonly NutritionPlan _draft;
    private readonly Consultation _consultation;

    public PublishFromConsultationTests()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);

        var baseline = ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female",
            168m, Today);
        // The first consultation: its diagnosis and its version 1 are the active ones.
        var older = ConsultationScenario.MeasuredAssessment(90, 40, PractitionerId, baseline, Today.AddMonths(-6), 80m);
        _activeDiagnosis = ConsultationScenario.Diagnosis(1, older, DiagnosisCode.ObesityGradeI);
        _activePlan = PlanScenario.PublishedBeforeNc6(400, PatientId, PractitionerId, 1, 1, ["Prioriza vegetales"],
            ["Vegan", "Sin cebolla"]);

        // The second consultation, at step 4.
        var assessment = ConsultationScenario.MeasuredAssessment(AssessmentId, ConsultationId, PractitionerId,
            baseline, Today, 74.2m);
        _pending = ConsultationScenario.PendingDiagnosis(PendingDiagnosisId, assessment,
            DiagnosisCode.OverweightGradeI, ConsultationId);
        _draft = PlanScenario.Prescribed(DraftPlanId, PatientId, PractitionerId, PendingDiagnosisId, 2);
        _consultation = ConsultationScenario.AtPublication(ConsultationId, PatientId, PractitionerId, AssessmentId,
            PendingDiagnosisId, DraftPlanId);

        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns(_consultation);
        _diagnoses.FindByIdAsync(PendingDiagnosisId, Arg.Any<CancellationToken>()).Returns(_pending);
        _diagnoses.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(_activeDiagnosis);
        _plans.FindByIdAsync(DraftPlanId, Arg.Any<CancellationToken>()).Returns(_draft);
        _plans.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(_activePlan);
    }

    [Fact]
    public async Task The_second_consultation_replaces_the_active_diagnosis_and_version_and_closes()
    {
        var outcome = Success(await CreateService().Handle(Publish()));

        Assert.False(_activeDiagnosis.IsActive);
        Assert.NotNull(_activeDiagnosis.SupersededAt);
        Assert.True(_pending.IsActive);
        Assert.Null(_pending.PendingConsultationId);

        Assert.False(_activePlan.IsActive);
        Assert.NotNull(_activePlan.SupersededAt);
        Assert.True(_draft.IsActive);
        Assert.True(_draft.IsPublished);

        // Never two active at once.
        Assert.Single(new[] { _activeDiagnosis, _pending }, d => d.IsActive);
        Assert.Single(new[] { _activePlan, _draft }, p => p.IsActive);

        Assert.Equal(ConsultationState.Completed, outcome.Consultation.State.Value);
        Assert.Equal(2, outcome.Consultation.PublishedPlanVersion);
        Assert.NotNull(outcome.Consultation.CompletedAt);
        Assert.Same(_draft, outcome.Plan);
        Assert.Same(_pending, outcome.Diagnosis);
        Assert.False(outcome.Replayed);
    }

    [Fact]
    public async Task Everything_is_one_save_in_one_transaction_and_events_follow_the_commit_in_order()
    {
        var commitsWhenPublished = new List<int>();
        Track<NutritionalDiagnosisSuperseded>(commitsWhenPublished);
        Track<NutritionalDiagnosisIssued>(commitsWhenPublished);
        Track<PlanVersionSuperseded>(commitsWhenPublished);
        Track<NutritionPlanPublished>(commitsWhenPublished);
        Track<ConsultationCompleted>(commitsWhenPublished);

        await CreateService().Handle(Publish());

        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Equal(1, _unitOfWork.DurableSaves);
        var events = Fakes.Published(_mediator);
        Assert.Equal(
            [
                typeof(NutritionalDiagnosisSuperseded), typeof(NutritionalDiagnosisIssued),
                typeof(PlanVersionSuperseded), typeof(NutritionPlanPublished), typeof(ConsultationCompleted)
            ],
            events.Select(e => e.GetType()).ToArray());
        Assert.Equal([1, 1, 1, 1, 1], commitsWhenPublished);

        Assert.Equal(1, Assert.IsType<NutritionalDiagnosisSuperseded>(events[0]).DiagnosisId);
        Assert.Equal(PendingDiagnosisId, Assert.IsType<NutritionalDiagnosisIssued>(events[1]).DiagnosisId);
        var superseded = Assert.IsType<PlanVersionSuperseded>(events[2]);
        Assert.Equal((1, 2), (superseded.SupersededVersion, superseded.NewVersion));
        Assert.Equal(DraftPlanId, Assert.IsType<NutritionPlanPublished>(events[3]).PlanId);
        var completed = Assert.IsType<ConsultationCompleted>(events[4]);
        Assert.Equal((ConsultationId, PatientId, PractitionerId, 2),
            (completed.ConsultationId, completed.PatientId, completed.PractitionerId, completed.PlanVersion));
        Assert.Null(completed.ScheduledFollowUpId);
    }

    [Fact]
    public async Task The_change_reason_of_the_new_version_is_written_automatically()
    {
        await CreateService().Handle(Publish());

        Assert.Equal("Nueva consulta del 18 sept. 2026", _draft.ChangeReason!.Value);
    }

    [Fact]
    public async Task Legacy_restrictions_of_the_version_in_force_are_recorded_as_left_out()
    {
        await CreateService().Handle(Publish() with { Restrictions = [DietaryRestriction.Vegan] });

        Assert.Equal([DietaryRestriction.Vegan], _draft.Restrictions);
        Assert.Equal(["Sin cebolla"], _draft.DroppedLegacyRestrictions);
        Assert.Equal(["Sin cebolla"], _activePlan.LegacyRestrictions);
    }

    [Fact]
    public async Task The_first_consultation_supersedes_nothing_and_needs_no_change_reason()
    {
        _diagnoses.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns((NutritionalDiagnosis?)null);
        _plans.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns((NutritionPlan?)null);
        var first = PlanScenario.Prescribed(DraftPlanId, PatientId, PractitionerId, PendingDiagnosisId, 1);
        _plans.FindByIdAsync(DraftPlanId, Arg.Any<CancellationToken>()).Returns(first);

        var outcome = Success(await CreateService().Handle(Publish()));

        Assert.True(outcome.Plan.IsActive);
        Assert.Null(outcome.Plan.ChangeReason);
        Assert.True(_pending.IsActive);
        Assert.Equal(
            [typeof(NutritionalDiagnosisIssued), typeof(NutritionPlanPublished), typeof(ConsultationCompleted)],
            Fakes.Published(_mediator).Select(e => e.GetType()).ToArray());
    }

    [Fact]
    public async Task A_failed_save_rolls_everything_back_and_publishes_nothing()
    {
        _unitOfWork.FailOnSave = 1;

        var result = await CreateService().Handle(Publish());

        AssertFailure(result, NutritionalCareError.UnexpectedError);
        Assert.Equal(1, _unitOfWork.Rollbacks);
        Assert.Equal(0, _unitOfWork.DurableSaves);
        Assert.Empty(Fakes.Published(_mediator));
    }

    [Theory]
    [InlineData(new[] { "NoOnion" }, new string[0], new string[0], NutritionalCareError.UnknownRestriction)]
    [InlineData(new string[0], new[] { "Prioriza vegetales" }, new string[0], NutritionalCareError.UnknownGuideline)]
    [InlineData(new string[0], new string[0], new[] { "ok" }, NutritionalCareError.InvalidCustomGuideline)]
    public async Task Catalogs_are_checked_before_anything_is_loaded(string[] restrictions, string[] guidelines,
        string[] custom, NutritionalCareError expected)
    {
        var result = await CreateService().Handle(new PublishFromConsultationCommand(ConsultationId, PractitionerId,
            restrictions, guidelines, custom));

        AssertFailure(result, expected);
        await _iam.DidNotReceiveWithAnyArgs().IsPractitioner(default, default);
        await _consultations.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task Only_a_practitioner_publishes_and_it_is_checked_before_loading()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(false);

        AssertFailure(await CreateService().Handle(Publish()), NutritionalCareError.PractitionerOnly);
        await _consultations.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task An_unknown_consultation_is_not_found()
    {
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns((Consultation?)null);

        AssertFailure(await CreateService().Handle(Publish()), NutritionalCareError.ConsultationNotFound);
    }

    [Fact]
    public async Task Only_the_practitioner_leading_the_consultation_publishes()
    {
        _iam.IsPractitioner(99, Arg.Any<CancellationToken>()).Returns(true);

        AssertFailure(await CreateService().Handle(Publish() with { PractitionerId = 99 }),
            NutritionalCareError.PractitionerOnly);
        AssertNothingChanged();
    }

    [Fact]
    public async Task Without_an_active_care_link_nothing_is_published()
    {
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(false);

        AssertFailure(await CreateService().Handle(Publish()), NutritionalCareError.ActiveCareLinkRequired);
        AssertNothingChanged();
    }

    [Fact]
    public async Task A_completed_consultation_cannot_publish_again()
    {
        await CreateService().Handle(Publish());
        _mediator.ClearReceivedCalls();

        AssertFailure(await CreateService().Handle(Publish()), NutritionalCareError.ConsultationNotInProgress);
        Assert.Empty(Fakes.Published(_mediator));
    }

    [Fact]
    public async Task There_is_no_publication_before_the_targets_of_step_three()
    {
        var atTargets = ConsultationScenario.Started(ConsultationId, PatientId, PractitionerId);
        atTargets.AttachAssessment(AssessmentId);
        atTargets.AttachDiagnosis(PendingDiagnosisId);
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns(atTargets);

        AssertFailure(await CreateService().Handle(Publish()), NutritionalCareError.ConsultationStepOutOfOrder);
        AssertNothingChanged();
    }

    [Fact]
    public async Task Unprescribed_targets_are_not_published()
    {
        var proposed = new NutritionPlan(PatientId, PractitionerId, PendingDiagnosisId, 2,
            _draft.CalculationBasis, _draft.TargetProposal);
        _plans.FindByIdAsync(DraftPlanId, Arg.Any<CancellationToken>())
            .Returns(Identity.Assign(proposed, new PlanId(DraftPlanId)));

        AssertFailure(await CreateService().Handle(Publish()), NutritionalCareError.PreviousProposalRequired);
        AssertNothingChanged();
    }

    [Fact]
    public async Task Targets_calculated_on_an_earlier_diagnosis_send_the_practitioner_back_to_step_three()
    {
        var stale = PlanScenario.Prescribed(DraftPlanId, PatientId, PractitionerId, diagnosisId: 77, version: 2);
        _plans.FindByIdAsync(DraftPlanId, Arg.Any<CancellationToken>()).Returns(stale);

        AssertFailure(await CreateService().Handle(Publish()), NutritionalCareError.ConsultationStepOutOfOrder);
        AssertNothingChanged();
    }

    [Fact]
    public async Task The_stand_alone_publication_refuses_a_draft_whose_diagnosis_is_still_pending()
    {
        var service = new NutritionPlanCommandService(_plans, _diagnoses,
            Substitute.For<INutritionalAssessmentRepository>(), _unitOfWork, Substitute.For<IBmrCalculator>(),
            NullLogger<NutritionPlanCommandService>.Instance, _mediator);
        _plans.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns((NutritionPlan?)null);

        var result = await service.Handle(new PublishNutritionPlanCommand(DraftPlanId, PractitionerId, [], []));

        var failure = Assert.IsType<Result<NutritionPlan, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.PlanRequiresDiagnosis, failure.Error);
        Assert.False(_draft.IsPublished);
    }

    [Theory]
    [InlineData(2026, 1, 5, "Nueva consulta del 5 ene. 2026")]
    [InlineData(2026, 9, 18, "Nueva consulta del 18 sept. 2026")]
    [InlineData(2026, 12, 31, "Nueva consulta del 31 dic. 2026")]
    public void The_automatic_change_reason_names_the_consultation_date(int year, int month, int day,
        string expected)
    {
        Assert.Equal(expected, ChangeReason.NewConsultation(new DateOnly(year, month, day)).Value);
    }

    private void Track<TEvent>(List<int> commitsWhenPublished) where TEvent : IEvent
    {
        _mediator.PublishAsync(Arg.Any<TEvent>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            commitsWhenPublished.Add(_unitOfWork.Commits);
            return Task.CompletedTask;
        });
    }

    private void AssertNothingChanged()
    {
        Assert.True(_activeDiagnosis.IsActive);
        Assert.True(_pending.IsPending);
        Assert.True(_activePlan.IsActive);
        Assert.False(_draft.IsPublished);
        Assert.True(_consultation.IsInProgress);
        Assert.Equal(0, _unitOfWork.DurableSaves);
        Assert.Empty(Fakes.Published(_mediator));
    }

    private ConsultationCommandService CreateService()
    {
        return new ConsultationCommandService(_consultations, Substitute.For<IPatientBaselineRepository>(),
            Substitute.For<INutritionalAssessmentRepository>(), _diagnoses, _plans, _unitOfWork, _iam,
            _careRelationship, new FixedClinicalDate(Today), Substitute.For<IBmrCalculator>(),
            Substitute.For<IDefaultTargetParametersPolicy>(), NullLogger<ConsultationCommandService>.Instance,
            _mediator);
    }

    private static PublishFromConsultationCommand Publish()
    {
        return new PublishFromConsultationCommand(ConsultationId, PractitionerId, [],
            [Guideline.PrioritizeVegetables], []);
    }

    private static ConsultationPublicationOutcome Success(
        Result<ConsultationPublicationOutcome, NutritionalCareError> result)
    {
        return Assert.IsType<Result<ConsultationPublicationOutcome, NutritionalCareError>.Success>(result).Value;
    }

    private static void AssertFailure(Result<ConsultationPublicationOutcome, NutritionalCareError> result,
        NutritionalCareError expected)
    {
        var failure = Assert.IsType<Result<ConsultationPublicationOutcome, NutritionalCareError>.Failure>(result);
        Assert.Equal(expected, failure.Error);
    }
}
