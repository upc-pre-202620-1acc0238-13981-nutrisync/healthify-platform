using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.EventHandlers;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Application.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-6. A plan published before the closed lists, with restrictions «Sin cerdo» and «Sin lactosa»:
///     after the migration it carries <c>["LactoseFree"]</c> plus the legacy <c>["Sin cerdo"]</c>, and the
///     patient keeps receiving both through the whole contract (event, Intake cache, resource, facade). A new
///     version that does not map the legacy one leaves it out and records it; the old version is untouched.
/// </summary>
public class LegacyRestrictionsContractTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    private readonly INutritionPlanRepository _plans = Substitute.For<INutritionPlanRepository>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly NutritionPlan _migrated;

    public LegacyRestrictionsContractTests()
    {
        _migrated = PlanScenario.PublishedBeforeNc6(1, PatientId, PractitionerId, 2, 1,
            ["Prioriza vegetales"], ["Sin cerdo", "Sin lactosa"]);
        _plans.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_migrated);
        _plans.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(_migrated);
    }

    [Fact]
    public void The_migrated_plan_keeps_the_code_and_the_legacy_text_apart()
    {
        Assert.Equal(["LactoseFree"], _migrated.Restrictions);
        Assert.Equal(["Sin cerdo"], _migrated.LegacyRestrictions);
        Assert.True(Assert.Single(_migrated.Guidelines).IsCustom);
    }

    [Fact]
    public async Task The_patient_keeps_receiving_both_restrictions_through_the_published_contract()
    {
        // Intake: the real policy and command service, behind the mediator, as in production.
        var cacheRepository = Substitute.For<IActiveTargetsCacheRepository>();
        ActiveTargetsCache? cached = null;
        await cacheRepository.AddAsync(Arg.Do<ActiveTargetsCache>(c => cached = c), Arg.Any<CancellationToken>());
        var careRelationship = Substitute.For<ICareRelationshipContextFacade>();
        careRelationship.GetActiveCareLinkByPatientId(PatientId, Arg.Any<CancellationToken>())
            .Returns(new CareLinkStatusItem(5, PatientId, PractitionerId, true, true, null));
        var cacheService = new ActiveTargetsCacheCommandService(cacheRepository, Substitute.For<IUnitOfWork>(),
            careRelationship, NullLogger<ActiveTargetsCacheCommandService>.Instance, Substitute.For<IMediator>());
        Fakes.Route(_mediator, new OnActiveTargetsUpdatedIntakeHandler(
            Fakes.ScopeFactoryWith<IActiveTargetsCacheCommandService>(cacheService),
            NullLogger<OnActiveTargetsUpdatedIntakeHandler>.Instance));

        var result = await PlanService().Handle(new PublishActiveTargetsCommand(1));

        Assert.IsType<Result<NutritionPlan, NutritionalCareError>.Success>(result);
        var published = Assert.IsType<ActiveTargetsUpdated>(Assert.Single(Fakes.Published(_mediator)));
        Assert.Equal(["LactoseFree"], published.Restrictions);
        Assert.Equal(["Sin cerdo"], published.LegacyRestrictions);
        Assert.Equal(["Prioriza vegetales"], published.Guidelines);
        Assert.Equal(new GuidelineItem(null, "Prioriza vegetales"), Assert.Single(published.GuidelineItems!));

        Assert.NotNull(cached);
        var resource = ActiveTargetsResourceAssembler.ToResource(cached);
        Assert.Equal(["LactoseFree"], resource.Restrictions);
        Assert.Equal(["Sin cerdo"], resource.LegacyRestrictions);
        Assert.Equal(["Prioriza vegetales"], resource.Guidelines);
        var item = Assert.Single(resource.GuidelineItems);
        Assert.Null(item.Code);
        Assert.Equal("Prioriza vegetales", item.Custom);
    }

    [Fact]
    public async Task The_facade_and_the_record_read_model_carry_both_restrictions()
    {
        var planQueries = Substitute.For<INutritionPlanQueryService>();
        planQueries.Handle(Arg.Any<GetActivePlanByPatientIdQuery>(), Arg.Any<CancellationToken>()).Returns(_migrated);
        var facade = new NutritionalCareContextFacade(planQueries, Substitute.For<IReviewItemQueryService>(),
            Substitute.For<IPatientBaselineQueryService>(), Substitute.For<IConsultationQueryService>(),
            Substitute.For<IClinicalDateProvider>(),
            Substitute.For<INutritionalAssessmentQueryService>(), Substitute.For<INutritionalDiagnosisQueryService>());

        var item = await facade.GetActiveTargetsByPatientId(PatientId);
        var record = PatientRecordResourceAssembler.ToResource(new PatientRecordComposition(PatientId, null, null,
            new PatientRecordAssessmentSection([]), item, new PatientRecordFollowUpSection([], [], null, [])));
        var resource = record.Intervention;

        Assert.NotNull(resource);
        Assert.Equal(["LactoseFree"], resource.Restrictions);
        Assert.Equal(["Sin cerdo"], resource.LegacyRestrictions);
        Assert.Equal("Prioriza vegetales", Assert.Single(resource.GuidelineItems).Custom);
    }

    [Fact]
    public async Task An_adjustment_that_does_not_map_the_legacy_restriction_leaves_it_out_and_records_it()
    {
        NutritionPlan? adjusted = null;
        await _plans.AddAsync(Arg.Do<NutritionPlan>(p => adjusted = Identity.Assign(p, new PlanId(2))),
            Arg.Any<CancellationToken>());

        var result = await PlanService().Handle(new AdjustNutritionPlanCommand(1, PractitionerId, 1700m, 118m, 180m,
            58m, ["ReduceSalt"], ["LactoseFree"], "Ajuste entre consultas", ["Caminar 20 min"]));

        Assert.IsType<Result<NutritionPlan, NutritionalCareError>.Success>(result);
        Assert.NotNull(adjusted);
        Assert.Equal(["LactoseFree"], adjusted.Restrictions);
        Assert.Empty(adjusted.LegacyRestrictions);
        Assert.Equal(["Sin cerdo"], adjusted.DroppedLegacyRestrictions);
        Assert.Equal([Guideline.FromCode("ReduceSalt"), Guideline.CustomText("Caminar 20 min")], adjusted.Guidelines);

        // The previous version is superseded and kept exactly as it was.
        Assert.True(_migrated.IsSuperseded);
        Assert.Equal(["Sin cerdo"], _migrated.LegacyRestrictions);
        Assert.Equal(["LactoseFree"], _migrated.Restrictions);
    }

    [Fact]
    public async Task An_adjustment_with_an_unknown_restriction_code_is_rejected_before_loading()
    {
        var result = await PlanService().Handle(new AdjustNutritionPlanCommand(1, PractitionerId, 1700m, 118m, 180m,
            58m, [], ["Sin cerdo"], "Ajuste entre consultas"));

        var failure = Assert.IsType<Result<NutritionPlan, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.UnknownRestriction, failure.Error);
        await _plans.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    private NutritionPlanCommandService PlanService()
    {
        return new NutritionPlanCommandService(_plans, Substitute.For<INutritionalDiagnosisRepository>(),
            Substitute.For<INutritionalAssessmentRepository>(), Substitute.For<IUnitOfWork>(),
            Substitute.For<IBmrCalculator>(), NullLogger<NutritionPlanCommandService>.Instance, _mediator);
    }
}
