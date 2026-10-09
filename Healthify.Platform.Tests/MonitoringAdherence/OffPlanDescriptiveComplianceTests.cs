using System.Text.Json;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>MA-1. Off-plan is described on the day and never moves Met, Exceeded or Short.</summary>
public class OffPlanDescriptiveComplianceTests
{
    private static readonly DateOnly Thursday = new(2026, 9, 10);
    private static readonly TargetsSnapshot Snapshot = new(1, 1800m, 119m, 195m, 60m, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(1800, DailyCompliance.Met)]
    [InlineData(1000, DailyCompliance.Short)]
    [InlineData(2500, DailyCompliance.Exceeded)]
    public void The_outcome_depends_only_on_total_confirmed_energy(decimal observed, string expected)
    {
        var withoutOffPlan = DailyCompliance.Evaluate(Thursday, true, 3, observed, Snapshot);
        var mostlyOffPlan = DailyCompliance.Evaluate(Thursday, true, 3, observed, Snapshot,
            offPlanEntryCount: 2, offPlanEnergyKcal: observed * 0.8m);

        Assert.Equal(expected, withoutOffPlan.Outcome);
        Assert.Equal(expected, mostlyOffPlan.Outcome);
        Assert.Equal(2, mostlyOffPlan.OffPlanEntryCount);
    }

    [Fact]
    public void An_unlogged_day_carries_no_off_plan_figures()
    {
        var day = DailyCompliance.Evaluate(Thursday, false, 0, 0m, Snapshot);

        Assert.Equal(DailyCompliance.Unlogged, day.Outcome);
        Assert.Equal(0, day.OffPlanEntryCount);
        Assert.Equal(0m, day.OffPlanEnergyKcal);
    }

    [Fact]
    public void Days_stored_before_MA_1_still_deserialize_with_zero_off_plan()
    {
        const string storedBeforeMa1 =
            """[{"Date":"2026-09-10","Outcome":"Met","ObservedEnergyKcal":1800,"TargetEnergyKcal":1800,"PlanVersion":1,"EntryCount":3,"EvaluatedAt":"2026-09-10T22:00:00+00:00"}]""";

        var day = Assert.Single(JsonSerializer.Deserialize<List<DailyCompliance>>(storedBeforeMa1)!);

        Assert.Equal(DailyCompliance.Met, day.Outcome);
        Assert.Equal(0, day.OffPlanEntryCount);
        Assert.Equal(0m, day.OffPlanEnergyKcal);
    }

    [Fact]
    public void The_new_figures_round_trip_through_the_stored_json()
    {
        var day = DailyCompliance.Evaluate(Thursday, true, 3, 1800m, Snapshot, 1, 569.24m);

        var restored = Assert.Single(JsonSerializer.Deserialize<List<DailyCompliance>>(
            JsonSerializer.Serialize(new List<DailyCompliance> { day }))!);

        Assert.Equal(1, restored.OffPlanEntryCount);
        Assert.Equal(569.24m, restored.OffPlanEnergyKcal);
        Assert.True(restored.SaysTheSameAs(day));
    }

    [Fact]
    public void A_change_in_off_plan_figures_is_a_change_worth_recording()
    {
        var before = DailyCompliance.Evaluate(Thursday, true, 3, 1800m, Snapshot, 0, 0m);
        var after = DailyCompliance.Evaluate(Thursday, true, 3, 1800m, Snapshot, 1, 500m);

        Assert.False(after.SaysTheSameAs(before));
    }
}
