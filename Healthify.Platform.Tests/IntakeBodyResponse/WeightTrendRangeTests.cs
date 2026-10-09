using Healthify.Platform.IntakeBodyResponse.Application.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     IN-5. The weight trend carries the summary of its last weeks: range, slope per week, change over the range
///     and how many readings of the range stayed out of the line. Never a daily figure.
/// </summary>
public class WeightTrendRangeTests
{
    private const int PatientId = 3;
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public void The_summary_covers_the_last_four_weeks_and_counts_the_excluded_readings_in_them()
    {
        var readings = new List<SelfWeighIn>
        {
            // Outside the range: an old excluded reading does not count.
            WeighIns.Reading(1, PatientId, Today.AddDays(-40), 82m, false),
            WeighIns.Reading(2, PatientId, Today.AddDays(-40), 82m)
        };
        // A fasted reading a week, going down 0.3 kg each week, from 28 days ago to today.
        for (var week = 0; week <= 4; week++)
            readings.Add(WeighIns.Reading(10 + week, PatientId, Today.AddDays(-28 + 7 * week), 80m - 0.3m * week));
        readings.Add(WeighIns.Reading(20, PatientId, Today.AddDays(-3), 85m, false));
        readings.Add(WeighIns.Reading(21, PatientId, Today.AddDays(-2), 85m, false));

        var trend = new WeightTrend(PatientId, 1);
        trend.Recalculate(readings);

        var range = trend.SummarizeLastWeeks(Today, 4, readings);

        Assert.Equal(Today.AddDays(-28), range.From);
        Assert.Equal(Today, range.To);
        Assert.Equal(4, range.Weeks);
        Assert.Equal(2, range.ExcludedReadingsCount);
        Assert.Equal(-0.3m, range.Summary.SlopeKgPerWeek);
        Assert.Equal(-1.2m, range.Summary.ChangeKg);
        Assert.Equal(5, range.Summary.PointCount);
    }

    [Fact]
    public void An_excluded_reading_in_the_range_does_not_move_the_slope()
    {
        var readings = new[]
        {
            WeighIns.Reading(1, PatientId, Today.AddDays(-14), 80m),
            WeighIns.Reading(2, PatientId, Today.AddDays(-7), 80m, false),
            WeighIns.Reading(3, PatientId, Today, 80m)
        };
        var trend = new WeightTrend(PatientId, 1);
        trend.Recalculate(readings);

        var range = trend.SummarizeLastWeeks(Today, 4, readings);

        Assert.Equal(0m, range.Summary.SlopeKgPerWeek);
        Assert.Equal(1, range.ExcludedReadingsCount);
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(12, 12)]
    [InlineData(500, 52)]
    public void The_number_of_weeks_is_clamped(int? asked, int expected)
    {
        Assert.Equal(expected, WeightTrendRange.ClampWeeks(asked));
    }

