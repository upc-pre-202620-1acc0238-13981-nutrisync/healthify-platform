using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-4. Step 2 of the guided consultation: the order of the guards, the single transaction and the
///     deterministic suggestion of EV-3. Since NC-7 the diagnosis of step 2 is pending: Consultation Supersedes
///     Previous Diagnosis applies when step 4 publishes the plan (see <c>PublishFromConsultationTests</c>), so the
///     active diagnosis is untouched here and no diagnosis event is published.
/// </summary>
public class ConsultationDiagnosisTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int ConsultationId = 40;
    private const int AssessmentId = 100;
    private static readonly DateOnly Today = new(2026, 3, 10);

    private readonly IConsultationRepository _consultations = Substitute.For<IConsultationRepository>();
    private readonly INutritionalAssessmentRepository _assessments = Substitute.For<INutritionalAssessmentRepository>();
    private readonly INutritionalDiagnosisRepository _diagnoses = Substitute.For<INutritionalDiagnosisRepository>();
    private readonly TransactionalUnitOfWork _unitOfWork = new();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();

    private readonly ICareRelationshipContextFacade _careRelationship =
        Substitute.For<ICareRelationshipContextFacade>();

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Consultation _consultation;
    private readonly NutritionalDiagnosis _previous;

    public ConsultationDiagnosisTests()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);

        var baseline = ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female",
            168m, Today);
        var assessment = ConsultationScenario.MeasuredAssessment(AssessmentId, ConsultationId, PractitionerId,
            baseline, Today, 74.2m);
        _assessments.FindByIdAsync(AssessmentId, Arg.Any<CancellationToken>()).Returns(assessment);

        _consultation = ConsultationScenario.Started(ConsultationId, PatientId, PractitionerId);
        _consultation.AttachAssessment(AssessmentId);
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns(_consultation);

        // The diagnosis of the first consultation, issued on an older assessment.
        var older = ConsultationScenario.MeasuredAssessment(90, 39, PractitionerId, baseline, Today, 80m);
        _previous = ConsultationScenario.Diagnosis(1, older, DiagnosisCode.ObesityGradeI);
        _diagnoses.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(_previous);

        _diagnoses.AddAsync(Arg.Do<NutritionalDiagnosis>(d => Identity.Assign(d, new DiagnosisId(2))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_second_consultation_issues_a_pending_diagnosis_and_leaves_the_active_one_untouched()
    {
        var result = await CreateService().Handle(Issue());

        var outcome = Assert.IsType<Result<ConsultationDiagnosisOutcome, NutritionalCareError>.Success>(result).Value;
        Assert.True(_previous.IsActive);
        Assert.Null(_previous.SupersededAt);
        Assert.False(outcome.Diagnosis.IsActive);
        Assert.True(outcome.Diagnosis.IsPendingFor(ConsultationId));
        Assert.Equal(DiagnosisCode.OverweightGradeI, outcome.Diagnosis.Code!.Value);
        Assert.Equal(AssessmentId, outcome.Diagnosis.AssessmentId);
        Assert.Equal("IMC 26.3 kg/m²; cintura 88 cm", outcome.Diagnosis.Rationale.Value);
        Assert.Equal(26.3m, outcome.Diagnosis.BmiAtIssue);

        Assert.Equal(2, outcome.Consultation.DiagnosisId);
        Assert.Equal(ConsultationStep.Targets, outcome.Consultation.CurrentStep.Value);
        _diagnoses.DidNotReceive().Update(_previous);
    }

    [Fact]
    public async Task Issue_and_step_are_committed_together_and_no_diagnosis_event_precedes_step_four()
    {
        await CreateService().Handle(Issue());

        Assert.Equal(2, _unitOfWork.DurableSaves);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Empty(Fakes.Published(_mediator));
    }

    [Fact]
    public async Task Repeating_step_two_discards_the_pending_diagnosis_and_keeps_one_per_consultation()
    {
        var first = Assert.IsType<Result<ConsultationDiagnosisOutcome, NutritionalCareError>.Success>(
            await CreateService().Handle(Issue())).Value.Diagnosis;
        _diagnoses.FindByIdAsync(2, Arg.Any<CancellationToken>()).Returns(first);
        await _diagnoses.AddAsync(Arg.Do<NutritionalDiagnosis>(d => Identity.Assign(d, new DiagnosisId(3))),
            Arg.Any<CancellationToken>());

        var second = Assert.IsType<Result<ConsultationDiagnosisOutcome, NutritionalCareError>.Success>(
            await CreateService().Handle(Issue() with { Code = DiagnosisCode.ObesityGradeI })).Value;

        Assert.True(first.IsDiscarded);
        Assert.False(first.IsPending);
        Assert.True(second.Diagnosis.IsPendingFor(ConsultationId));
        Assert.Equal(3, second.Consultation.DiagnosisId);
        Assert.True(_previous.IsActive);
        _diagnoses.Received(1).Update(first);
        Assert.Empty(Fakes.Published(_mediator));
    }

    [Fact]
    public async Task A_failed_save_rolls_everything_back_and_publishes_nothing()
    {
        _unitOfWork.FailOnSave = 2;

        var result = await CreateService().Handle(Issue());

        AssertFailure(result, NutritionalCareError.UnexpectedError);
        Assert.Equal(1, _unitOfWork.Rollbacks);
        Assert.Equal(0, _unitOfWork.DurableSaves);
        Assert.Empty(Fakes.Published(_mediator));
    }

    [Theory]
    [InlineData("Other", "PractitionerSelected", null, null, NutritionalCareError.UnknownDiagnosisCode)]
    [InlineData("OverweightGradeI", "Rule", null, null, NutritionalCareError.InvalidDiagnosisSource)]
    [InlineData("OverweightGradeI", "AiSuggestionAccepted", null, "IMC 26.3", NutritionalCareError.InvalidDiagnosisSource)]
    [InlineData("OverweightGradeI", "AiSuggestionAccepted", 7L, null, NutritionalCareError.InvalidDiagnosisSource)]
    public async Task Value_objects_are_checked_before_anything_is_loaded(string code, string source,
        long? generationId, string? rationale, NutritionalCareError expected)
    {
        var result = await CreateService().Handle(new IssueConsultationDiagnosisCommand(ConsultationId,
            PractitionerId, code, source, generationId, rationale));

        AssertFailure(result, expected);
        await _iam.DidNotReceiveWithAnyArgs().IsPractitioner(default, default);
        await _consultations.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task Only_a_practitioner_diagnoses_and_it_is_checked_before_loading()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(false);

        AssertFailure(await CreateService().Handle(Issue()), NutritionalCareError.PractitionerOnly);
        await _consultations.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task An_unknown_consultation_is_not_found()
    {
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns((Consultation?)null);

        AssertFailure(await CreateService().Handle(Issue()), NutritionalCareError.ConsultationNotFound);
    }

    [Fact]
    public async Task Only_the_practitioner_leading_the_consultation_saves_its_steps()
    {
        _iam.IsPractitioner(99, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().Handle(Issue() with { PractitionerId = 99 });

        AssertFailure(result, NutritionalCareError.PractitionerOnly);
    }

    [Fact]
    public async Task Without_an_active_care_link_nothing_is_issued()
    {
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(false);

        AssertFailure(await CreateService().Handle(Issue()), NutritionalCareError.ActiveCareLinkRequired);
        Assert.True(_previous.IsActive);
    }

    [Fact]
    public async Task There_is_no_diagnosis_before_the_measurement_of_step_one()
    {
        var fresh = ConsultationScenario.Started(ConsultationId, PatientId, PractitionerId);
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns(fresh);

        AssertFailure(await CreateService().Handle(Issue()), NutritionalCareError.ConsultationStepOutOfOrder);
        Assert.True(_previous.IsActive);
        Assert.Equal(0, _unitOfWork.DurableSaves);
    }

    [Fact]
    public void The_aggregate_refuses_a_diagnosis_before_the_measurement()
    {
        var fresh = ConsultationScenario.Started(ConsultationId, PatientId, PractitionerId);

        Assert.Throws<InvalidOperationException>(() => fresh.AttachDiagnosis(2));
        Assert.Throws<InvalidOperationException>(() => _consultation.AttachPlanDraft(5));
    }

    [Fact]
    public async Task The_suggestion_is_the_deterministic_category_of_the_index_with_source_rule()
    {
        var suggestion = await new ConsultationQueryService(_consultations, _assessments, _diagnoses,
            Substitute.For<IDefaultGuidelinesProvider>(), Substitute.For<INutritionPlanRepository>(),
            Substitute.For<IMonitoringContextFacade>())
            .Handle(new GetConsultationDiagnosisSuggestionQuery(ConsultationId));

        Assert.NotNull(suggestion);
        Assert.Equal(DiagnosisCode.OverweightGradeI, suggestion.Code);
        Assert.Equal("IMC 26.3 kg/m²; cintura 88 cm", suggestion.Rationale);
        Assert.Equal("Rule", suggestion.Source);
        Assert.Null(suggestion.AiGenerationId);
    }

    [Fact]
    public async Task There_is_no_suggestion_before_the_measurement()
    {
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>())
            .Returns(ConsultationScenario.Started(ConsultationId, PatientId, PractitionerId));

        Assert.Null(await new ConsultationQueryService(_consultations, _assessments, _diagnoses,
            Substitute.For<IDefaultGuidelinesProvider>(), Substitute.For<INutritionPlanRepository>(),
            Substitute.For<IMonitoringContextFacade>())
            .Handle(new GetConsultationDiagnosisSuggestionQuery(ConsultationId)));
    }

    private ConsultationCommandService CreateService()
    {
        return new ConsultationCommandService(_consultations, Substitute.For<IPatientBaselineRepository>(),
            _assessments, _diagnoses, Substitute.For<INutritionPlanRepository>(), _unitOfWork, _iam, _careRelationship,
            new FixedClinicalDate(Today), Substitute.For<IBmrCalculator>(), Substitute.For<IDefaultTargetParametersPolicy>(),
            NullLogger<ConsultationCommandService>.Instance, _mediator);
    }

    private static IssueConsultationDiagnosisCommand Issue()
    {
        return new IssueConsultationDiagnosisCommand(ConsultationId, PractitionerId, "OverweightGradeI",
            "PractitionerSelected", null, null);
    }

    private static void AssertFailure(Result<ConsultationDiagnosisOutcome, NutritionalCareError> result,
        NutritionalCareError expected)
    {
        var failure = Assert.IsType<Result<ConsultationDiagnosisOutcome, NutritionalCareError>.Failure>(result);
        Assert.Equal(expected, failure.Error);
    }
}
