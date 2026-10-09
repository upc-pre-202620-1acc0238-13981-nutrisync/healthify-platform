using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-4. The invariants of the check in: feeling required, at most three questions of each origin, 3 to 300
///     characters, the origin of each question kept, only the patient of the visit writes it, and it can be edited
///     until the hour of the visit.
/// </summary>
public class PreVisitCheckInTests
{
    private const int PatientId = 10;
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static ScheduledFollowUp Visit(DateTimeOffset? scheduledFor = null)
    {
        var followUp = new ScheduledFollowUp(new ScheduleFollowUpCommand(PatientId, 20,
            DateTimeOffset.UtcNow.AddDays(1)));
        typeof(ScheduledFollowUp).GetProperty(nameof(ScheduledFollowUp.ScheduledFor))!.SetValue(followUp,
            scheduledFor ?? Now.AddDays(3));
        return Identity.Assign(followUp, new FollowUpId(7));
    }

    private static SubmitPreVisitCheckInCommand Answer(string? feeling = "Fair", string[]? difficulties = null,
        params PreVisitCheckInQuestionInput[] questions)
    {
        return new SubmitPreVisitCheckInCommand(7, PatientId, feeling, difficulties ?? ["Dinners", "Weekends"],
            questions);
    }

    [Fact]
    public void A_check_in_keeps_what_the_patient_said_and_where_each_question_came_from()
    {
        var checkIn = new PreVisitCheckIn(Answer("fair", ["dinners", "Weekends", "Dinners"],
            new PreVisitCheckInQuestionInput("¿Puedo comer fuera los viernes?"),
            new PreVisitCheckInQuestionInput("¿Cómo armo cenas con más proteína?", "AiSuggested", 42)), Visit(), Now);

        Assert.Equal((7, PatientId, 20), (checkIn.FollowUpId, checkIn.PatientId, checkIn.PractitionerId));
        Assert.Equal(PlanFeeling.Fair, checkIn.Feeling.Value);
        Assert.Equal(["Dinners", "Weekends"], checkIn.Difficulties.Select(d => d.Value));
        Assert.Equal([QuestionOrigin.Patient, QuestionOrigin.AiSuggested], checkIn.Questions.Select(q => q.Origin));
        Assert.Equal([null, 42], checkIn.Questions.Select(q => q.AiGenerationId));
        Assert.Equal(Now, checkIn.SubmittedAt);
        Assert.Null(checkIn.EditedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("Excelente")]
    public void The_feeling_is_required_and_from_the_list(string? feeling)
    {
        Assert.Throws<ArgumentException>(() => new PreVisitCheckIn(Answer(feeling), Visit(), Now));
    }

    [Fact]
    public void Difficulties_and_questions_are_optional()
    {
        var checkIn = new PreVisitCheckIn(new SubmitPreVisitCheckInCommand(7, PatientId, "Good"), Visit(), Now);

        Assert.Empty(checkIn.Difficulties);
        Assert.Empty(checkIn.Questions);
    }

    [Fact]
    public void An_unknown_difficulty_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new PreVisitCheckIn(Answer(difficulties: ["Desayunos"]), Visit(), Now));
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("")]
    public void A_question_has_at_least_three_characters(string text)
    {
        Assert.Throws<ArgumentException>(() => new PatientQuestion(text));
    }

    [Fact]
    public void A_question_has_at_most_three_hundred_characters()
    {
        Assert.Throws<ArgumentException>(() => new PatientQuestion(new string('a', 301)));
        Assert.Equal(300, new PatientQuestion(new string('a', 300)).Text.Length);
    }

    [Fact]
    public void An_unknown_origin_is_refused_and_an_id_without_ai_origin_is_not_kept()
    {
        Assert.Throws<ArgumentException>(() => new PatientQuestion("¿Puedo?", "Nutritionist"));
        Assert.Null(new PatientQuestion("¿Puedo comer fuera?", "Patient", 42).AiGenerationId);
    }

    [Fact]
    public void An_ai_generation_id_beyond_the_int_range_is_kept_whole()
    {
        // ai_generations.id is a bigint: the check in keeps the same long the generation was audited with.
        const long generationId = 5_000_000_000L;

        var question = new PatientQuestion("¿Cómo armo una cena más completa?", "AiSuggested", generationId);
        var json = System.Text.Json.JsonSerializer.Serialize(new List<PatientQuestion> { question });
        var back = System.Text.Json.JsonSerializer.Deserialize<List<PatientQuestion>>(json)!;
        var legacy = System.Text.Json.JsonSerializer.Deserialize<List<PatientQuestion>>(
            """[{"Text":"¿Puedo cenar más tarde?","Origin":"AiSuggested","AiGenerationId":42}]""")!;

        Assert.Equal(generationId, question.AiGenerationId);
        Assert.Equal(generationId, Assert.Single(back).AiGenerationId);
        Assert.Equal(42L, Assert.Single(legacy).AiGenerationId);
    }

    [Fact]
    public void At_most_three_questions_of_the_patient()
    {
        var four = Enumerable.Range(1, 4).Select(i => new PreVisitCheckInQuestionInput($"Pregunta {i}")).ToArray();

        Assert.Throws<ArgumentException>(() => new PreVisitCheckIn(Answer(questions: four), Visit(), Now));
    }

    [Fact]
    public void Three_own_questions_and_three_suggestions_fit()
    {
        var questions = Enumerable.Range(1, 3).Select(i => new PreVisitCheckInQuestionInput($"Mía {i}"))
            .Concat(Enumerable.Range(1, 3).Select(i => new PreVisitCheckInQuestionInput($"IA {i}", "AiSuggested", i)))
            .ToArray();

        Assert.Equal(6, new PreVisitCheckIn(Answer(questions: questions), Visit(), Now).Questions.Count);
    }

    [Fact]
    public void Only_the_patient_of_the_visit_writes_it()
    {
        var command = Answer() with { PatientId = 99 };

        Assert.Throws<InvalidOperationException>(() => new PreVisitCheckIn(command, Visit(), Now));
    }

    [Fact]
    public void It_can_be_edited_until_the_hour_of_the_visit()
    {
        var visit = Visit(Now.AddHours(1));
        var checkIn = new PreVisitCheckIn(Answer("Hard"), visit, Now);

        checkIn.Edit(Answer("Good", ["Cravings"]), visit, Now.AddMinutes(59));

        Assert.Equal(PlanFeeling.Good, checkIn.Feeling.Value);
        Assert.Equal(["Cravings"], checkIn.Difficulties.Select(d => d.Value));
        Assert.Equal(Now, checkIn.SubmittedAt);
        Assert.Equal(Now.AddMinutes(59), checkIn.EditedAt);
        Assert.False(PreVisitCheckIn.IsLockedFor(visit, Now.AddMinutes(59)));
    }

    [Fact]
    public void At_the_hour_of_the_visit_it_is_locked()
    {
        var visit = Visit(Now.AddHours(1));
        var checkIn = new PreVisitCheckIn(Answer(), visit, Now);

        Assert.True(PreVisitCheckIn.IsLockedFor(visit, Now.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => checkIn.Edit(Answer("Good"), visit, Now.AddHours(1)));
        Assert.Equal(PlanFeeling.Fair, checkIn.Feeling.Value);
    }

    [Fact]
    public void A_completed_or_cancelled_visit_locks_it()
    {
        var completed = Visit();
        var checkIn = new PreVisitCheckIn(Answer(), completed, Now);
        completed.MarkCompleted(1, Now);
        var cancelled = Visit();
        cancelled.Cancel("Discharged");

        Assert.True(PreVisitCheckIn.IsLockedFor(completed, Now));
        Assert.Throws<InvalidOperationException>(() => checkIn.Edit(Answer("Good"), completed, Now));
        Assert.Throws<InvalidOperationException>(() => new PreVisitCheckIn(Answer(), cancelled, Now));
    }
}