    [Fact]
    public async Task The_query_ends_the_range_today_and_returns_null_without_a_trend()
    {
        var trends = Substitute.For<IWeightTrendRepository>();
        var readings = Substitute.For<ISelfWeighInRepository>();
        var protocol = Substitute.For<ISelfWeighInProtocolProvider>();
        protocol.Current.Returns(SelfWeighInProtocol.Default);
        var service = new WeightTrendQueryService(trends, readings, protocol,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 6, 15, 0, 0, TimeSpan.Zero)));

        Assert.Null(await service.Handle(new GetWeightTrendRangeByPatientIdQuery(PatientId)));

        trends.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(new WeightTrend(PatientId));
        readings.ListByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns([]);

        var view = await service.Handle(new GetWeightTrendRangeByPatientIdQuery(PatientId, 2));

        Assert.NotNull(view);
        Assert.Equal(Today, view.Range.To);
        Assert.Equal(Today.AddDays(-14), view.Range.From);
    }

    [Fact]
    public void The_resource_carries_the_summary_and_still_the_whole_series()
    {
        var readings = new[]
        {
            WeighIns.Reading(1, PatientId, Today.AddDays(-60), 81m),
            WeighIns.Reading(2, PatientId, Today.AddDays(-7), 80m),
            WeighIns.Reading(3, PatientId, Today.AddDays(-6), 90m, false),
            WeighIns.Reading(4, PatientId, Today, 79.7m)
        };
        var trend = new WeightTrend(PatientId, 1);
        trend.Recalculate(readings);
        var view = new Healthify.Platform.IntakeBodyResponse.Application.Internal.WeightTrendView(trend,
            trend.SummarizeLastWeeks(Today, 4, readings));

        var resource = WeightTrendResourceAssembler.ToResource(view);

        Assert.Equal(3, resource.Points.Count);
        Assert.Equal(1, resource.ExcludedReadingsCount);
        Assert.Equal(-0.3m, resource.ChangeKgOverRange);
        Assert.Equal(-0.3m, resource.SlopeKgPerWeek);
        Assert.Equal(Today.AddDays(-28), resource.RangeFrom);
        Assert.Equal(Today, resource.RangeTo);
    }

    [Fact]
    public void The_resource_has_no_field_for_a_single_days_weight()
    {
        // Business rule: Daily Figure Never Exposed As Headline (Subflow 4.5).
        var names = typeof(WeightTrendResource).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Today", StringComparison.OrdinalIgnoreCase)
                                          || n.Contains("Latest", StringComparison.OrdinalIgnoreCase)
                                          || n.Contains("Current", StringComparison.OrdinalIgnoreCase)
                                          || n.Contains("Reading", StringComparison.OrdinalIgnoreCase)
                                          && n != nameof(WeightTrendResource.ExcludedReadingsCount));
    }

    [Fact]
    public async Task The_facade_summary_is_the_same_range_the_endpoint_shows()
    {
        var readings = new[]
        {
            WeighIns.Reading(1, PatientId, Today.AddDays(-14), 80m),
            WeighIns.Reading(2, PatientId, Today, 79.4m)
        };
        var trend = new WeightTrend(PatientId, 1);
        trend.Recalculate(readings);
        var queries = Substitute.For<IWeightTrendQueryService>();
        queries.Handle(Arg.Any<GetWeightTrendRangeByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Healthify.Platform.IntakeBodyResponse.Application.Internal.WeightTrendView(trend,
                trend.SummarizeLastWeeks(Today, 4, readings)));
        var facade = new IntakeContextFacade(Substitute.For<IDiaryEntryQueryService>(), queries, Fakes.Catalog());

        var summary = await facade.GetWeightTrendSummary(PatientId, 4);

        Assert.NotNull(summary);
        Assert.Equal(-0.3m, summary.SlopeKgPerWeek);
        Assert.Equal(-0.6m, summary.ChangeKg);
        Assert.Equal(2, summary.PointCount);
        await queries.Received(1).Handle(new GetWeightTrendRangeByPatientIdQuery(PatientId, 4),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IA2_the_summary_of_an_explicit_week_does_not_depend_on_the_day_it_is_asked()
    {
        var monday = new DateOnly(2026, 9, 7);
        var sunday = monday.AddDays(6);
        // A fasted reading a day: steady before the week, down 0.1 kg a day during it, and a jump after it.
        var readings = Enumerable.Range(0, 16)
            .Select(i => monday.AddDays(i - 6))
            .Select((day, i) => WeighIns.Reading(i + 1, PatientId, day,
                day < monday ? 80m : day <= sunday ? 80m - 0.1m * (day.DayNumber - monday.DayNumber + 1) : 82m))
            .ToList();
        readings.Add(WeighIns.Reading(100, PatientId, monday.AddDays(2), 90m, false)); // excluded, inside the week
        readings.Add(WeighIns.Reading(101, PatientId, sunday.AddDays(2), 90m, false)); // excluded, after it

        var onTime = new WeightTrend(PatientId, 1);
        var onTimeReadings = readings.Where(r => r.LocalDate <= sunday).ToList();
        onTime.Recalculate(onTimeReadings);
        var late = new WeightTrend(PatientId, 1);
        late.Recalculate(readings); // three more days of readings, as when the job runs on Thursday

        var asked = onTime.SummarizeBetween(monday, sunday, onTimeReadings);
        var askedLate = late.SummarizeBetween(monday, sunday, readings);

        Assert.Equal((monday, sunday, 1, 1), (askedLate.From, askedLate.To, askedLate.Weeks,
            askedLate.ExcludedReadingsCount));
        Assert.Equal(asked.Summary, askedLate.Summary);
        Assert.True(askedLate.Summary.ChangeKg < 0);
        // Counted back from Wednesday, the same question would have mixed in the jump after the week.
        Assert.NotEqual(askedLate.Summary, late.SummarizeLastWeeks(sunday.AddDays(3), 1, readings).Summary);
        Assert.Throws<ArgumentException>(() => late.SummarizeBetween(sunday, monday, readings));
    }

    [Fact]
    public async Task IA2_the_facade_answers_the_trend_of_the_range_it_is_given()
    {
        var monday = new DateOnly(2026, 9, 7);
        var readings = Enumerable.Range(0, 7)
            .Select(i => WeighIns.Reading(i + 1, PatientId, monday.AddDays(i), 80m - 0.1m * i)).ToList();
        var trend = new WeightTrend(PatientId, 1);
        trend.Recalculate(readings);
        var trends = Substitute.For<IWeightTrendQueryService>();
        trends.Handle(Arg.Any<GetWeightTrendBetweenByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(c => new Healthify.Platform.IntakeBodyResponse.Application.Internal.WeightTrendView(trend,
                trend.SummarizeBetween(c.Arg<GetWeightTrendBetweenByPatientIdQuery>().From,
                    c.Arg<GetWeightTrendBetweenByPatientIdQuery>().To, readings)));
        var facade = new IntakeContextFacade(Substitute.For<IDiaryEntryQueryService>(), trends,
            Fakes.Catalog());

        var week = await facade.GetWeightTrendSummaryBetween(PatientId, monday, monday.AddDays(6));
        var reversed = await facade.GetWeightTrendSummaryBetween(PatientId, monday.AddDays(6), monday);

        Assert.NotNull(week);
        Assert.Equal(7, week.PointCount);
        Assert.True(week.ChangeKg < 0);
        Assert.Null(reversed);
        await trends.Received(1).Handle(
            new GetWeightTrendBetweenByPatientIdQuery(PatientId, monday, monday.AddDays(6)), Arg.Any<CancellationToken>());
    }
}
