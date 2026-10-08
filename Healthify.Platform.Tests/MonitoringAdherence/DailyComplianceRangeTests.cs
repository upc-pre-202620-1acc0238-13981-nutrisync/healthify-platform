using System.Security.Claims;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.Acl;
using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;
using Healthify.Platform.MonitoringAdherence.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-6. <c>GET /patients/{id}/daily-compliance?from=&amp;to=</c>: every calendar day of the range (at most 31)
///     with its summary; a day never evaluated is Unlogged, never Short (DECISIÓN §12-#7). <c>?date=</c> and no
///     parameters keep the original list for legacy clients.
/// </summary>
public class DailyComplianceRangeTests
{
    private const int PatientId = 10;
    private static readonly DateOnly Monday = new(2026, 9, 8);
    private static readonly DateOnly Sunday = new(2026, 9, 14);

    private readonly IEvaluationWindowQueryService _windows = Substitute.For<IEvaluationWindowQueryService>();

    public DailyComplianceRangeTests()
    {
        _windows.Handle(Arg.Any<GetDailyComplianceByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(Week());
        _windows.Handle(Arg.Any<GetDailyComplianceRangeQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var q = call.Arg<GetDailyComplianceRangeQuery>();
                return new DailyComplianceRange(q.From, q.To, ComplianceSummary.DaysOf(Week(), q.From, q.To),
                    ComplianceSummary.Of(Week(), q.From, q.To));
            });
    }

    [Fact]
    public void Every_calendar_day_of_the_week_is_listed_and_a_day_never_evaluated_is_unlogged()
    {
        var days = ComplianceSummary.DaysOf(Week(), Monday, Sunday);

        Assert.Equal(7, days.Count);
        Assert.Equal(Monday, days[0].Date);
        Assert.Equal(Sunday, days[6].Date);
        Assert.Equal(
        [
            DailyCompliance.Met, DailyCompliance.Met, DailyCompliance.Short, DailyCompliance.Met,
            DailyCompliance.Met, DailyCompliance.Met, DailyCompliance.Unlogged
        ], days.Select(d => d.Outcome));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(-1, false)]
    public void A_range_has_at_most_31_days_in_order(int extraDays, bool valid)
    {
        Assert.Equal(valid, ComplianceSummary.IsValidRange(Monday, Monday.AddDays(extraDays)));
    }

    [Fact]
    public async Task The_range_returns_the_days_and_the_summary_five_of_seven()
    {
        var result = await Controller().GetDailyCompliance(PatientId, null, Monday, Sunday);

        var range = Assert.IsType<DailyComplianceRangeResource>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((Monday, Sunday), (range.From, range.To));
        Assert.Equal(7, range.Days.Count);
        Assert.Equal(DailyCompliance.Unlogged, range.Days[^1].Outcome);
        Assert.Equal(new ComplianceSummaryResource(5, 0, 1, 1, 6, 7), range.Summary);
    }

    [Fact]
    public async Task Legacy_clients_keep_the_original_list()
    {
        var withDate = await Controller().GetDailyCompliance(PatientId, Monday);
        var withoutParameters = await Controller().GetDailyCompliance(PatientId, null);

        Assert.IsAssignableFrom<IEnumerable<DailyComplianceResource>>(
            Assert.IsType<OkObjectResult>(withDate).Value);
        Assert.IsAssignableFrom<IEnumerable<DailyComplianceResource>>(
            Assert.IsType<OkObjectResult>(withoutParameters).Value);
        await _windows.DidNotReceiveWithAnyArgs().Handle(default(GetDailyComplianceRangeQuery)!);
    }

    [Theory]
    [InlineData("2026-09-08", null, null)]
    [InlineData(null, "2026-09-14", null)]
    [InlineData("2026-09-14", "2026-09-08", null)]
    [InlineData("2026-09-01", "2026-10-02", null)]
    [InlineData("2026-09-08", "2026-09-14", "2026-09-10")]
    public async Task An_invalid_range_is_a_bad_request(string? from, string? to, string? date)
    {
        var result = await Controller().GetDailyCompliance(PatientId, Parse(date), Parse(from), Parse(to));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        await _windows.DidNotReceiveWithAnyArgs().Handle(default(GetDailyComplianceRangeQuery)!);
    }

    [Fact]
    public async Task Another_patient_cannot_read_the_range()
    {
        var result = await Controller(PatientId + 1).GetDailyCompliance(PatientId, null, Monday, Sunday);

        Assert.IsNotType<OkObjectResult>(result);
    }

    [Fact]
    public async Task The_facade_publishes_the_range_with_primitives_and_degrades_to_null()
    {
        var facade = new MonitoringContextFacade(_windows, Substitute.For<IConsistencyIndexQueryService>(),
            Substitute.For<IReferralQueryService>(), Substitute.For<IScheduledFollowUpQueryService>(),
            Substitute.For<IPreVisitCheckInQueryService>());

        var range = await facade.GetComplianceRange(PatientId, Monday, Sunday);

        Assert.Equal(7, range!.Days.Count);
        Assert.Equal(new DailyComplianceItem(Sunday, DailyCompliance.Unlogged), range.Days[^1]);
        Assert.Equal(new ComplianceSummaryItem(5, 0, 1, 1, 6, 7), range.Summary);
        Assert.Null(await facade.GetComplianceRange(PatientId, Sunday, Monday));
        Assert.Null(await facade.GetComplianceRange(PatientId, Monday, Monday.AddDays(40)));
    }

    private PatientMonitoringController Controller(int userId = PatientId)
    {
        var localizer = Substitute.For<IStringLocalizer<MonitoringMessages>>();
        localizer[Arg.Any<string>()].Returns(c => new LocalizedString((string)c[0], (string)c[0]));
        return new PatientMonitoringController(_windows, Substitute.For<IDeviationQueryService>(),
            Substitute.For<IConsistencyIndexQueryService>(), Substitute.For<IReferralQueryService>(),
            Substitute.For<ICareRelationshipContextFacade>(), localizer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "Patient")
                    ], "test"))
                }
            }
        };
    }

    private static DateOnly? Parse(string? value)
    {
        return value is null ? null : DateOnly.Parse(value);
    }

    private static DailyCompliance[] Week()
    {
        // Monday to Saturday evaluated; Sunday not yet.
        return
        [
            Day(Monday, DailyCompliance.Met), Day(Monday.AddDays(1), DailyCompliance.Met),
            Day(Monday.AddDays(2), DailyCompliance.Short), Day(Monday.AddDays(3), DailyCompliance.Met),
            Day(Monday.AddDays(4), DailyCompliance.Met), Day(Monday.AddDays(5), DailyCompliance.Met)
        ];
    }

    private static DailyCompliance Day(DateOnly date, string outcome)
    {
        return new DailyCompliance(date, outcome, 1500m, 1800m, 1, 3, DateTimeOffset.UtcNow);
    }
}
