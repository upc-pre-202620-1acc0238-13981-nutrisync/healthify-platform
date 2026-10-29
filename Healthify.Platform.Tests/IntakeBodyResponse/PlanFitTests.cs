using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Lexicon;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     "Was it in your plan?" suggested without AI: the restrictions of the plan first (real lexicon), then the
///     energy of the portion against what is left today, with its margin.
/// </summary>
public class PlanFitTests
{
    private const int PatientId = 10;
    private static readonly DateOnly Today = new(2026, 9, 15);
    private static readonly IRestrictionLexicon Lexicon = EmbeddedRestrictionLexicon.Instance;

    private readonly IActiveTargetsCacheRepository _targets = Substitute.For<IActiveTargetsCacheRepository>();
    private readonly IDiaryEntryRepository _diary = Substitute.For<IDiaryEntryRepository>();
    private readonly IFoodCatalogContextFacade _catalog = Substitute.For<IFoodCatalogContextFacade>();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero));

    public PlanFitTests()
    {
        _targets.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(Targets(["ShellfishFree"]));
        _diary.ListByPatientIdAsync(PatientId, Today, Arg.Any<CancellationToken>()).Returns(_ => [Eaten(1000m)]);
        // 1000 g of arroz con pollo at 121 kcal / 100 g: 1210 kcal eaten, 590 kcal left of 1800.
        _catalog.GetReferenceFoodById(1, Arg.Any<CancellationToken>())
            .Returns(new ReferenceFoodItem(1, "Arroz con pollo", 121m, 5.2m, 15m, 4m, false));
        _catalog.GetReferenceFoodById(2, Arg.Any<CancellationToken>())
            .Returns(new ReferenceFoodItem(2, "Ceviche", 110m, 18m, 6m, 1.5m, false));
        _catalog.GetReferenceFoodById(3, Arg.Any<CancellationToken>())
            .Returns(new ReferenceFoodItem(3, "Pechuga de pollo a la plancha", 165m, 31m, 0m, 3.6m, false));
    }

    [Fact]
    public void A_food_that_breaks_a_restriction_is_suggested_off_the_plan_whatever_its_energy()
    {
        var fit = PlanFitRules.Evaluate(["Arroz con langostinos"], 200m, new RemainingTargets(1000m, 0, 0, 0),
            ["ShellfishFree", "GlutenFree"], Lexicon);

        Assert.Equal((PlanAdherence.OffPlan, PlanFitReason.BreaksRestriction), (fit.SuggestedAdherence, fit.Reason));
        Assert.Equal(["ShellfishFree"], fit.ViolatedRestrictions);
    }

    [Fact]
    public void An_exception_of_the_lexicon_is_not_a_broken_restriction()
    {
        var fit = PlanFitRules.Evaluate(["Batido con leche de almendras"], 200m, null, ["LactoseFree"], Lexicon);

        Assert.True(fit.SuggestsInPlan);
        Assert.Empty(fit.ViolatedRestrictions);
    }

    [Fact]
    public void Every_name_of_the_dish_is_checked()
    {
        // The catalog says «Ceviche»; the AI saw «Ceviche de langostinos».
        var fit = PlanFitRules.Evaluate(["Ceviche", "Ceviche de langostinos"], 300m, null, ["ShellfishFree"],
            Lexicon);

        Assert.Equal(["ShellfishFree"], fit.ViolatedRestrictions);
    }

    [Theory]
    [InlineData(690, true)] // exactly 100 kcal above the 590 left: still in the plan
    [InlineData(691, false)]
    public void A_portion_is_suggested_off_the_plan_only_when_clearly_above_what_is_left(int kcal, bool inPlan)
    {
        var fit = PlanFitRules.Evaluate(["Pollo a la plancha"], kcal, new RemainingTargets(590m, 0, 0, 0), [],
            Lexicon);

        Assert.Equal(inPlan, fit.SuggestsInPlan);
        Assert.Equal(inPlan ? PlanFitReason.Fits : PlanFitReason.ExceedsRemaining, fit.Reason);
        Assert.Equal((decimal?)590m, fit.RemainingKcal);
    }

    [Fact]
    public void Without_the_day_the_energy_is_not_judged()
    {
        var fit = PlanFitRules.Evaluate(["Pollo a la plancha"], 5000m, null, [], Lexicon);

        Assert.Equal((PlanAdherence.InPlan, (decimal?)null), (fit.SuggestedAdherence, fit.RemainingKcal));
    }

    [Fact]
    public async Task The_query_weighs_each_candidate_against_the_plan_and_what_is_left_today()
    {
        var fits = Success(await Service().Handle(new GetPlanFitsQuery(PatientId, Today,
        [
            new PlanFitCandidate(2, 300m, ["Ceviche de langostinos"]),
            new PlanFitCandidate(3, 200m),
            new PlanFitCandidate(3, 500m),
            new PlanFitCandidate(99, 100m)
        ])));

        Assert.Equal(4, fits.Count);
        Assert.Equal((PlanAdherence.OffPlan, PlanFitReason.BreaksRestriction), (fits[0]!.SuggestedAdherence,
            fits[0]!.Reason));
        // 200 g of chicken: 330 kcal of the 590 left.
        Assert.Equal((PlanAdherence.InPlan, 330m, (decimal?)590m),
            (fits[1]!.SuggestedAdherence, fits[1]!.EnergyKcal, fits[1]!.RemainingKcal));
        // 500 g: 825 kcal, more than 100 above what is left.
        Assert.Equal(PlanFitReason.ExceedsRemaining, fits[2]!.Reason);
        // Not in the catalog: nothing to suggest.
        Assert.Null(fits[3]);
    }

    [Fact]
    public async Task Without_a_published_plan_there_is_no_suggestion()
    {
        _targets.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns((ActiveTargetsCache?)null);

        var result = await Service().Handle(new GetPlanFitsQuery(PatientId, Today, [new PlanFitCandidate(3, 200m)]));

        Assert.Equal(IntakeError.ActiveTargetsCacheNotFound,
            Assert.IsType<Result<IReadOnlyList<PlanFit?>, IntakeError>.Failure>(result).Error);
    }

    [Fact]
    public async Task A_day_that_is_not_today_on_the_device_is_refused()
    {
        var result = await Service().Handle(new GetPlanFitsQuery(PatientId, Today.AddDays(-3),
            [new PlanFitCandidate(3, 200m)]));

        Assert.Equal(IntakeError.InvalidLocalDate,
            Assert.IsType<Result<IReadOnlyList<PlanFit?>, IntakeError>.Failure>(result).Error);
    }

    [Fact]
    public async Task Without_the_day_the_diary_is_not_read()
    {
        var fits = Success(await Service().Handle(new GetPlanFitsQuery(PatientId, null,
            [new PlanFitCandidate(3, 500m)])));

        Assert.True(fits.Single()!.SuggestsInPlan);
        await _diary.DidNotReceiveWithAnyArgs().ListByPatientIdAsync(default, default, default);
    }

    private PlanFitQueryService Service()
    {
        return new PlanFitQueryService(_targets, _diary, _catalog, Lexicon, _clock);
    }

    private static IReadOnlyList<PlanFit?> Success(Result<IReadOnlyList<PlanFit?>, IntakeError> result)
    {
        return Assert.IsType<Result<IReadOnlyList<PlanFit?>, IntakeError>.Success>(result).Value;
    }

    private static ActiveTargetsCache Targets(IReadOnlyList<string> restrictions)
    {
        return new ActiveTargetsCache(new RefreshActiveTargetsCacheCommand(PatientId, 2,
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), 1800m, 100m, 200m, 60m, [], restrictions));
    }

    private static DiaryEntry Eaten(decimal grams)
    {
        var entry = new DiaryEntry(PatientId,
            new LocalTimestamp(new DateTimeOffset(2026, 9, 15, 13, 0, 0, TimeSpan.FromHours(-5))),
            new Provenance(Provenance.Manual), new SyncState(SyncState.Synced));
        entry.ConfirmDirectly(new ConfirmedEstimate(1, grams, DateTimeOffset.UtcNow),
            new PlanAdherence(PlanAdherence.InPlan));
        return entry;
    }
}
