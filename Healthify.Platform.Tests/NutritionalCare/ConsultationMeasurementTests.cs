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
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-3. Step 1 of the guided consultation (RecordConsultationMeasurementCommand) and the minimal
///     start of a consultation it needs: order of the guards, the snapshot taken from the baseline, the
///     single transaction around both saves and the events published after the final commit.
/// </summary>
public class ConsultationMeasurementTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int ConsultationId = 40;

    /// <summary>The practice's date of the consultation: the patient (born 1995-03-10) turns 31 today.</summary>
    private static readonly DateOnly ConsultationDate = new(2026, 3, 10);

    private readonly IConsultationRepository _consultations = Substitute.For<IConsultationRepository>();
    private readonly IPatientBaselineRepository _baselines = Substitute.For<IPatientBaselineRepository>();
    private readonly INutritionalAssessmentRepository _assessments = Substitute.For<INutritionalAssessmentRepository>();
    private readonly TransactionalUnitOfWork _unitOfWork = new();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();

    private readonly ICareRelationshipContextFacade _careRelationship =
        Substitute.For<ICareRelationshipContextFacade>();

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly PatientBaseline _baseline;
    private readonly Consultation _consultation;
    private int _nextAssessmentId = 100;

    public ConsultationMeasurementTests()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);

        _baseline = new PatientBaseline(new RecordPatientBaselineCommand(PatientId, PractitionerId,
            new DateOnly(1995, 3, 10), "Female", 168m, ["Hypothyroidism"]), ConsultationDate);
        _baselines.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(_baseline);

        _consultation = Identity.Assign(
            new Consultation(new StartConsultationCommand(PatientId, PractitionerId), true),
            new ConsultationId(ConsultationId));
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns(_consultation);

        _assessments.AddAsync(
            Arg.Do<NutritionalAssessment>(a => Identity.Assign(a, new AssessmentId(_nextAssessmentId++))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Step_one_creates_a_closed_assessment_with_the_baseline_snapshot()
    {
        var result = await CreateService().Handle(Measure());

        var outcome = Assert.IsType<Result<ConsultationMeasurementOutcome, NutritionalCareError>.Success>(result)
            .Value;
        var assessment = outcome.Assessment;
        var measurement = Assert.Single(assessment.Measurements);

        Assert.True(assessment.IsClosed);
        Assert.Equal(PatientId, assessment.PatientId);
        Assert.Equal(ConsultationId, assessment.ConsultationId);
        Assert.Equal(31, assessment.AgeYears); // the birthday is the practice's today
        Assert.Equal("Female", assessment.BiologicalSex.Value);
        Assert.Equal(["Hypothyroidism"], assessment.ConditionsSnapshot);
        Assert.Equal(ActivityLevel.Moderate, assessment.ActivityLevel!.Value);
        Assert.Equal(4, assessment.EatingHabits!.MealsPerDay);
        Assert.Null(assessment.BiochemistryPanel);
        Assert.Null(assessment.Habits);
        Assert.Null(assessment.MedicalHistory);

        // The height is the baseline's; the BMI is computed and stored with it.
        Assert.Equal(168m, measurement.HeightCm);
        Assert.Equal(26.3m, measurement.BmiKgM2);
        Assert.Equal(BodyMassIndex.OverweightGradeI, measurement.BmiCategory);
        Assert.Equal(["Fasting", "NoShoes"], measurement.ProtocolChecks);
        Assert.Equal("Fasting, NoShoes", measurement.Protocol.Value);

        Assert.Equal(100, outcome.Consultation.AssessmentId);
        Assert.Equal(ConsultationStep.Diagnosis, outcome.Consultation.CurrentStep.Value);
    }

    [Fact]
    public async Task Both_saves_are_committed_together_and_events_follow_the_commit()
    {
        var commitsWhenPublished = new List<int>();
        _mediator.PublishAsync(Arg.Any<NutritionalAssessmentRecorded>(), Arg.Any<CancellationToken>())
            .Returns(_ => Record(commitsWhenPublished));
        _mediator.PublishAsync(Arg.Any<ClinicalMeasurementTaken>(), Arg.Any<CancellationToken>())
            .Returns(_ => Record(commitsWhenPublished));
        _mediator.PublishAsync(Arg.Any<AssessmentClosed>(), Arg.Any<CancellationToken>())
            .Returns(_ => Record(commitsWhenPublished));

        await CreateService().Handle(Measure());

        Assert.Equal(2, _unitOfWork.DurableSaves);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Equal([1, 1, 1], commitsWhenPublished);
        Received.InOrder(() =>
        {
            _mediator.PublishAsync(Arg.Any<NutritionalAssessmentRecorded>(), Arg.Any<CancellationToken>());
            _mediator.PublishAsync(Arg.Any<ClinicalMeasurementTaken>(), Arg.Any<CancellationToken>());
            _mediator.PublishAsync(Arg.Any<AssessmentClosed>(), Arg.Any<CancellationToken>());
        });
        var taken = Assert.Single(Fakes.Published(_mediator).OfType<ClinicalMeasurementTaken>());
        Assert.Equal(74.2m, taken.WeightKg);
    }

    [Fact]
    public async Task When_linking_to_the_consultation_fails_nothing_of_the_step_remains()
    {
        _unitOfWork.FailOnSave = 2;

        var result = await CreateService().Handle(Measure());

        AssertFailure(result, NutritionalCareError.UnexpectedError);
        // The first save (the closed assessment) was inside the transaction and was rolled back with the
        // second (the consultation): the database keeps neither.
        Assert.Equal(0, _unitOfWork.DurableSaves);
        Assert.Equal(1, _unitOfWork.DiscardedSaves);
        Assert.Equal(1, _unitOfWork.Rollbacks);
        Assert.Equal(0, _unitOfWork.Commits);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task Repeating_step_one_creates_a_new_assessment_that_supersedes_the_previous_one()
    {
        var service = CreateService();
        await service.Handle(Measure());

        var second = await service.Handle(Measure() with { WeightKg = 74.0m });

        var outcome = Assert.IsType<Result<ConsultationMeasurementOutcome, NutritionalCareError>.Success>(second)
            .Value;
        Assert.Equal(101, outcome.Assessment.Id.Value);
        Assert.Equal(100, outcome.Assessment.SupersedesAssessmentId);
        Assert.Equal(101, _consultation.AssessmentId);
        await _assessments.Received(2).AddAsync(Arg.Any<NutritionalAssessment>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(400, new[] { "Fasting" }, NutritionalCareError.ImplausibleMeasurement)]
    [InlineData(74.2, new string[0], NutritionalCareError.ProtocolChecklistEmpty)]
    [InlineData(74.2, new[] { "Fasting", "Barefoot" }, NutritionalCareError.UnknownProtocolCheck)]
    public async Task Invalid_input_is_rejected_before_anything_is_loaded(double weight, string[] checks,
        NutritionalCareError error)
    {
        var command = Measure() with { WeightKg = (decimal)weight, ProtocolChecks = checks };

        AssertFailure(await CreateService().Handle(command), error);
        Assert.Empty(_iam.ReceivedCalls());
        Assert.Empty(_consultations.ReceivedCalls());
    }

    [Fact]
    public async Task Each_structured_value_maps_to_its_own_error()
    {
        var service = CreateService();

        AssertFailure(await service.Handle(Measure() with { ActivityLevel = "Moderada" }),
            NutritionalCareError.InvalidActivityLevel);
        AssertFailure(await service.Handle(Measure() with { ActivityLevel = null! }),
            NutritionalCareError.InvalidActivityLevel);
        AssertFailure(await service.Handle(Measure() with { Habits = new EatingHabitsDto(12, null, null) }),
            NutritionalCareError.InvalidEatingHabits);
        AssertFailure(await service.Handle(Measure() with { Biochemistry = new BiochemistryDto(5m, null, null) }),
            NutritionalCareError.ImplausibleBiochemistry);
        AssertFailure(await service.Handle(Measure() with { WaistCm = 30m }),
            NutritionalCareError.ImplausibleMeasurement);
    }

    [Fact]
    public async Task Unknown_consultation_is_not_found()
    {
        AssertFailure(await CreateService().Handle(Measure() with { ConsultationId = 999 }),
            NutritionalCareError.ConsultationNotFound);
    }

    [Fact]
    public async Task Only_the_practitioner_leading_the_consultation_saves_its_steps()
    {
        _iam.IsPractitioner(21, Arg.Any<CancellationToken>()).Returns(true);

        AssertFailure(await CreateService().Handle(Measure() with { PractitionerId = 21 }),
            NutritionalCareError.PractitionerOnly);
        Assert.Empty(_careRelationship.ReceivedCalls());
    }

    [Fact]
    public async Task Withdrawn_consent_stops_a_paused_consultation()
    {
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(false);

        AssertFailure(await CreateService().Handle(Measure()), NutritionalCareError.ActiveCareLinkRequired);
        await _assessments.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Without_a_baseline_the_measurement_cannot_be_recorded()
    {
        _baselines.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns((PatientBaseline?)null);

        AssertFailure(await CreateService().Handle(Measure()), NutritionalCareError.BaselineRequired);
        Assert.Equal(0, _unitOfWork.DurableSaves);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task Start_requires_a_baseline()
    {
        _baselines.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns((PatientBaseline?)null);

        var result = await CreateService().Handle(new StartConsultationCommand(PatientId, PractitionerId));

        var failure = Assert.IsType<Result<Consultation, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.BaselineRequired, failure.Error);
    }

    [Fact]
    public async Task Start_refuses_a_second_consultation_in_progress()
    {
        _consultations.FindInProgressByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns(_consultation);

        var result = await CreateService().Handle(new StartConsultationCommand(PatientId, PractitionerId));

        var failure = Assert.IsType<Result<Consultation, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.ConsultationAlreadyInProgress, failure.Error);
        await _consultations.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Start_opens_the_consultation_on_the_measurement_step()
    {
        _consultations.FindInProgressByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns((Consultation?)null);
        _assessments.ExistsForPatientAsync(PatientId, Arg.Any<CancellationToken>()).Returns(true);
        await _consultations.AddAsync(Arg.Do<Consultation>(c => Identity.Assign(c, new ConsultationId(41))),
            Arg.Any<CancellationToken>());

        var result = await CreateService().Handle(new StartConsultationCommand(PatientId, PractitionerId, 7));

        var consultation = Assert.IsType<Result<Consultation, NutritionalCareError>.Success>(result).Value;
        Assert.True(consultation.IsInProgress);
        Assert.Equal(ConsultationStep.Measurement, consultation.CurrentStep.Value);
        Assert.Equal(1, consultation.CurrentStep.Number);
        Assert.Equal(7, consultation.ScheduledFollowUpId);
        Assert.False(consultation.IsFirstConsultation); // the patient already had an assessment
        var started = Assert.IsType<ConsultationStarted>(Assert.Single(Fakes.Published(_mediator)));
        Assert.Equal(41, started.ConsultationId);
    }

    private Task Record(List<int> commitsWhenPublished)
    {
        commitsWhenPublished.Add(_unitOfWork.Commits);
        return Task.CompletedTask;
    }

    private ConsultationCommandService CreateService()
    {
        return new ConsultationCommandService(_consultations, _baselines, _assessments,
            Substitute.For<INutritionalDiagnosisRepository>(), Substitute.For<INutritionPlanRepository>(), _unitOfWork,
            _iam, _careRelationship, new FixedClinicalDate(ConsultationDate), Substitute.For<IBmrCalculator>(),
            Substitute.For<IDefaultTargetParametersPolicy>(),
            NullLogger<ConsultationCommandService>.Instance, _mediator);
    }

    private static RecordConsultationMeasurementCommand Measure()
    {
        return new RecordConsultationMeasurementCommand(ConsultationId, PractitionerId, 74.2m, 88m, null,
            ["Fasting", "NoShoes"], "Moderate", new EatingHabitsDto(4, 1.5m, 2), null);
    }

    private static void AssertFailure(Result<ConsultationMeasurementOutcome, NutritionalCareError> result,
        NutritionalCareError expected)
    {
        var failure = Assert.IsType<Result<ConsultationMeasurementOutcome, NutritionalCareError>.Failure>(result);
        Assert.Equal(expected, failure.Error);
    }
}
