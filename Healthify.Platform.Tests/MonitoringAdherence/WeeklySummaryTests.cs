using System.Text.Json;
using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     IA-2. The facts are computed without AI (MA-6, IN-5); the summary is one per patient and week, needs three
///     logged days, and Generate Weekly Summary follows its order: the week, the AI gates and the care link before
///     anything of the diary is read, the existing summary, the facts, the generation, the aggregate.
/// </summary>
public class WeeklySummaryTests
{
    private const string ValidOutput = """
        {"headline":"Cumpliste tus metas 5 de 7 días.",
         "wentWell":["Registraste tus comidas 6 de 7 días.","Tu tendencia de peso bajó 0,3 kg."],
         "watchOut":["El jueves podrías sumar algo más en el almuerzo."]}
        """;

    private readonly MonitoringAiScenario _scenario = new();

    private Task<Result<WeeklySummary, MonitoringAiFailure>> Generate(DateOnly? weekStart = null)
    {
        return _scenario.WeeklySummaries.Handle(
            new GenerateWeeklySummaryCommand(MonitoringAiScenario.PatientId, weekStart ?? MonitoringAiScenario.WeekStart));
    }

    private static MonitoringAiFailure FailureOf(Result<WeeklySummary, MonitoringAiFailure> result)
    {
        return Assert.IsType<Result<WeeklySummary, MonitoringAiFailure>.Failure>(result).Error;
    }

