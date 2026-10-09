using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-4. Submit Pre Visit Check In follows the order of the specification: answers first, each to its own
///     error, before the visit is read; then the visit (another patient's does not exist), its state, the hour;
///     then one row per visit, created or edited, and one internal event after the commit.
/// </summary>
public class PreVisitCheckInCommandServiceTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private readonly InMemoryScheduledFollowUps _agenda = new(new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.Utc));

    private SubmitPreVisitCheckInCommand Answer(int followUpId, string? feeling = "Fair", int patientId = PatientId)
    {
        return new SubmitPreVisitCheckInCommand(followUpId, patientId, feeling, ["Dinners"],
            [new PreVisitCheckInQuestionInput("¿Puedo comer fuera los viernes?")]);
    }

    private static MonitoringError ErrorOf(Result<PreVisitCheckInView, MonitoringError> result)
    {
        return Assert.IsType<Result<PreVisitCheckInView, MonitoringError>.Failure>(result).Error;
    }

    private static PreVisitCheckInView ValueOf(Result<PreVisitCheckInView, MonitoringError> result)
    {
        return Assert.IsType<Result<PreVisitCheckInView, MonitoringError>.Success>(result).Value;
    }

    [Theory]
    [InlineData(null, MonitoringError.FeelingRequired)]
    [InlineData("Excelente", MonitoringError.FeelingRequired)]
    public async Task The_feeling_is_validated_before_the_visit_is_read(string? feeling, MonitoringError expected)
    {
        var result = await _agenda.CheckInCommands.Handle(Answer(999, feeling));

        Assert.Equal(expected, ErrorOf(result));
        Assert.Empty(_agenda.CheckIns);
        await _agenda.UnitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
    }

    [Fact]
    public async Task Each_answer_has_its_own_error()
    {
        var unknownDifficulty = await _agenda.CheckInCommands.Handle(
            new SubmitPreVisitCheckInCommand(999, PatientId, "Good", ["Desayunos"]));
        var shortQuestion = await _agenda.CheckInCommands.Handle(
            new SubmitPreVisitCheckInCommand(999, PatientId, "Good", null, [new PreVisitCheckInQuestionInput("ok")]));
        var tooMany = await _agenda.CheckInCommands.Handle(new SubmitPreVisitCheckInCommand(999, PatientId, "Good",
            null, Enumerable.Range(1, 4).Select(i => new PreVisitCheckInQuestionInput($"Pregunta {i}")).ToList()));

        Assert.Equal(MonitoringError.UnknownCheckInDifficulty, ErrorOf(unknownDifficulty));
        Assert.Equal(MonitoringError.InvalidCheckInQuestion, ErrorOf(shortQuestion));
        Assert.Equal(MonitoringError.TooManyQuestions, ErrorOf(tooMany));
    }

    [Fact]
    public async Task A_visit_that_does_not_exist_or_is_another_patients_is_not_found()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(3));

        Assert.Equal(MonitoringError.ScheduledFollowUpNotFound,
            ErrorOf(await _agenda.CheckInCommands.Handle(Answer(999))));
        Assert.Equal(MonitoringError.ScheduledFollowUpNotFound,
            ErrorOf(await _agenda.CheckInCommands.Handle(Answer(visit.Id.Value, patientId: 11))));
        Assert.Empty(_agenda.CheckIns);
    }

    [Fact]
    public async Task A_visit_no_longer_scheduled_refuses_it_before_the_hour_is_checked()
    {
        var completed = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(3));
        completed.MarkCompleted(5, _agenda.Clock.Now);
        var cancelled = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(-1));
        cancelled.Cancel("Discharged");

        Assert.Equal(MonitoringError.FollowUpNotScheduled,
            ErrorOf(await _agenda.CheckInCommands.Handle(Answer(completed.Id.Value))));
        Assert.Equal(MonitoringError.FollowUpNotScheduled,
            ErrorOf(await _agenda.CheckInCommands.Handle(Answer(cancelled.Id.Value))));
    }

    [Fact]
    public async Task At_the_hour_of_the_visit_the_check_in_is_locked()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddHours(1));
        _agenda.Clock.Now = visit.ScheduledFor;

        Assert.Equal(MonitoringError.CheckInLocked,
            ErrorOf(await _agenda.CheckInCommands.Handle(Answer(visit.Id.Value))));
        Assert.Empty(_agenda.CheckIns);
    }

    [Fact]
    public async Task The_first_answer_creates_it_and_the_second_edits_the_same_row()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(3));
        var sentAt = _agenda.Clock.Now;

        var created = ValueOf(await _agenda.CheckInCommands.Handle(Answer(visit.Id.Value, "Hard")));
        _agenda.Clock.Now = sentAt.AddDays(1);
        var edited = ValueOf(await _agenda.CheckInCommands.Handle(Answer(visit.Id.Value, "Good")));

        Assert.Same(created.CheckIn, edited.CheckIn);
        Assert.Same(edited.CheckIn, Assert.Single(_agenda.CheckIns));
        Assert.Equal("Good", edited.CheckIn.Feeling.Value);
        Assert.Equal(sentAt, edited.CheckIn.SubmittedAt);
        Assert.Equal(sentAt.AddDays(1), edited.CheckIn.EditedAt);
        Assert.False(edited.IsLocked);
        await _agenda.UnitOfWork.Received(2).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_the_internal_event_is_published_after_the_commit()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(3));

        await _agenda.CheckInCommands.Handle(Answer(visit.Id.Value, "Hard"));

        var published = Assert.IsType<PreVisitCheckInSubmitted>(Assert.Single(Fakes.Published(_agenda.Mediator)));
        Assert.Equal((visit.Id.Value, PatientId, PractitionerId),
            (published.FollowUpId, published.PatientId, published.PractitionerId));
        Received.InOrder(() =>
        {
            _agenda.UnitOfWork.CompleteAsync(Arg.Any<CancellationToken>());
            _agenda.Mediator.PublishAsync(Arg.Any<PreVisitCheckInSubmitted>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task The_resource_shows_the_origin_and_never_the_ai_generation()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(3));
        await _agenda.CheckInCommands.Handle(new SubmitPreVisitCheckInCommand(visit.Id.Value, PatientId, "Fair",
            ["Dinners", "Weekends"],
            [
                new PreVisitCheckInQuestionInput("¿Puedo comer fuera los viernes?"),
                new PreVisitCheckInQuestionInput("¿Cómo armo cenas con más proteína?", "AiSuggested", 42)
            ]));

        var view = await _agenda.CheckInQueries.Handle(new GetPreVisitCheckInByFollowUpIdQuery(visit.Id.Value));
        var resource = PreVisitCheckInResourceAssembler.ToResource(view!);

        Assert.Equal(["Patient", "AiSuggested"], resource.Questions.Select(q => q.Origin));
        Assert.DoesNotContain(resource.Questions[0].GetType().GetProperties(), p => p.Name.Contains("Ai"));
        Assert.Equal(["Dinners", "Weekends"], resource.Difficulties);
    }

    [Fact]
    public async Task Without_an_answer_the_query_is_null()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(3));

        Assert.Null(await _agenda.CheckInQueries.Handle(new GetPreVisitCheckInByFollowUpIdQuery(visit.Id.Value)));
    }
}
