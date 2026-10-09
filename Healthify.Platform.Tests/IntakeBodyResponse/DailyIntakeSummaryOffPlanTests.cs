using Healthify.Platform.IntakeBodyResponse.Application.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>MA-1 on the Intake facade: off-plan meals count, and are described, never judged.</summary>
public class DailyIntakeSummaryOffPlanTests
{
    private const int PatientId = 1;
    private const int PizzaId = 40;
    private const int RiceId = 41;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Two_portions_of_pizza_off_the_plan_add_their_energy_to_the_day()
    {
        var summary = await Summarize(
            Manual(RiceId, 200m, PlanAdherence.InPlan),
            Manual(PizzaId, 2 * 107m, PlanAdherence.OffPlan));

        // Rice 130 kcal/100 g * 200 g = 260; pizza 266 kcal/100 g * 214 g = 569.24
        Assert.Equal(829.24m, summary.EnergyKcal);
        Assert.Equal(1, summary.OffPlanEntryCount);
        Assert.Equal(569.24m, summary.OffPlanEnergyKcal);
        Assert.Equal(2, summary.EntryCount);
    }

    [Fact]
    public async Task Historical_one_tap_entries_count_as_logged_and_off_plan_with_no_energy()
    {
        var legacy = DiaryEntry.LegacyOffPlan(PatientId, new LocalTimestamp(DateTimeOffset.UtcNow.AddHours(-1)),
            new SyncState(SyncState.Synced));

        var summary = await Summarize(legacy);

        Assert.True(summary.HasAnyEntry);
        Assert.Equal(1, summary.OffPlanEntryCount);
        Assert.Equal(0m, summary.EnergyKcal);
        Assert.Equal(0m, summary.OffPlanEnergyKcal);
    }

    [Fact]
    public async Task An_unconfirmed_proposal_is_not_intake_whatever_it_is()
    {
        var waiting = new DiaryEntry(PatientId, new LocalTimestamp(DateTimeOffset.UtcNow.AddHours(-1)),
            new Provenance(Provenance.Photo), new SyncState(SyncState.Synced));
        waiting.ProposeEstimate(new ProposedEstimate(PizzaId, 300m, new Confidence(0.8m), DateTimeOffset.UtcNow));

        var summary = await Summarize(waiting);

        Assert.True(summary.HasAnyEntry);
        Assert.Equal(0m, summary.EnergyKcal);
        Assert.Equal(0, summary.OffPlanEntryCount);
    }

    private static async Task<Healthify.Platform.IntakeBodyResponse.Interfaces.Acl.DailyIntakeSummaryItem>
        Summarize(params DiaryEntry[] entries)
    {
        var queryService = Substitute.For<IDiaryEntryQueryService>();
        queryService.Handle(Arg.Any<GetDiaryEntriesByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(entries.AsEnumerable());

        var facade = new IntakeContextFacade(queryService, Substitute.For<IWeightTrendQueryService>(),
            Fakes.Catalog(Fakes.Food(PizzaId, "Pizza", 266m), Fakes.Food(RiceId, "Arroz", 130m)));

        var summary = await facade.GetDailyIntakeSummary(PatientId, Today);
        Assert.NotNull(summary);
        return summary;
    }

    private static DiaryEntry Manual(int foodId, decimal grams, string answer)
    {
        var entry = new DiaryEntry(PatientId, new LocalTimestamp(DateTimeOffset.UtcNow.AddHours(-1)),
            new Provenance(Provenance.Manual), new SyncState(SyncState.Synced));
        entry.ConfirmDirectly(new ConfirmedEstimate(foodId, grams, DateTimeOffset.UtcNow), new PlanAdherence(answer));
        return entry;
    }
}