    [Fact]
    public void The_facts_count_calendar_days_and_never_turn_an_unlogged_day_into_a_short_one()
    {
        _scenario.LogDays(MonitoringAiScenario.WeekStart, ["Met", "Met", "Short", "Met", "Short"], withoutDinner: true);
        var moments = _scenario.Moments.GroupBy(m => m.Date)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DateTimeOffset>)g.Select(m => m.LocalTimestamp).ToList());

        // Saturday and Sunday were never evaluated: Unlogged, not Short.
        var facts = MonitoringPeriodFacts.Compute(MonitoringAiScenario.WeekStart,
            MonitoringAiScenario.WeekStart.AddDays(6), _scenario.Evaluated, moments, -0.34m, -0.31m, 3);

        Assert.Equal((7, 3, 0, 2, 2, 5), (facts.TotalDays, facts.MetDays, facts.ExceededDays, facts.ShortDays,
            facts.UnloggedDays, facts.LoggedDays));
        Assert.Equal(["Wed", "Fri"], facts.ShortWeekdays);
        Assert.Equal(MealSlot.Dinner, facts.DominantMissingSlotOnShortDays);
        Assert.Equal(new MealSlotDays(5, 5, 0, 0), facts.MealSlotDays);
        Assert.Equal(-0.3m, facts.WeightChangeKg);
        Assert.Equal(DailyCompliance.Unlogged, facts.Days[6].Outcome);
        Assert.Null(facts.Days[6].EnergyKcal);
    }

    [Fact]
    public async Task The_facts_survive_their_json_column()
    {
        _scenario.LogTheWeek();
        var facts = await _scenario.Facts.ReadAsync(MonitoringAiScenario.PatientId, MonitoringAiScenario.WeekStart,
            MonitoringAiScenario.WeekStart.AddDays(6), true);

        var summary = new WeeklySummary(MonitoringAiScenario.PatientId, facts, "Cumpliste tus metas 5 de 7 días.",
            ["Bien"], [], 1, "es", _scenario.Clock.GetUtcNow());
        var json = typeof(WeeklySummary).GetField("_complianceFactsJson",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(summary);
        var back = JsonSerializer.Deserialize<MonitoringPeriodFacts>((string)json!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal((facts.MetDays, facts.LoggedDays, facts.TotalDays, facts.WeightChangeKg),
            (back.MetDays, back.LoggedDays, back.TotalDays, back.WeightChangeKg));
        Assert.Equal(facts.Days.Select(d => (d.Date, d.Outcome)), back.Days.Select(d => (d.Date, d.Outcome)));
    }

    [Fact]
    public async Task A_summary_covers_one_week_from_Monday_with_three_logged_days()
    {
        _scenario.LogDays(MonitoringAiScenario.WeekStart, ["Met", "Met", "Unlogged", "Unlogged", "Unlogged"]);
        var twoDays = await _scenario.Facts.ReadAsync(MonitoringAiScenario.PatientId, MonitoringAiScenario.WeekStart,
            MonitoringAiScenario.WeekStart.AddDays(6), false);
        var fromTuesday = await _scenario.Facts.ReadAsync(MonitoringAiScenario.PatientId,
            MonitoringAiScenario.WeekStart.AddDays(1), MonitoringAiScenario.WeekStart.AddDays(7), false);

        Assert.Throws<ArgumentException>(() =>
            new WeeklySummary(10, twoDays, "Bien", ["Bien"], [], 1, "es", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() =>
            new WeeklySummary(10, fromTuesday, "Bien", ["Bien"], [], 1, "es", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_week_that_is_not_a_Monday_or_not_over_is_refused_before_anything_is_read()
    {
        var tuesday = await Generate(MonitoringAiScenario.WeekStart.AddDays(1));
        var thisWeek = await Generate(MonitoringAiScenario.WeekStart.AddDays(7)); // ends next Sunday

        Assert.Equal(MonitoringError.InvalidWeekStart, FailureOf(tuesday).Error);
        Assert.Equal(MonitoringError.InvalidWeekStart, FailureOf(thisWeek).Error);
        await _scenario.Consent.DidNotReceiveWithAnyArgs().IsAllowedAsync(default, null!);
    }

    [Fact]
    public async Task With_the_preference_off_nothing_is_read_nor_generated()
    {
        _scenario.LogTheWeek();
        _scenario.Consent.IsAllowedAsync(MonitoringAiScenario.PatientId, AiFeature.WeeklySummary,
            Arg.Any<CancellationToken>()).Returns(false);

        var result = await Generate();

        Assert.Equal(AiError.AiConsentRequired, FailureOf(result).AiError);
        // "Si las desactivas, dejamos de usar tu diario": not a read of the diary, no call, no audit row.
        await _scenario.Intake.DidNotReceiveWithAnyArgs().GetDiaryEntryMoments(default, default, default);
        await _scenario.Intake.DidNotReceiveWithAnyArgs().GetWeightTrendSummaryBetween(default, default, default);
        Assert.Empty(_scenario.Model.Requests);
        Assert.Empty(_scenario.Log.Rows);
        Assert.Empty(_scenario.Summaries.Rows);
    }

    [Fact]
    public async Task With_AI_off_nothing_is_read_nor_generated()
    {
        _scenario.LogTheWeek();
        _scenario.Configuration["Ai:Enabled"] = null; // the default in code

        var result = await Generate();

        Assert.Equal(AiError.AiFeatureDisabled, FailureOf(result).AiError);
        await _scenario.Consent.DidNotReceiveWithAnyArgs().IsAllowedAsync(default, null!);
        await _scenario.Intake.DidNotReceiveWithAnyArgs().GetDiaryEntryMoments(default, default, default);
        Assert.Empty(_scenario.Model.Requests);
        Assert.Empty(_scenario.Summaries.Rows);
    }

    [Fact]
    public async Task Without_an_active_care_link_nothing_is_generated()
    {
        _scenario.LogTheWeek();
        _scenario.Care.GetActiveCareLinkByPatientId(MonitoringAiScenario.PatientId, Arg.Any<CancellationToken>())
            .Returns((Healthify.Platform.CareRelationship.Interfaces.Acl.CareLinkStatusItem?)null);

        Assert.Equal(MonitoringError.ActiveCareLinkRequired, FailureOf(await Generate()).Error);
        Assert.Empty(_scenario.Model.Requests);
    }

    [Fact]
    public async Task Fewer_than_three_logged_days_is_not_enough_data_and_the_model_is_not_called()
    {
        _scenario.LogDays(MonitoringAiScenario.WeekStart, ["Met", "Short", "Unlogged", "Unlogged"]);

        var result = await Generate();

        Assert.Equal(MonitoringError.NotEnoughData, FailureOf(result).Error);
        Assert.Empty(_scenario.Model.Requests);
        Assert.Empty(_scenario.Log.Rows);
    }

    [Fact]
    public async Task The_summary_is_written_from_the_facts_in_the_patients_language_and_kept_once_per_week()
    {
        _scenario.LogTheWeek();
        _scenario.Iam.GetPreferredLanguage(MonitoringAiScenario.PatientId, Arg.Any<CancellationToken>()).Returns("en");
        _scenario.Model.Answers(ValidOutput);

        var first = Assert.IsType<Result<WeeklySummary, MonitoringAiFailure>.Success>(await Generate()).Value;
        var again = Assert.IsType<Result<WeeklySummary, MonitoringAiFailure>.Success>(await Generate()).Value;

        Assert.Same(first, again); // One Summary Per Patient And Week: the second run does not call the model
        Assert.Single(_scenario.Model.Requests);
        Assert.Single(_scenario.Summaries.Rows);
        Assert.Equal(("en", 5, 7, 6), (first.Language, first.ComplianceFacts.MetDays, first.ComplianceFacts.TotalDays,
            first.ComplianceFacts.LoggedDays));
        Assert.Equal(AiGenerationStatus.Succeeded, _scenario.Log.Rows.Single().Status);
        Assert.True(first.AiGenerationId > 0);

        var request = _scenario.Model.Requests.Single();
        Assert.Contains("en", request.SystemInstruction); // {{language}} rendered
        // The facts travel as facts; no identifier and no diagnosis exist in the input.
        Assert.Contains("\"metDays\":5", request.UserContent);
        Assert.DoesNotContain("\"10\"", request.UserContent);
        Assert.DoesNotContain("diagnos", request.UserContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_output_with_invented_numbers_is_rejected_and_nothing_is_stored()
    {
        _scenario.LogTheWeek();
        _scenario.Model.Answers("""
            {"headline":"Cumpliste tus metas 6 de 7 días y bajaste 1,5 kg.","wentWell":["Bien hecho"],"watchOut":[]}
            """);

        var result = await Generate();

        Assert.Equal(AiError.AiOutputRejected, FailureOf(result).AiError);
        Assert.Empty(_scenario.Summaries.Rows);
        Assert.Equal(AiGenerationStatus.Rejected, _scenario.Log.Rows.Single().Status);
    }

    [Fact]
    public async Task An_accusatory_output_is_rejected()
    {
        _scenario.LogTheWeek();
        _scenario.Model.Answers("""
            {"headline":"Cumpliste tus metas 5 de 7 días.","wentWell":["Registraste 6 de 7 días."],
             "watchOut":["El domingo fallaste: no registraste nada."]}
            """);

        Assert.Equal(AiError.AiOutputRejected, FailureOf(await Generate()).AiError);
        Assert.Empty(_scenario.Summaries.Rows);
    }

    [Fact]
    public async Task Expired_summaries_are_deleted_by_retention()
    {
        _scenario.LogTheWeek();
        _scenario.Model.Answers(ValidOutput);
        await Generate();

        var none = await _scenario.WeeklySummaries.Handle(
            new PurgeExpiredWeeklySummariesCommand(_scenario.Clock.GetUtcNow().AddDays(-1)));
        var one = await _scenario.WeeklySummaries.Handle(
            new PurgeExpiredWeeklySummariesCommand(_scenario.Clock.GetUtcNow().AddSeconds(1)));

        Assert.Equal(0, Assert.IsType<Result<int, MonitoringError>.Success>(none).Value);
        Assert.Equal(1, Assert.IsType<Result<int, MonitoringError>.Success>(one).Value);
        Assert.Empty(_scenario.Summaries.Rows);
    }
}
