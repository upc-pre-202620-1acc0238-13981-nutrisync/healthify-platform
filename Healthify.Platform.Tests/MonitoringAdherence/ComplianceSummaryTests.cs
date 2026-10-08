using Healthify.Platform.MonitoringAdherence.Application.Acl;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     RM-2 ("Cumplimiento 5 de 7 días"). The denominator is calendar days and a day without data is Unlogged,
///     never Short (DECISIÓN §12-#7, rule Unlogged Is Not Non Compliant).
/// </summary>
public class ComplianceSummaryTests
{
    private static readonly DateOnly From = new(2026, 9, 12);
    private static readonly DateOnly To = new(2026, 9, 18);

    [Fact]
    public void Five_met_of_seven_calendar_days_with_a_missing_day_counted_as_unlogged()
    {
        var days = new[]
        {
            Day(From, DailyCompliance.Met), Day(From.AddDays(1), DailyCompliance.Met),
            Day(From.AddDays(2), DailyCompliance.Short), Day(From.AddDays(3), DailyCompliance.Met),
            Day(From.AddDays(4), DailyCompliance.Met), Day(From.AddDays(5), DailyCompliance.Met)
            // 18 September has not been evaluated yet.
        };

        var summary = ComplianceSummary.Of(days, From, To);

        Assert.Equal((5, 0, 1, 1, 6, 7), (summary.Met, summary.Exceeded, summary.Short, summary.Unlogged,
            summary.Logged, summary.TotalDays));
    }

    [Fact]
    public void Days_outside_the_range_are_ignored_and_a_re_evaluated_day_counts_once()
    {
        var days = new[]
        {
            Day(From.AddDays(-1), DailyCompliance.Met),
            Day(From, DailyCompliance.Short), Day(From, DailyCompliance.Met),
            Day(To.AddDays(1), DailyCompliance.Exceeded)
        };

        var summary = ComplianceSummary.Of(days, From, To);

        Assert.Equal((1, 0, 6, 7), (summary.Met, summary.Short, summary.Unlogged, summary.TotalDays));
    }

    [Fact]
    public async Task The_facade_counts_through_the_domain_and_degrades_to_null()
    {
        var windows = Substitute.For<IEvaluationWindowQueryService>();
        windows.Handle(Arg.Any<GetDailyComplianceByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns([Day(From, DailyCompliance.Unlogged), Day(To, DailyCompliance.Exceeded)]);
        var facade = new MonitoringContextFacade(windows, Substitute.For<IConsistencyIndexQueryService>(),
            Substitute.For<IReferralQueryService>(), Substitute.For<IScheduledFollowUpQueryService>(),
            Substitute.For<IPreVisitCheckInQueryService>());

        var summary = await facade.GetComplianceSummary(10, From, To);

        Assert.Equal(new(0, 1, 0, 6, 1, 7), summary);
        Assert.Null(await facade.GetComplianceSummary(10, To, From));

        windows.Handle(Arg.Any<GetDailyComplianceByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("down"));
        Assert.Null(await facade.GetComplianceSummary(10, From, To));
    }

    private static DailyCompliance Day(DateOnly date, string outcome)
    {
        return new DailyCompliance(date, outcome, 1500m, 1800m, 1, 3, DateTimeOffset.UtcNow);
    }
}
