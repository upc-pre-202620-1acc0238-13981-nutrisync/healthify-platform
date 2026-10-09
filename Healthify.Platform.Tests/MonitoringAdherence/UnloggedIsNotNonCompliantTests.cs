using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     Characterization of the rule Day Without Entries Marked Unlogged Not Non Compliant
///     (Subflow 5.4) and its consequence Only Logged Days Count (Subflow 5.6).
/// </summary>
public class UnloggedIsNotNonCompliantTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);
    private static readonly TargetsSnapshot Snapshot = new(1, 1800m, 119m, 195m, 60m, DateTimeOffset.UtcNow);

    [Fact]
    public void A_day_without_entries_is_unlogged_not_short()
    {
        var day = DailyCompliance.Evaluate(Monday, hasAnyEntry: false, entryCount: 0, observedEnergyKcal: 0m,
            Snapshot);

        Assert.Equal(DailyCompliance.Unlogged, day.Outcome);
        Assert.False(day.IsLogged);
        Assert.False(day.IsOutsideBand);
        Assert.Null(day.Direction);
        Assert.Equal(0m, day.RelativeDeviation);
    }

    [Fact]
    public void A_logged_day_with_little_confirmed_energy_is_short()
    {
        var day = DailyCompliance.Evaluate(Monday, hasAnyEntry: true, entryCount: 2, observedEnergyKcal: 0m,
            Snapshot);

        Assert.Equal(DailyCompliance.Short, day.Outcome);
        Assert.True(day.IsLogged);
    }

    [Theory]
    [InlineData(1980, DailyCompliance.Met)] // +10 % is still inside the band
    [InlineData(1620, DailyCompliance.Met)] // -10 % is still inside the band
    [InlineData(1981, DailyCompliance.Exceeded)]
    [InlineData(1619, DailyCompliance.Short)]
    public void The_band_is_ten_percent_either_side_of_the_energy_target(decimal observed, string expected)
    {
        var day = DailyCompliance.Evaluate(Monday, true, 3, observed, Snapshot);

        Assert.Equal(expected, day.Outcome);
    }

    [Fact]
    public void A_horizon_of_unlogged_days_holds_no_deviation()
    {
        var horizon = Enumerable.Range(0, 7)
            .Select(i => DailyCompliance.Evaluate(Monday.AddDays(i), false, 0, 0m, Snapshot))
            .ToList();

        Assert.Null(Deviation.DetectFrom(new WindowId(1), 10, horizon));
    }

    [Fact]
    public void Unlogged_days_are_not_counted_as_deviating_days()
    {
        var horizon = new List<DailyCompliance>
        {
            DailyCompliance.Evaluate(Monday, true, 2, 1000m, Snapshot),
            DailyCompliance.Evaluate(Monday.AddDays(1), true, 2, 1100m, Snapshot)
        };
        horizon.AddRange(Enumerable.Range(2, 5)
            .Select(i => DailyCompliance.Evaluate(Monday.AddDays(i), false, 0, 0m, Snapshot)));

        var deviation = Deviation.DetectFrom(new WindowId(1), 10, horizon);

        Assert.NotNull(deviation);
        Assert.Equal(2, deviation.LoggedDaysConsidered);
        Assert.Equal(2, deviation.DeviatingDaysConsidered);
    }
}
