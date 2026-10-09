using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     RM-2/IN-5. "−0,3 kg/sem · Tendencia · 4 semanas": a least squares slope over the smoothed points of the
///     range, never a single day's reading.
/// </summary>
public class WeightTrendSummaryTests
{
    private static readonly DateOnly Start = new(2026, 8, 21);

    [Fact]
    public void A_steady_loss_of_three_tenths_a_week_is_minus_0_3()
    {
        // One point a week for 4 weeks, going down 0.3 kg each week.
        var points = Enumerable.Range(0, 5)
            .Select(w => new WeightTrendPoint(Start.AddDays(7 * w), 76.0m - 0.3m * w))
            .ToList();

        var summary = WeightTrendSummary.Of(points, Start);

        Assert.Equal(-0.3m, summary.SlopeKgPerWeek);
        Assert.Equal(-1.2m, summary.ChangeKg);
        Assert.Equal(5, summary.PointCount);
    }

    [Fact]
    public void Daily_noise_around_a_line_still_gives_its_slope()
    {
        var noise = new[] { 0.1m, -0.1m, 0.05m, -0.05m, 0m, 0.1m, -0.1m };
        var points = Enumerable.Range(0, 28)
            .Select(d => new WeightTrendPoint(Start.AddDays(d), 76.0m + 0.6m / 28m * d + noise[d % noise.Length]))
            .ToList();

        var slope = WeightTrendSummary.Of(points, Start).SlopeKgPerWeek;

        Assert.NotNull(slope);
        Assert.InRange(slope.Value, 0.12m, 0.18m);
    }

    [Fact]
    public void Only_the_points_of_the_range_count()
    {
        var points = new[]
        {
            new WeightTrendPoint(Start.AddDays(-30), 90m),
            new WeightTrendPoint(Start, 76m),
            new WeightTrendPoint(Start.AddDays(14), 75.4m)
        };

        var summary = WeightTrendSummary.Of(points, Start);

        Assert.Equal(2, summary.PointCount);
        Assert.Equal(-0.3m, summary.SlopeKgPerWeek);
        Assert.Equal(-0.6m, summary.ChangeKg);
    }

    [Fact]
    public void With_fewer_than_two_points_there_is_no_direction()
    {
        var summary = WeightTrendSummary.Of([new WeightTrendPoint(Start, 76m)], Start);

        Assert.Null(summary.SlopeKgPerWeek);
        Assert.Null(summary.ChangeKg);
        Assert.Equal(1, summary.PointCount);
        Assert.Equal(0, WeightTrendSummary.Of([], Start).PointCount);
    }
}
