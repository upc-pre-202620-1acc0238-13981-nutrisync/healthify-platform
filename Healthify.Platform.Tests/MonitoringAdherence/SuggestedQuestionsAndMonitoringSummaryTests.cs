using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     IA-4. Suggest Questions: the AI gates before the diary is read, the visit must be the patient's, one
///     generation per visit renewed when the check in changes, three logged days, and never the diagnosis.
/// </summary>
public class SuggestedQuestionsTests
{
    private const int FollowUpId = 5;

    private const string Questions = """
        {"questions":["¿Qué puedo almorzar los días de trabajo?","¿Cómo armo una cena más completa?",
                      "¿Qué hago los fines de semana cuando como fuera?"]}
        """;

    private readonly MonitoringAiScenario _scenario = new();
    private readonly ScheduledFollowUp _visit;

    public SuggestedQuestionsTests()
    {
        _visit = Identity.Assign(new ScheduledFollowUp(new ScheduleFollowUpCommand(MonitoringAiScenario.PatientId,
            MonitoringAiScenario.PractitionerId, DateTimeOffset.UtcNow.AddDays(3))), new FollowUpId(FollowUpId));
        _scenario.FollowUps.FindByIdAsync(FollowUpId, Arg.Any<CancellationToken>()).Returns(_visit);
        // The last consultation was the Monday of the week the scenario logs.
        _scenario.NutritionalCare.GetLastCompletedConsultationAt(MonitoringAiScenario.PatientId,
                Arg.Any<CancellationToken>())
            .Returns(new DateTimeOffset(MonitoringAiScenario.WeekStart.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero));
        _scenario.NutritionalCare.GetActiveTargetsByPatientId(MonitoringAiScenario.PatientId,
                Arg.Any<CancellationToken>())
            .Returns(new ActiveTargetsItem(MonitoringAiScenario.PatientId, 2, DateTimeOffset.UtcNow, 1800, 90, 200, 60,
                ["ReduceSalt", "Comer despacio con Ana"], [],
                [new ActiveGuidelineItem("ReduceSalt", null), new ActiveGuidelineItem(null, "Comer despacio con Ana")]));
        _scenario.LogTheWeek();
    }

    private Task<Result<SuggestedQuestionsView, MonitoringAiFailure>> Suggest(int? followUpId = FollowUpId,
        int patientId = MonitoringAiScenario.PatientId)
    {
        return _scenario.Questions.Handle(new SuggestQuestionsCommand(patientId, followUpId));
    }

    private static SuggestedQuestionsView ValueOf(Result<SuggestedQuestionsView, MonitoringAiFailure> result)
    {
        return Assert.IsType<Result<SuggestedQuestionsView, MonitoringAiFailure>.Success>(result).Value;
    }

    private static MonitoringAiFailure FailureOf(Result<SuggestedQuestionsView, MonitoringAiFailure> result)
    {
        return Assert.IsType<Result<SuggestedQuestionsView, MonitoringAiFailure>.Failure>(result).Error;
    }

