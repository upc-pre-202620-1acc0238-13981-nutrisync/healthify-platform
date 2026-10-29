using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     A patient never has two active plan versions at once. Publishing from a consultation supersedes the
///     version in force and publishes the new one in the same transaction.
/// </summary>
public class OneActiveVersionPerPatientTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int DiagnosisId = 7;

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
