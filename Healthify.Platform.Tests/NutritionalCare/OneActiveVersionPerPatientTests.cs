using Cortex.Mediator;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     Characterization of the rule One Active Version Per Patient (Subflow 3.5), reinterpreted by NC-7 as "never
///     two active at once". The stand-alone publication keeps rejecting a second active version (409); publishing
///     from a consultation supersedes the version in force and publishes the new one in the same transaction.
/// </summary>
/// <remarks>
///     Updated on purpose with NC-7: the original cases are kept unchanged; the consultation case was added.
/// </remarks>
public class OneActiveVersionPerPatientTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int DiagnosisId = 7;

    private readonly INutritionPlanRepository _planRepository = Substitute.For<INutritionPlanRepository>();

    private readonly INutritionalDiagnosisRepository _diagnosisRepository =
        Substitute.For<INutritionalDiagnosisRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public OneActiveVersionPerPatientTests()
    {
        _diagnosisRepository.FindByIdAsync(DiagnosisId, Arg.Any<CancellationToken>())
            .Returns(new NutritionalDiagnosis(new IssueDiagnosisCommand(PatientId, PractitionerId, 30,
                "Overweight grade I", "BMI 26.3 kg/m2.")));
    }

    [Fact]
    public async Task Publishing_is_rejected_while_another_version_is_active()
    {
        var draft = PrescribedPlan(planId: 2, version: 2);
        var active = PrescribedPlan(planId: 1, version: 1);
        active.Publish([], []);
        _planRepository.FindByIdAsync(2, Arg.Any<CancellationToken>()).Returns(draft);
        _planRepository.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(active);

        var result = await CreateService().Handle(PublishCommand(planId: 2));

        var failure = Assert.IsType<Result<NutritionPlan, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.PatientAlreadyHasActivePlanVersion, failure.Error);
        Assert.False(draft.IsPublished);
        Assert.True(active.IsActive);
        _planRepository.DidNotReceiveWithAnyArgs().Update(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task Publishing_succeeds_when_no_version_is_active()
    {
        var draft = PrescribedPlan(planId: 1, version: 1);
        _planRepository.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(draft);
        _planRepository.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns((NutritionPlan?)null);

        var result = await CreateService().Handle(PublishCommand(planId: 1));

        var success = Assert.IsType<Result<NutritionPlan, NutritionalCareError>.Success>(result);
        Assert.True(success.Value.IsActive);
        Assert.True(success.Value.IsPublished);
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Superseding_a_published_version_deactivates_it_and_keeps_the_row()
    {
        var plan = PrescribedPlan(planId: 1, version: 1);
        plan.Publish(["Reduce salt"], []);
        Assert.True(plan.IsActive);

        plan.Supersede();

        Assert.False(plan.IsActive);
        Assert.True(plan.IsPublished);
        Assert.NotNull(plan.SupersededAt);
        Assert.Throws<InvalidOperationException>(plan.Supersede);
    }

    [Fact]
    public async Task From_a_consultation_the_version_in_force_is_superseded_never_two_active()
    {
        var scenario = new SecondConsultationAtPublication();
        Assert.Single(new[] { scenario.ActivePlan, scenario.Draft }, p => p.IsActive);

        var result = await scenario.Service.Handle(scenario.Publish());

        Assert.IsType<Result<ConsultationPublicationOutcome, NutritionalCareError>.Success>(result);
        Assert.False(scenario.ActivePlan.IsActive);
        Assert.NotNull(scenario.ActivePlan.SupersededAt);
        Assert.True(scenario.ActivePlan.IsPublished);
        Assert.True(scenario.Draft.IsActive);
        Assert.Single(new[] { scenario.ActivePlan, scenario.Draft }, p => p.IsActive);
        // Both rows change in one save: no committed state ever holds two active versions.
        Assert.Equal(1, scenario.UnitOfWork.DurableSaves);
    }

    private NutritionPlanCommandService CreateService()
    {
        return new NutritionPlanCommandService(_planRepository, _diagnosisRepository,
            Substitute.For<INutritionalAssessmentRepository>(), _unitOfWork, Substitute.For<IBmrCalculator>(),
            NullLogger<NutritionPlanCommandService>.Instance, _mediator);
    }

    private static PublishNutritionPlanCommand PublishCommand(int planId)
    {
        return new PublishNutritionPlanCommand(planId, PractitionerId, ["Prioritize vegetables"], []);
    }

    private static NutritionPlan PrescribedPlan(int planId, int version)
    {
        var basis = new CalculationBasis(new Equation(Equation.MifflinStJeor), ReferenceWeight.Actual, 74.2m,
            1.55m, DeficitStrategy.FixedKcal, 500m, 1500m, 2325m);
        var plan = new NutritionPlan(PatientId, PractitionerId, DiagnosisId, version, basis,
            new TargetProposal(1825m, 119m, 195m, 60m));
        plan.PrescribeTargets(new PrescribedTargets(1825m, 119m, 195m, 60m,
            new PrescriptionOutcome(PrescriptionOutcome.AcceptedAsProposed)));
        return Identity.Assign(plan, new PlanId(planId));
    }
}