    private PreVisitCheckIn CheckIn(string feeling)
    {
        return new PreVisitCheckIn(new SubmitPreVisitCheckInCommand(FollowUpId, MonitoringAiScenario.PatientId,
            feeling, ["Dinners"]), _visit, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task One_generation_per_visit_is_served_from_the_cache()
    {
        _scenario.Model.Answers(Questions);

        var first = ValueOf(await Suggest());
        var second = ValueOf(await Suggest());

        Assert.Single(_scenario.Model.Requests);
        Assert.Same(first, second);
        Assert.Equal(3, first.Questions.Count);
        Assert.Equal($"{first.AiGenerationId}-1", first.Questions[0].Id);
        Assert.Equal((MonitoringAiScenario.WeekStart, new DateOnly(2026, 9, 14)), (first.BasedOnFrom, first.BasedOnTo));
    }

    [Fact]
    public async Task A_changed_check_in_generates_again()
    {
        _scenario.Model.Answers(Questions).Answers(Questions);
        var checkIn = CheckIn("Fair");
        _scenario.CheckIns.FindByFollowUpIdAsync(FollowUpId, Arg.Any<CancellationToken>()).Returns(checkIn);

        await Suggest();
        await Suggest();
        Assert.Single(_scenario.Model.Requests);

        checkIn.Edit(new SubmitPreVisitCheckInCommand(FollowUpId, MonitoringAiScenario.PatientId, "Hard",
            ["Dinners", "Weekends"]), _visit, DateTimeOffset.UtcNow);
        await Suggest();

        Assert.Equal(2, _scenario.Model.Requests.Count);
        Assert.Contains("\"feeling\":\"Hard\"", _scenario.Model.Requests[1].UserContent);
    }

    [Fact]
    public async Task The_visit_of_another_patient_does_not_exist()
    {
        var result = await Suggest(patientId: 11);

        Assert.Equal(MonitoringError.ScheduledFollowUpNotFound, FailureOf(result).Error);
        Assert.Empty(_scenario.Model.Requests);
    }

    [Fact]
    public async Task With_the_preference_off_nothing_is_read_nor_generated()
    {
        _scenario.Consent.IsAllowedAsync(MonitoringAiScenario.PatientId, AiFeature.SuggestedQuestions,
            Arg.Any<CancellationToken>()).Returns(false);

        var result = await Suggest();

        Assert.Equal(AiError.AiConsentRequired, FailureOf(result).AiError);
        await _scenario.FollowUps.DidNotReceiveWithAnyArgs().FindByIdAsync(default);
        await _scenario.Intake.DidNotReceiveWithAnyArgs().GetDiaryEntryMoments(default, default, default);
        Assert.Empty(_scenario.Model.Requests);
        Assert.Empty(_scenario.Log.Rows);
    }

    [Fact]
    public async Task Fewer_than_three_logged_days_since_the_last_consultation_is_not_enough_data()
    {
        _scenario.NutritionalCare.GetLastCompletedConsultationAt(MonitoringAiScenario.PatientId,
                Arg.Any<CancellationToken>())
            .Returns(new DateTimeOffset(2026, 9, 13, 10, 0, 0, TimeSpan.Zero)); // only Sunday (unlogged) and Monday

        Assert.Equal(MonitoringError.NotEnoughData, FailureOf(await Suggest()).Error);
        Assert.Empty(_scenario.Model.Requests);
    }

    [Fact]
    public async Task The_input_carries_guideline_codes_and_the_check_in_but_never_the_diagnosis()
    {
        _scenario.Model.Answers(Questions);
        _scenario.CheckIns.FindByFollowUpIdAsync(FollowUpId, Arg.Any<CancellationToken>()).Returns(CheckIn("Fair"));

        ValueOf(await Suggest());

        var input = _scenario.Model.Requests.Single().UserContent;
        Assert.Contains("ReduceSalt", input);
        Assert.DoesNotContain("Ana", input); // a custom guideline is free text: it does not leave
        Assert.Contains("\"difficulties\":[\"Dinners\"]", input);
        await _scenario.NutritionalCare.DidNotReceiveWithAnyArgs().GetActiveDiagnosis(default);
        await _scenario.NutritionalCare.DidNotReceiveWithAnyArgs().GetClinicalEvaluations(default);
        await _scenario.NutritionalCare.DidNotReceiveWithAnyArgs().GetBaselineSummary(default);
    }

    [Fact]
    public async Task Questions_that_mention_a_diagnosis_are_rejected()
    {
        _scenario.Model.Answers("""
            {"questions":["¿Qué ceno para mejorar mi sobrepeso?","¿Cómo armo una cena más completa?",
                          "¿Qué hago los fines de semana?"]}
            """);

        Assert.Equal(AiError.AiOutputRejected, FailureOf(await Suggest()).AiError);
    }
}

/// <summary>
///     IA-5. Summarize Monitoring: only a practitioner with an active care link; the deterministic facts without the
///     patient's AI consent (§12-#5); the consistency index only after a ConsistencyEscalation (Patient Shown First);
///     six hours of cache per patient and range; the practitioner's language.
/// </summary>
public class MonitoringSummaryTests
{
    private const string Text = """{"text":"Cumple sus metas casi todos los días (5 de 7). El jueves registró menos energía de la indicada."}""";

    private readonly MonitoringAiScenario _scenario = new();

    public MonitoringSummaryTests()
    {
        _scenario.LogTheWeek();
    }

    private Task<Result<MonitoringSummaryView, MonitoringAiFailure>> Summarize(DateOnly? from = null,
        DateOnly? to = null, int practitionerId = MonitoringAiScenario.PractitionerId)
    {
        return _scenario.MonitoringSummaries.Handle(new SummarizeMonitoringCommand(MonitoringAiScenario.PatientId,
            practitionerId, from ?? MonitoringAiScenario.WeekStart, to ?? MonitoringAiScenario.WeekStart.AddDays(6)));
    }

    private static MonitoringSummaryView ValueOf(Result<MonitoringSummaryView, MonitoringAiFailure> result)
    {
        return Assert.IsType<Result<MonitoringSummaryView, MonitoringAiFailure>.Success>(result).Value;
    }

    private static MonitoringAiFailure FailureOf(Result<MonitoringSummaryView, MonitoringAiFailure> result)
    {
        return Assert.IsType<Result<MonitoringSummaryView, MonitoringAiFailure>.Failure>(result).Error;
    }

    [Fact]
    public async Task Without_an_active_care_link_nothing_is_read()
    {
        var result = await Summarize(practitionerId: 99);

        Assert.Equal(MonitoringError.ActiveCareLinkRequired, FailureOf(result).Error);
        await _scenario.Windows.DidNotReceiveWithAnyArgs().Handle(Arg.Any<GetDailyComplianceByPatientIdQuery>());
    }

    [Fact]
    public async Task A_range_with_one_end_or_over_a_month_is_invalid()
    {
        var oneEnd = await _scenario.MonitoringSummaries.Handle(new SummarizeMonitoringCommand(
            MonitoringAiScenario.PatientId, MonitoringAiScenario.PractitionerId, MonitoringAiScenario.WeekStart));
        var tooLong = await Summarize(MonitoringAiScenario.WeekStart.AddDays(-40));

        Assert.Equal(MonitoringError.InvalidComplianceRange, FailureOf(oneEnd).Error);
        Assert.Equal(MonitoringError.InvalidComplianceRange, FailureOf(tooLong).Error);
    }

    [Fact]
    public async Task Without_the_patients_AI_consent_the_answer_is_the_facts_alone()
    {
        _scenario.Consent.IsAllowedAsync(MonitoringAiScenario.PatientId, AiFeature.PractitionerMonitoringSummary,
            Arg.Any<CancellationToken>()).Returns(false);

        var view = ValueOf(await Summarize());

        Assert.Null(view.Text);
        Assert.Null(view.AiGenerationId);
        Assert.Equal(nameof(AiError.AiConsentRequired), view.TextUnavailableReason);
        Assert.Equal((5, 7), (view.Facts.MetDays, view.Facts.TotalDays));
        Assert.Equal(["Thu"], view.Facts.ShortWeekdays);
        Assert.Empty(_scenario.Model.Requests);
        Assert.Empty(_scenario.Log.Rows);
    }

    [Fact]
    public async Task The_consistency_index_is_not_in_the_input_before_an_escalation()
    {
        _scenario.Model.Answers(Text);

        var view = ValueOf(await Summarize());

        Assert.Null(view.ConsistencyState);
        Assert.Contains("\"consistencyState\":null", _scenario.Model.Requests.Single().UserContent);
        await _scenario.Consistency.DidNotReceiveWithAnyArgs().Handle(Arg.Any<GetConsistencyIndexByPatientIdQuery>());
    }

    [Fact]
    public async Task After_an_escalation_the_consistency_state_is_part_of_the_input()
    {
        _scenario.NutritionalCare.HasConsistencyEscalation(MonitoringAiScenario.PatientId,
            Arg.Any<CancellationToken>()).Returns(true);
        _scenario.Consistency.Handle(Arg.Any<GetConsistencyIndexByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ConsistencyIndex(MonitoringAiScenario.PatientId));
        _scenario.Model.Answers(Text);

        var view = ValueOf(await Summarize());

        Assert.Equal(ConsistencyState.Normal, view.ConsistencyState);
        Assert.Contains("\"consistencyState\":\"Normal\"", _scenario.Model.Requests.Single().UserContent);
    }

    [Fact]
    public async Task A_text_that_mentions_the_consistency_index_before_an_escalation_is_rejected()
    {
        _scenario.Model.Answers("""{"text":"Cumple sus metas 5 de 7 días; su consistencia de registro es baja."}""");

        Assert.Equal(AiError.AiOutputRejected, FailureOf(await Summarize()).AiError);
    }

    [Fact]
    public async Task The_text_is_cached_six_hours_per_range_in_the_practitioners_language()
    {
        _scenario.Iam.GetPreferredLanguage(MonitoringAiScenario.PractitionerId, Arg.Any<CancellationToken>())
            .Returns("en");
        _scenario.Model.Answers(Text).Answers(Text).Answers(Text);

        var first = ValueOf(await Summarize());
        await Summarize();
        Assert.Single(_scenario.Model.Requests);
        Assert.Contains("Write the text in `en`", _scenario.Model.Requests[0].SystemInstruction);

        await Summarize(MonitoringAiScenario.WeekStart.AddDays(1)); // another range
        Assert.Equal(2, _scenario.Model.Requests.Count);

        _scenario.Clock.Now += TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1);
        await Summarize();
        Assert.Equal(3, _scenario.Model.Requests.Count);
        Assert.NotNull(first.Text);
        // Counted against the practitioner's quota.
        Assert.All(_scenario.Log.Rows, r => Assert.Equal(MonitoringAiScenario.PractitionerId, r.RequestedByUserId));
    }
}
