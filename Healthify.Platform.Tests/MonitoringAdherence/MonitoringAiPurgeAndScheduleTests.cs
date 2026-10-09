using System.Security.Claims;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Application.Internal.EventHandlers;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;
using Healthify.Platform.MonitoringAdherence.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Resources;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     IA-2/IA-4/IA-5 (§12-#14). Withdrawing the AI consent purges every weekly summary and cache entry of the
///     patient; turning a function off purges what that function generated, and only that.
/// </summary>
public class MonitoringAiPurgeTests : IAsyncLifetime
{
    private const string WeeklyOutput = """
        {"headline":"Cumpliste tus metas 5 de 7 días.","wentWell":["Registraste tus comidas 6 de 7 días."],
         "watchOut":[]}
        """;

    private readonly MonitoringAiScenario _scenario = new();

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task InitializeAsync()
    {
        _scenario.LogTheWeek();
        _scenario.Model.Answers(WeeklyOutput);
        await _scenario.WeeklySummaries.Handle(
            new GenerateWeeklySummaryCommand(MonitoringAiScenario.PatientId, MonitoringAiScenario.WeekStart));
        _scenario.Cache.Set(AiFeature.SuggestedQuestions, MonitoringAiScenario.PatientId, "follow-up:5", "questions",
            TimeSpan.FromDays(1));
        _scenario.Cache.Set(AiFeature.PractitionerMonitoringSummary, MonitoringAiScenario.PatientId, "range",
            "summary", TimeSpan.FromDays(1));
        _scenario.Cache.Set(AiFeature.SuggestedQuestions, 11, "follow-up:6", "another patient", TimeSpan.FromDays(1));
        Assert.Single(_scenario.Summaries.Rows);
    }

    private IServiceScopeFactory Scopes =>
        Fakes.ScopeFactoryWith<IMonitoringAiContentCommandService>(_scenario.Purge);

    private bool Cached(AiFeature feature, int patientId, string key)
    {
        return _scenario.Cache.TryGet<string>(feature, patientId, key, out _);
    }

    [Fact]
    public async Task Withdrawing_the_AI_consent_purges_summaries_and_caches_of_that_patient_only()
    {
        await new OnAiProcessingConsentChangedMonitoringHandler(Scopes,
                NullLogger<OnAiProcessingConsentChangedMonitoringHandler>.Instance)
            .Handle(new AiProcessingConsentChanged(MonitoringAiScenario.PatientId, false, DateTimeOffset.UtcNow),
                CancellationToken.None);

        Assert.Empty(_scenario.Summaries.Rows);
        Assert.False(Cached(AiFeature.SuggestedQuestions, MonitoringAiScenario.PatientId, "follow-up:5"));
        Assert.False(Cached(AiFeature.PractitionerMonitoringSummary, MonitoringAiScenario.PatientId, "range"));
        Assert.True(Cached(AiFeature.SuggestedQuestions, 11, "follow-up:6"));
    }

    [Fact]
    public async Task Granting_the_AI_consent_purges_nothing()
    {
        await new OnAiProcessingConsentChangedMonitoringHandler(Scopes,
                NullLogger<OnAiProcessingConsentChangedMonitoringHandler>.Instance)
            .Handle(new AiProcessingConsentChanged(MonitoringAiScenario.PatientId, true, DateTimeOffset.UtcNow),
                CancellationToken.None);

        Assert.Single(_scenario.Summaries.Rows);
        Assert.True(Cached(AiFeature.SuggestedQuestions, MonitoringAiScenario.PatientId, "follow-up:5"));
    }

    [Fact]
    public async Task Turning_the_weekly_summary_off_purges_the_summaries_only()
    {
        await new OnAiPreferencesChangedMonitoringHandler(Scopes,
                NullLogger<OnAiPreferencesChangedMonitoringHandler>.Instance)
            .Handle(new AiPreferencesChanged(MonitoringAiScenario.PatientId, false, true, true, DateTimeOffset.UtcNow),
                CancellationToken.None);

        Assert.Empty(_scenario.Summaries.Rows);
        Assert.True(Cached(AiFeature.SuggestedQuestions, MonitoringAiScenario.PatientId, "follow-up:5"));
        Assert.True(Cached(AiFeature.PractitionerMonitoringSummary, MonitoringAiScenario.PatientId, "range"));
    }

    [Fact]
    public async Task Turning_the_suggested_questions_off_purges_their_cache_only()
    {
        await new OnAiPreferencesChangedMonitoringHandler(Scopes,
                NullLogger<OnAiPreferencesChangedMonitoringHandler>.Instance)
            .Handle(new AiPreferencesChanged(MonitoringAiScenario.PatientId, true, true, false, DateTimeOffset.UtcNow),
                CancellationToken.None);

        Assert.Single(_scenario.Summaries.Rows);
        Assert.False(Cached(AiFeature.SuggestedQuestions, MonitoringAiScenario.PatientId, "follow-up:5"));
        // IA-5 is the practitioner's: it depends on the consent, not on the patient's preferences.
        Assert.True(Cached(AiFeature.PractitionerMonitoringSummary, MonitoringAiScenario.PatientId, "range"));
    }

    [Fact]
    public async Task A_purge_that_fails_never_throws_out_of_the_handler()
    {
        var failing = Substitute.For<IMonitoringAiContentCommandService>();
        failing.Handle(Arg.Any<PurgeMonitoringAiContentCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        await new OnAiProcessingConsentChangedMonitoringHandler(Fakes.ScopeFactoryWith(failing),
                NullLogger<OnAiProcessingConsentChangedMonitoringHandler>.Instance)
            .Handle(new AiProcessingConsentChanged(MonitoringAiScenario.PatientId, false, DateTimeOffset.UtcNow),
                CancellationToken.None);
    }
}

/// <summary>
///     IA-2. The schedule (cron on the clinical clock, Mondays 06:00 Lima by default), the week each run summarizes,
///     a run that generates nothing while the preference is off, and the GET of the latest summary.
/// </summary>
public class WeeklySummaryScheduleTests
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

    [Fact]
    public void The_default_schedule_is_Monday_at_six_in_Lima()
    {
        var cron = CronSchedule.Parse(WeeklySummaryHostedService.DefaultCron);

        var next = cron.NextAfter(new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.Zero), Lima);

        Assert.Equal(new DateTimeOffset(2026, 9, 21, 6, 0, 0, TimeSpan.FromHours(-5)), next);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 6, 0, 0, TimeSpan.FromHours(-5)),
            cron.PreviousAtOrBefore(new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(7),
                Lima));
    }

    [Theory]
    [InlineData("*/15 * * * *", "2026-09-15T14:07:00Z", "2026-09-15T14:15:00Z")]
    [InlineData("30 20 * * 0", "2026-09-15T14:00:00Z", "2026-09-21T01:30:00Z")] // Sunday 20:30 Lima
    [InlineData("0 6 1 * *", "2026-09-15T14:00:00Z", "2026-10-01T11:00:00Z")]
    public void Steps_days_of_week_and_days_of_month_are_understood(string expression, string after, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected),
            CronSchedule.Parse(expression).NextAfter(DateTimeOffset.Parse(after), Lima));
    }

    [Theory]
    [InlineData("0 6 * *")]
    [InlineData("61 6 * * 1")]
    [InlineData("0 6 * * 8")]
    [InlineData("0 6-4 * * 1")]
    public void An_invalid_expression_is_refused(string expression)
    {
        Assert.Throws<FormatException>(() => CronSchedule.Parse(expression));
    }

    [Theory]
    [InlineData("2026-09-14", "2026-09-07")] // Monday: the week that ended yesterday
    [InlineData("2026-09-15", "2026-09-07")]
    [InlineData("2026-09-13", "2026-08-31")] // Sunday: the current week is not over
    public void Each_run_summarizes_the_most_recent_week_that_is_over(string runDate, string weekStart)
    {
        Assert.Equal(DateOnly.Parse(weekStart), WeeklySummaryHostedService.WeekToSummarize(DateOnly.Parse(runDate)));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task A_run_generates_for_open_windows_and_nothing_while_the_preference_is_off(bool allowed,
        int expected)
    {
        var scenario = new MonitoringAiScenario();
        scenario.LogTheWeek();
        scenario.Model.Answers("""
            {"headline":"Cumpliste tus metas 5 de 7 días.","wentWell":["Registraste 6 de 7 días."],"watchOut":[]}
            """);
        scenario.Consent.IsAllowedAsync(MonitoringAiScenario.PatientId, AiFeature.WeeklySummary,
            Arg.Any<CancellationToken>()).Returns(allowed);
        scenario.Windows.Handle(Arg.Any<GetOpenEvaluationWindowsQuery>(), Arg.Any<CancellationToken>())
            .Returns([new EvaluationWindow(new OpenEvaluationWindowCommand(MonitoringAiScenario.PatientId, 1), 7,
                MonitoringAiScenario.WeekStart)]);
        var scopes = Fakes.ScopeFactoryWith(
            (typeof(IEvaluationWindowQueryService), scenario.Windows),
            (typeof(IWeeklySummaryCommandService), scenario.WeeklySummaries));
        var service = new WeeklySummaryHostedService(scopes,
            new ConfigurationBuilder().AddInMemoryCollection(scenario.Configuration).Build(), scenario.Settings,
            scenario.Clock, NullLogger<WeeklySummaryHostedService>.Instance);

        var generated = await service.RunCycleAsync(new DateOnly(2026, 9, 14), CancellationToken.None);

        Assert.Equal(expected, generated);
        Assert.Equal(expected, scenario.Summaries.Rows.Count);
        Assert.Equal(expected, scenario.Model.Requests.Count);
    }

    [Fact]
    public async Task A_run_three_days_late_still_summarizes_the_figures_of_the_right_week()
    {
        var scenario = new MonitoringAiScenario();
        scenario.LogTheWeek();
        // The days after the week exist too: three more logged days by the time the job runs.
        scenario.LogDays(MonitoringAiScenario.WeekStart.AddDays(7), ["Exceeded", "Exceeded", "Exceeded"]);
        var weekEnd = MonitoringAiScenario.WeekStart.AddDays(6);
        // The trend of the week went down 0.3 kg; counted back from Thursday it would be +1.5 kg.
        scenario.WeightByRange = (from, to) => from == MonitoringAiScenario.WeekStart && to == weekEnd
            ? new Healthify.Platform.IntakeBodyResponse.Interfaces.Acl.WeightTrendSummaryItem(-0.3m, -0.3m, 4)
            : new Healthify.Platform.IntakeBodyResponse.Interfaces.Acl.WeightTrendSummaryItem(1.5m, 1.5m, 7);
        scenario.Clock.Now = new DateTimeOffset(2026, 9, 17, 14, 0, 0, TimeSpan.Zero); // Thursday, 3 days late
        scenario.Model.Answers("""
            {"headline":"Cumpliste tus metas 5 de 7 días.","wentWell":["Tu tendencia de peso bajó 0,3 kg."],
             "watchOut":[]}
            """);
        scenario.Windows.Handle(Arg.Any<GetOpenEvaluationWindowsQuery>(), Arg.Any<CancellationToken>())
            .Returns([new EvaluationWindow(new OpenEvaluationWindowCommand(MonitoringAiScenario.PatientId, 1), 28,
                MonitoringAiScenario.WeekStart)]);
        var scopes = Fakes.ScopeFactoryWith(
            (typeof(IEvaluationWindowQueryService), scenario.Windows),
            (typeof(IWeeklySummaryCommandService), scenario.WeeklySummaries));
        var service = new WeeklySummaryHostedService(scopes,
            new ConfigurationBuilder().AddInMemoryCollection(scenario.Configuration).Build(), scenario.Settings,
            scenario.Clock, NullLogger<WeeklySummaryHostedService>.Instance);

        Assert.Equal(1, await service.RunCycleAsync(new DateOnly(2026, 9, 17), CancellationToken.None));

        var summary = Assert.Single(scenario.Summaries.Rows);
        Assert.Equal((MonitoringAiScenario.WeekStart, weekEnd), (summary.WeekStart, summary.WeekEnd));
        Assert.Equal((5, 0, 6, 7, -0.3m), (summary.ComplianceFacts.MetDays, summary.ComplianceFacts.ExceededDays,
            summary.ComplianceFacts.LoggedDays, summary.ComplianceFacts.TotalDays,
            summary.ComplianceFacts.WeightChangeKg));
        await scenario.Intake.Received(1).GetWeightTrendSummaryBetween(MonitoringAiScenario.PatientId,
            MonitoringAiScenario.WeekStart, weekEnd, Arg.Any<CancellationToken>());
        await scenario.Intake.DidNotReceiveWithAnyArgs().GetWeightTrendSummary(default, default);
    }

    [Fact]
    public async Task The_latest_summary_is_the_patients_own_and_hidden_when_the_function_is_off()
    {
        var scenario = new MonitoringAiScenario();
        var queries = Substitute.For<IWeeklySummaryQueryService>();
        var localizer = Substitute.For<IStringLocalizer<MonitoringMessages>>();
        localizer[Arg.Any<string>()].Returns(c => new LocalizedString((string)c[0], (string)c[0]));
        var aiLocalizer = Substitute.For<IStringLocalizer<AiMessages>>();
        aiLocalizer[Arg.Any<string>()].Returns(c => new LocalizedString((string)c[0], (string)c[0]));
        var controller = new PatientAiSummariesController(queries,
            Substitute.For<ISuggestedQuestionsCommandService>(), Substitute.For<IMonitoringSummaryCommandService>(),
            scenario.Settings, scenario.Consent, localizer, aiLocalizer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, MonitoringAiScenario.PatientId.ToString()),
                        new Claim(ClaimTypes.Role, "Patient")
                    ], "test"))
                }
            }
        };

        Assert.IsType<ForbidResult>(await controller.GetLatestWeeklySummary(11));
        Assert.Equal(StatusCodes.Status404NotFound,
            Assert.IsType<ObjectResult>(await controller.GetLatestWeeklySummary(MonitoringAiScenario.PatientId))
                .StatusCode);

        scenario.Consent.IsAllowedAsync(MonitoringAiScenario.PatientId, AiFeature.WeeklySummary,
            Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(await controller.GetLatestWeeklySummary(MonitoringAiScenario.PatientId))
                .StatusCode);
        // The function off hides even a stored summary: the read is not made.
        await queries.ReceivedWithAnyArgs(1).Handle(Arg.Any<GetLatestWeeklySummaryByPatientIdQuery>());
    }

    [Fact]
    public async Task The_latest_summary_is_served_with_its_facts()
    {
        var scenario = new MonitoringAiScenario();
        scenario.LogTheWeek();
        scenario.Model.Answers("""
            {"headline":"Cumpliste tus metas 5 de 7 días.","wentWell":["Registraste 6 de 7 días."],"watchOut":[]}
            """);
        var summary = ((Healthify.Platform.Shared.Application.Patterns.Result<WeeklySummary, MonitoringAiFailure>
                .Success)await scenario.WeeklySummaries.Handle(
                new GenerateWeeklySummaryCommand(MonitoringAiScenario.PatientId, MonitoringAiScenario.WeekStart)))
            .Value;

        var resource = Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform.WeeklySummaryResourceAssembler
            .ToResource(summary);

        Assert.IsType<WeeklySummaryResource>(resource);
        Assert.Equal((5, 7, 6, -0.3m), (resource.Facts.MetDays, resource.Facts.TotalDays, resource.Facts.LoggedDays,
            resource.Facts.WeightChangeKg));
        Assert.Equal(MonitoringAiScenario.WeekStart.AddDays(6), resource.WeekEnd);
    }
}
