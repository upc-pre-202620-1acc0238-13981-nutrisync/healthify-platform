using System.Reflection;
using System.Text.Json;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST.Resources;
using Healthify.Platform.ReadModels.Interfaces.REST.Transform;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.ReadModels;

/// <summary>
///     RM-3. PAC-2 without the consistency index (Patient First Always: it reaches the practitioner only as a
///     ConsistencyEscalation review item), and with the week, the logged days, the weight trend, the last clinical
///     measurement and, per diary entry, the food, the plan adherence and whether it counts.
/// </summary>
public class PatientMonitoringPanelComposerTests
{
    private const int PatientId = 10;

    // Wednesday 16 September 2026.
    private static readonly DateOnly Wednesday = new(2026, 9, 16);

    private readonly INutritionalCareContextFacade _care = Substitute.For<INutritionalCareContextFacade>();
    private readonly IIntakeContextFacade _intake = Substitute.For<IIntakeContextFacade>();
    private readonly IMonitoringContextFacade _monitoring = Substitute.For<IMonitoringContextFacade>();

    public PatientMonitoringPanelComposerTests()
    {
        _monitoring.GetComplianceRange(PatientId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var from = call.ArgAt<DateOnly>(1);
                var days = Enumerable.Range(0, 7).Select(i => new DailyComplianceItem(from.AddDays(i),
                    i < 2 ? "Met" : i == 2 ? "Short" : "Unlogged")).ToList();
                return new ComplianceRangeItem(days, new ComplianceSummaryItem(2, 0, 1, 4, 3, 7));
            });
        _monitoring.GetComplianceSummary(PatientId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(new ComplianceSummaryItem(5, 0, 1, 1, 6, 7));
        _monitoring.GetConsistencyState(PatientId, Arg.Any<CancellationToken>())
            .Returns(new ConsistencyStateItem("Alert", null, null));
        _intake.GetWeightTrendSummary(PatientId, 4, Arg.Any<CancellationToken>())
            .Returns(new WeightTrendSummaryItem(-0.3m, -1.2m, 20));
        _intake.GetDiaryEntries(PatientId, Wednesday, Arg.Any<CancellationToken>()).Returns(
        [
            new DiaryEntryItem(1, new DateTimeOffset(2026, 9, 16, 13, 15, 0, TimeSpan.FromHours(-5)), "Photo",
                "Synced", 7, "Lomo saltado", 300m, 0.8m, 7, "Lomo saltado", 300m, DateTimeOffset.UtcNow, "InPlan",
                true, "Lomo saltado"),
            new DiaryEntryItem(2, new DateTimeOffset(2026, 9, 16, 14, 10, 0, TimeSpan.FromHours(-5)), "Photo",
                "Synced", 8, "Ceviche", 280m, 0.6m, null, null, null, null, "NotAnswered", false, "Ceviche")
        ]);
        _care.GetLatestClinicalMeasurement(PatientId, Arg.Any<CancellationToken>()).Returns(
            new ClinicalMeasurementItem(new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero), 74.2m,
                ["Fasting", "NoShoes"], "en ayunas, sin zapatos"));
    }

    [Fact]
    public async Task The_panel_no_longer_reads_the_consistency_index_and_keeps_the_field_as_null()
    {
        var composition = await Composer().Compose(PatientId, Wednesday);
        var resource = PatientMonitoringPanelResourceAssembler.ToResource(composition);

        await _monitoring.DidNotReceiveWithAnyArgs().GetConsistencyState(default);
#pragma warning disable CS0618 // The deprecated field is what this test checks.
        Assert.Null(resource.Consistency);
#pragma warning restore CS0618
        Assert.NotNull(typeof(PatientMonitoringPanelResource).GetProperty("Consistency")!
            .GetCustomAttribute<ObsoleteAttribute>());

        // Old clients still find the key, always null.
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(resource));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Consistency").ValueKind);
    }

    [Fact]
    public async Task The_week_runs_monday_to_sunday_of_the_panel_day()
    {
        var resource = PatientMonitoringPanelResourceAssembler.ToResource(
            await Composer().Compose(PatientId, Wednesday));

        await _monitoring.Received(1).GetComplianceRange(PatientId, new DateOnly(2026, 9, 14),
            new DateOnly(2026, 9, 20), Arg.Any<CancellationToken>());
        var week = resource.Week!;
        Assert.Equal((new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20)), (week.From, week.To));
        Assert.Equal(7, week.Days.Count);
        Assert.Equal("Unlogged", week.Days[^1].Outcome);
        Assert.Equal((2, 7), (week.MetDays, week.TotalDays));
    }

    [Fact]
    public async Task Logged_days_count_the_last_seven_calendar_days()
    {
        var resource = PatientMonitoringPanelResourceAssembler.ToResource(
            await Composer().Compose(PatientId, Wednesday));

        await _monitoring.Received(1).GetComplianceSummary(PatientId, new DateOnly(2026, 9, 10), Wednesday,
            Arg.Any<CancellationToken>());
        Assert.Equal(new PanelLoggedDaysResource(new DateOnly(2026, 9, 10), Wednesday, 6, 7), resource.LoggedDays);
    }

    [Fact]
    public async Task Trend_measurement_and_diary_details_are_on_the_panel()
    {
        var resource = PatientMonitoringPanelResourceAssembler.ToResource(
            await Composer().Compose(PatientId, Wednesday));

        Assert.Equal(new PanelWeightTrendSummaryResource(4, -0.3m, -1.2m, 20), resource.WeightTrendSummary);
        Assert.Equal(74.2m, resource.LastClinicalMeasurement!.WeightKg);
        Assert.Equal(["Fasting", "NoShoes"], resource.LastClinicalMeasurement.ProtocolChecks);

        Assert.Equal(("Lomo saltado", "InPlan", true),
            (resource.Diary[0].FoodName, resource.Diary[0].PlanAdherence, resource.Diary[0].IsCountedTowardsTargets));
        Assert.Equal(("Ceviche", "NotAnswered", false),
            (resource.Diary[1].FoodName, resource.Diary[1].PlanAdherence, resource.Diary[1].IsCountedTowardsTargets));
    }

    [Fact]
    public async Task A_quiet_context_leaves_its_section_empty()
    {
        var care = Substitute.For<INutritionalCareContextFacade>();
        var composer = new PatientMonitoringPanelComposer(care, Substitute.For<IIntakeContextFacade>(),
            Substitute.For<IMonitoringContextFacade>(), new FixedTimeProvider(DateTimeOffset.UtcNow));

        var resource = PatientMonitoringPanelResourceAssembler.ToResource(await composer.Compose(PatientId, Wednesday));

        Assert.Null(resource.Week);
        Assert.Null(resource.LoggedDays);
        Assert.Null(resource.WeightTrendSummary);
        Assert.Null(resource.LastClinicalMeasurement);
    }

    private PatientMonitoringPanelComposer Composer()
    {
        return new PatientMonitoringPanelComposer(_care, _intake, _monitoring,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero)));
    }
}
