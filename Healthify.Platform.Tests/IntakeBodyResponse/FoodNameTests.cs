using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>IN-1. <c>DiaryEntryResource.FoodName</c>, resolved once per food through the catalog.</summary>
public class FoodNameTests
{
    [Fact]
    public async Task Names_are_resolved_once_per_distinct_food_and_unknown_foods_are_skipped()
    {
        var catalog = Fakes.Catalog(Fakes.Food(12, "Ceviche", 120m), Fakes.Food(15, "Lomo saltado", 180m));
        var service = new DiaryEntryQueryService(Substitute.For<IDiaryEntryRepository>(), catalog);

        var names = await service.Handle(new GetFoodNamesQuery([12, 12, 15, 99]));

        Assert.Equal("Ceviche", names[12]);
        Assert.Equal("Lomo saltado", names[15]);
        Assert.False(names.ContainsKey(99));
        await catalog.Received(1).GetReferenceFoodById(12, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_resource_shows_the_confirmed_food_over_the_proposed_one()
    {
        var entry = new DiaryEntry(1, new LocalTimestamp(DateTimeOffset.UtcNow.AddHours(-1)),
            new Provenance(Provenance.Photo), new SyncState(SyncState.Synced));
        entry.ProposeEstimate(new ProposedEstimate(12, 320m, new Confidence(0.8m), DateTimeOffset.UtcNow));
        Identity.Assign(entry, new DiaryEntryId(3));
        var names = new Dictionary<int, string> { [12] = "Ceviche", [15] = "Lomo saltado" };

        var waiting = Assert.Single(DiaryEntryResourceAssembler.ToResources([entry], names));
        Assert.Equal("Ceviche", waiting.FoodName);
        Assert.False(waiting.IsCountedTowardsTargets);
        Assert.Equal(PlanAdherence.NotAnswered, waiting.PlanAdherence);

        entry.AdjustProposedEstimate(15, 300m, new PlanAdherence(PlanAdherence.OffPlan));

        var confirmed = Assert.Single(DiaryEntryResourceAssembler.ToResources([entry], names));
        Assert.Equal("Lomo saltado", confirmed.FoodName);
        Assert.True(confirmed.IsCountedTowardsTargets);
        Assert.Equal(PlanAdherence.OffPlan, confirmed.PlanAdherence);
    }

    [Fact]
    public void Historical_off_plan_entries_still_read_as_off_plan()
    {
        var entry = Identity.Assign(DiaryEntry.LegacyOffPlan(1, new LocalTimestamp(DateTimeOffset.UtcNow),
            new SyncState(SyncState.Synced)), new DiaryEntryId(4));

        var resource = DiaryEntryResourceAssembler.ToResource(entry);

        Assert.Equal(Provenance.OffPlan, resource.Provenance);
        Assert.Equal(PlanAdherence.OffPlan, resource.PlanAdherence);
        Assert.Null(resource.FoodName);
    }
}
