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
using Healthify.Platform.NutritionalCare.Infrastructure.Calculators;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-5. Step 3 of the guided consultation with the real arithmetic and the default parameters: the
///     inputs come from the assessment of the consultation, the draft is recalculated in place until it is
///     prescribed, and DECISIÓN §12-#1 keeps the override reason required.
/// </summary>
/// <remarks>
///     The case of EV-4 ("Mujer · 31 años · 168 cm · 74.2 kg · actividad moderada → 1 796 kcal") does not
///     hold with Mifflin-St Jeor: 31 years give 1 787.80 kcal; 1 796 is the result for 30 years. Both are
///     asserted, with the platform rounding (two decimals).
/// </remarks>
public class ConsultationTargetsTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int ConsultationId = 40;
    private const int AssessmentId = 100;
    private const int DiagnosisId = 2;
    private const int DraftPlanId = 500;
    private static readonly DateOnly Today = new(2026, 3, 10);

    private readonly IConsultationRepository _consultations = Substitute.For<IConsultationRepository>();
    private readonly INutritionalAssessmentRepository _assessments = Substitute.For<INutritionalAssessmentRepository>();
    private readonly INutritionalDiagnosisRepository _diagnoses = Substitute.For<INutritionalDiagnosisRepository>();
    private readonly INutritionPlanRepository _plans = Substitute.For<INutritionPlanRepository>();
    private readonly TransactionalUnitOfWork _unitOfWork = new();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();

    private readonly ICareRelationshipContextFacade _careRelationship =
        Substitute.For<ICareRelationshipContextFacade>();

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Dictionary<int, NutritionPlan> _storedPlans = [];
    private Consultation _consultation = null!;

    public ConsultationTargetsTests()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _plans.GetLatestVersionAsync(PatientId, Arg.Any<CancellationToken>()).Returns(1);
        _plans.AddAsync(Arg.Do<NutritionPlan>(p => _storedPlans[DraftPlanId] = Identity.Assign(p, new PlanId(DraftPlanId))),
            Arg.Any<CancellationToken>());
        _plans.FindByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => _storedPlans.GetValueOrDefault(call.ArgAt<int>(0)));

        AtStepThree(new DateOnly(1995, 3, 10), DiagnosisCode.OverweightGradeI);
    }

    [Fact]
    public async Task Woman_31_years_168_cm_74_2_kg_moderate_with_the_defaults()
    {
        var outcome = Success(await CreateService().Handle(Propose()));

        var basis = outcome.Plan.CalculationBasis;
        Assert.Equal(1476.00m, basis.ComputedBmr); // 10·74.2 + 6.25·168 − 5·31 − 161
        Assert.Equal(1.55m, basis.ActivityFactor);
        Assert.Equal(2287.80m, basis.ComputedTdee);
        Assert.Equal(500m, basis.DeficitValue);
        Assert.Equal(new TargetProposal(1787.80m, 118.72m, 194.15m, 59.59m), outcome.Plan.TargetProposal);

        Assert.Equal(new TargetInputsSummary("Female", 31, 168m, 74.2m, "Moderate"), outcome.Inputs);
    }

    [Fact]
    public async Task The_1_796_kcal_of_the_mockup_is_the_result_for_30_years()
    {
        AtStepThree(new DateOnly(1995, 3, 11), DiagnosisCode.OverweightGradeI);

        var outcome = Success(await CreateService().Handle(Propose()));

        Assert.Equal(1481.00m, outcome.Plan.CalculationBasis.ComputedBmr);
        Assert.Equal(new TargetProposal(1795.55m, 118.72m, 195.50m, 59.85m), outcome.Plan.TargetProposal);
        Assert.Equal(1796m, decimal.Round(outcome.Plan.TargetProposal.EnergyKcal, 0));
        Assert.Equal(119m, decimal.Round(outcome.Plan.TargetProposal.ProteinG, 0));
        Assert.Equal(60m, decimal.Round(outcome.Plan.TargetProposal.FatG, 0));
        Assert.Equal(30, outcome.Inputs.AgeYears);
    }

    [Fact]
    public async Task Inputs_come_from_the_assessment_of_the_consultation_not_from_the_one_behind_the_diagnosis()
    {
        // The diagnosis of this consultation points at an older assessment (80 kg). The consultation's own
        // step 1 (74.2 kg) is what the equations must read.
        var baseline = ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female",
            168m, Today);
        var older = ConsultationScenario.MeasuredAssessment(90, 39, PractitionerId, baseline, Today, 80m);
        _assessments.FindByIdAsync(90, Arg.Any<CancellationToken>()).Returns(older);
        _diagnoses.FindByIdAsync(DiagnosisId, Arg.Any<CancellationToken>())
            .Returns(ConsultationScenario.Diagnosis(DiagnosisId, older, DiagnosisCode.OverweightGradeI));

        var outcome = Success(await CreateService().Handle(Propose()));

        Assert.Equal(74.2m, outcome.Plan.CalculationBasis.ReferenceWeightKg);
        Assert.Equal(1787.80m, outcome.Plan.TargetProposal.EnergyKcal);
    }

    [Fact]
    public async Task The_first_proposal_creates_the_draft_version_and_attaches_it_in_one_transaction()
    {
        var outcome = Success(await CreateService().Handle(Propose()));

        Assert.Equal(2, outcome.Plan.Version);
        Assert.Equal(DiagnosisId, outcome.Plan.DiagnosisId);
        Assert.False(outcome.Plan.IsPrescribed);
        Assert.False(outcome.Plan.IsPublished);
        Assert.Equal(DraftPlanId, outcome.Consultation.PlanId);
        Assert.Equal(ConsultationStep.Publication, outcome.Consultation.CurrentStep.Value);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Equal(2, _unitOfWork.DurableSaves);
        var proposed = Assert.IsType<TargetsProposed>(Assert.Single(Fakes.Published(_mediator)));
        Assert.Equal(1787.80m, proposed.EnergyKcal);
    }

    [Fact]
    public async Task Changing_parameters_recalculates_the_same_draft()
    {
        var service = CreateService();
        await service.Handle(Propose());

        var outcome = Success(await service.Handle(Propose(new TargetParametersDto(DeficitValue: 300m))));

        Assert.Equal(DraftPlanId, outcome.Plan.Id.Value);
        Assert.Equal(1987.80m, outcome.Plan.TargetProposal.EnergyKcal);
        Assert.Equal(300m, outcome.Plan.CalculationBasis.DeficitValue);
        await _plans.Received(1).AddAsync(Arg.Any<NutritionPlan>(), Arg.Any<CancellationToken>());
        _plans.Received(1).Update(outcome.Plan);
    }

    [Fact]
    public async Task A_prescribed_draft_is_not_recalculated()
    {
        var service = CreateService();
        await service.Handle(Propose());
        await service.Handle(Accept());

        var result = await service.Handle(Propose(new TargetParametersDto(DeficitValue: 300m)));

        AssertFailure(result, NutritionalCareError.PlanNotInExpectedState);
        Assert.Equal(1787.80m, _storedPlans[DraftPlanId].TargetProposal.EnergyKcal);
        Assert.Throws<InvalidOperationException>(() => _storedPlans[DraftPlanId].Recalculate(DiagnosisId,
            _storedPlans[DraftPlanId].CalculationBasis, _storedPlans[DraftPlanId].TargetProposal));
    }

    [Fact]
    public async Task The_draft_shows_the_legacy_restrictions_of_the_version_in_force()
    {
        // NC-6: the practitioner sees them in EV-5 and chooses the equivalent code for the new version.
        _plans.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(
            PlanScenario.PublishedBeforeNc6(1, PatientId, PractitionerId, 1, 1, [], ["Sin cerdo", "Sin lactosa"]));

        var outcome = Success(await CreateService().Handle(Propose()));

        Assert.Equal(["Sin cerdo"], outcome.ActivePlanLegacyRestrictions);
    }

    [Fact]
    public async Task Underweight_gets_no_deficit_by_default()
    {
        AtStepThree(new DateOnly(1995, 3, 10), DiagnosisCode.Underweight);

        var outcome = Success(await CreateService().Handle(Propose()));

        Assert.Equal(0m, outcome.Plan.CalculationBasis.DeficitValue);
        Assert.Equal(outcome.Plan.CalculationBasis.ComputedTdee, outcome.Plan.TargetProposal.EnergyKcal);
    }

    [Fact]
    public async Task There_are_no_targets_before_the_diagnosis_of_step_two()
    {
        var consultation = ConsultationScenario.Started(ConsultationId, PatientId, PractitionerId);
        consultation.AttachAssessment(AssessmentId);
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns(consultation);

        AssertFailure(await CreateService().Handle(Propose()), NutritionalCareError.ConsultationStepOutOfOrder);
        Assert.Empty(_storedPlans);
    }

    [Theory]
    [InlineData("Magic", null, null, NutritionalCareError.UnsupportedEquation)]
    [InlineData(null, "Ideal", null, NutritionalCareError.InvalidReferenceWeight)]
    [InlineData(null, null, 3.0, NutritionalCareError.InvalidActivityFactor)]
    public async Task Changed_parameters_are_checked_before_anything_is_loaded(string? equation, string? kind,
        double? activityFactor, NutritionalCareError expected)
    {
        var result = await CreateService().Handle(Propose(new TargetParametersDto(equation, kind,
            ActivityFactor: (decimal?)activityFactor)));

        AssertFailure(result, expected);
        await _consultations.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task Accepting_signs_the_proposed_numbers()
    {
        var service = CreateService();
        await service.Handle(Propose());

        var result = await service.Handle(Accept());

        var outcome = Assert.IsType<Result<ConsultationTargetsOutcome, NutritionalCareError>.Success>(result).Value;
        Assert.True(outcome.Plan.IsPrescribed);
        Assert.Equal(1787.80m, outcome.Plan.PrescribedTargets!.EnergyKcal);
        Assert.Contains(Fakes.Published(_mediator), e => e is TargetsAcceptedAsProposed);
    }

    [Fact]
    public async Task Writing_own_values_without_a_reason_is_rejected_before_loading()
    {
        // DECISIÓN §12-#1: the override reason stays required in the consultation.
        var result = await CreateService().Handle(new PrescribeConsultationTargetsCommand(ConsultationId,
            PractitionerId, PrescriptionOutcome.Overridden, 1700m));

        var failure = Assert.IsType<Result<ConsultationTargetsOutcome, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.OverrideReasonRequired, failure.Error);
        await _consultations.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task Writing_own_values_with_a_reason_overrides_the_proposal()
    {
        var service = CreateService();
        await service.Handle(Propose());

        var result = await service.Handle(new PrescribeConsultationTargetsCommand(ConsultationId, PractitionerId,
            PrescriptionOutcome.Overridden, 1700m, OverrideReason: "Prefiere un descenso más gradual."));

        var outcome = Assert.IsType<Result<ConsultationTargetsOutcome, NutritionalCareError>.Success>(result).Value;
        Assert.Equal(1700m, outcome.Plan.PrescribedTargets!.EnergyKcal);
        Assert.Equal(118.72m, outcome.Plan.PrescribedTargets.ProteinG);
        Assert.Contains(Fakes.Published(_mediator), e => e is TargetsOverridden);
    }

    [Fact]
    public async Task There_is_nothing_to_prescribe_before_the_proposal()
    {
        var result = await CreateService().Handle(Accept());

        var failure = Assert.IsType<Result<ConsultationTargetsOutcome, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.ConsultationStepOutOfOrder, failure.Error);
    }

    /// <summary>A consultation with step 1 (Female, 168 cm, 74.2 kg, moderate) and step 2 saved.</summary>
    private void AtStepThree(DateOnly birthDate, string diagnosisCode)
    {
        var baseline = ConsultationScenario.Baseline(PatientId, PractitionerId, birthDate, "Female", 168m, Today);
        var assessment = ConsultationScenario.MeasuredAssessment(AssessmentId, ConsultationId, PractitionerId,
            baseline, Today, 74.2m);
        _assessments.FindByIdAsync(AssessmentId, Arg.Any<CancellationToken>()).Returns(assessment);
        _diagnoses.FindByIdAsync(DiagnosisId, Arg.Any<CancellationToken>())
            .Returns(ConsultationScenario.Diagnosis(DiagnosisId, assessment, diagnosisCode));

        _consultation = ConsultationScenario.Started(ConsultationId, PatientId, PractitionerId);
        _consultation.AttachAssessment(AssessmentId);
        _consultation.AttachDiagnosis(DiagnosisId);
        _consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns(_consultation);
    }

    private ConsultationCommandService CreateService()
    {
        return new ConsultationCommandService(_consultations, Substitute.For<IPatientBaselineRepository>(),
            _assessments, _diagnoses, _plans, _unitOfWork, _iam, _careRelationship, new FixedClinicalDate(Today),
            new BmrCalculator(), DefaultTargetParametersPolicyTests.Policy(),
            NullLogger<ConsultationCommandService>.Instance, _mediator);
    }

    private static ProposeConsultationTargetsCommand Propose(TargetParametersDto? parameters = null)
    {
        return new ProposeConsultationTargetsCommand(ConsultationId, PractitionerId, parameters);
    }

    private static PrescribeConsultationTargetsCommand Accept()
    {
        return new PrescribeConsultationTargetsCommand(ConsultationId, PractitionerId,
            PrescriptionOutcome.AcceptedAsProposed);
    }

    private static ConsultationTargetProposalOutcome Success(
        Result<ConsultationTargetProposalOutcome, NutritionalCareError> result)
    {
        return Assert.IsType<Result<ConsultationTargetProposalOutcome, NutritionalCareError>.Success>(result).Value;
    }

    private static void AssertFailure(Result<ConsultationTargetProposalOutcome, NutritionalCareError> result,
        NutritionalCareError expected)
    {
        var failure = Assert.IsType<Result<ConsultationTargetProposalOutcome, NutritionalCareError>.Failure>(result);
        Assert.Equal(expected, failure.Error);
    }
}
