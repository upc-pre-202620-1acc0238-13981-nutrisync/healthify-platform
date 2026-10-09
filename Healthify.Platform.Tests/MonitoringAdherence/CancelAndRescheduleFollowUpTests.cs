using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.MonitoringAdherence.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-5. A moment that is not in the future is its own 400 (it used to be 404). Only the practitioner whose
///     agenda the visit is on cancels or reschedules it, only while it is scheduled; rescheduling keeps the
///     pre-visit check in.
/// </summary>
public class CancelAndRescheduleFollowUpTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private readonly InMemoryScheduledFollowUps _agenda = new(new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.Utc));

    private static MonitoringError ErrorOf(Result<ScheduledFollowUp, MonitoringError> result)
    {
        return Assert.IsType<Result<ScheduledFollowUp, MonitoringError>.Failure>(result).Error;
    }

    private static ScheduledFollowUp ValueOf(Result<ScheduledFollowUp, MonitoringError> result)
    {
        return Assert.IsType<Result<ScheduledFollowUp, MonitoringError>.Success>(result).Value;
    }

    private static IStringLocalizer<MonitoringMessages> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<MonitoringMessages>>();
        localizer[Arg.Any<string>()].Returns(c => new LocalizedString((string)c[0], (string)c[0]));
        return localizer;
    }

    [Fact]
    public async Task Scheduling_in_the_past_is_a_bad_request_before_the_link_is_asked()
    {
        var result = await _agenda.Commands.Handle(new ScheduleFollowUpCommand(PatientId, PractitionerId,
            DateTimeOffset.UtcNow.AddMinutes(-5)));

        Assert.Equal(MonitoringError.ScheduledForMustBeInFuture, ErrorOf(result));
        await _agenda.CareRelationship.DidNotReceiveWithAnyArgs().IsCareLinkActive(default, default, default);
        Assert.Empty(_agenda.Rows);
        var http = Assert.IsAssignableFrom<ObjectResult>(
            MonitoringActionResultAssembler.ToScheduledFollowUpResult(result, Localizer()));
        Assert.Equal(StatusCodes.Status400BadRequest, http.StatusCode);
    }

    [Fact]
    public async Task The_practitioner_cancels_a_scheduled_visit_with_a_reason_or_the_default_one()
    {
        var withReason = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(3));
        var withoutReason = _agenda.Add(11, PractitionerId, DateTimeOffset.UtcNow.AddDays(3));

        var first = ValueOf(await _agenda.Commands.Handle(
            new CancelFollowUpCommand(withReason.Id.Value, PractitionerId, " Viaje ")));
        var second = ValueOf(await _agenda.Commands.Handle(
            new CancelFollowUpCommand(withoutReason.Id.Value, PractitionerId)));

        Assert.True(first.State.IsCancelled);
        Assert.Equal("Viaje", first.CancellationReason);
        Assert.NotNull(first.CancelledAt);
        Assert.Equal(ScheduledFollowUp.CancelledByPractitionerReason, second.CancellationReason);
        Assert.Equal(2, Fakes.Published(_agenda.Mediator).OfType<FollowUpCancelled>().Count());
    }

    [Fact]
    public async Task A_reason_over_thirty_characters_is_a_bad_request_before_the_visit_is_read()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(3));

        var result = await _agenda.Commands.Handle(
            new CancelFollowUpCommand(visit.Id.Value, PractitionerId, new string('x', 31)));

        Assert.Equal(MonitoringError.CancellationReasonTooLong, ErrorOf(result));
        Assert.True(visit.IsScheduled);
    }

    [Fact]
    public async Task Only_the_practitioner_whose_agenda_it_is_on()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(3));

        Assert.Equal(MonitoringError.ScheduledFollowUpNotFound,
            ErrorOf(await _agenda.Commands.Handle(new CancelFollowUpCommand(visit.Id.Value, 99))));
        Assert.Equal(MonitoringError.ScheduledFollowUpNotFound,
            ErrorOf(await _agenda.Commands.Handle(new RescheduleFollowUpCommand(visit.Id.Value, 99,
                DateTimeOffset.UtcNow.AddDays(5)))));
        Assert.True(visit.IsScheduled);
        await _agenda.UnitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
    }

    [Fact]
    public async Task A_visit_no_longer_scheduled_is_a_conflict()
    {
        var completed = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(-1));
        completed.MarkCompleted(4, DateTimeOffset.UtcNow);
        var cancelled = _agenda.Add(11, PractitionerId, DateTimeOffset.UtcNow.AddDays(2));
        cancelled.Cancel("Discharged");

        Assert.Equal(MonitoringError.FollowUpNotScheduled,
            ErrorOf(await _agenda.Commands.Handle(new CancelFollowUpCommand(completed.Id.Value, PractitionerId))));
        Assert.Equal(MonitoringError.FollowUpNotScheduled,
            ErrorOf(await _agenda.Commands.Handle(new RescheduleFollowUpCommand(cancelled.Id.Value,
                PractitionerId, DateTimeOffset.UtcNow.AddDays(5)))));
    }

    [Fact]
    public async Task Rescheduling_to_the_past_is_a_bad_request()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(3));
        var before = visit.ScheduledFor;

        var result = await _agenda.Commands.Handle(new RescheduleFollowUpCommand(visit.Id.Value, PractitionerId,
            DateTimeOffset.UtcNow.AddHours(-1)));

        Assert.Equal(MonitoringError.ScheduledForMustBeInFuture, ErrorOf(result));
        Assert.Equal(before, visit.ScheduledFor);
    }

    [Fact]
    public async Task Rescheduling_keeps_the_preparation_unless_a_new_one_is_given()
    {
        var kept = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(3), "Fasting");
        var replaced = _agenda.Add(11, PractitionerId, DateTimeOffset.UtcNow.AddDays(3), "Fasting");
        var newMoment = DateTimeOffset.UtcNow.AddDays(10);

        ValueOf(await _agenda.Commands.Handle(new RescheduleFollowUpCommand(kept.Id.Value, PractitionerId,
            newMoment)));
        ValueOf(await _agenda.Commands.Handle(new RescheduleFollowUpCommand(replaced.Id.Value, PractitionerId,
            newMoment, ["EmptyBladder", "LightClothing"])));
        var unknown = await _agenda.Commands.Handle(new RescheduleFollowUpCommand(replaced.Id.Value, PractitionerId,
            newMoment, ["Ayunas"]));

        Assert.Equal(newMoment, kept.ScheduledFor);
        Assert.Equal(["Fasting"], kept.Preparation.Select(p => p.Value));
        Assert.Equal(["EmptyBladder", "LightClothing"], replaced.Preparation.Select(p => p.Value));
        Assert.Equal(MonitoringError.UnknownPreparationInstruction, ErrorOf(unknown));
        Assert.Equal(2, Fakes.Published(_agenda.Mediator).OfType<FollowUpRescheduled>().Count());
    }

    [Fact]
    public async Task Rescheduling_keeps_the_check_in_and_its_lock_follows_the_new_moment()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddHours(2));
        await _agenda.CheckInCommands.Handle(new SubmitPreVisitCheckInCommand(visit.Id.Value, PatientId, "Fair"));
        _agenda.Clock.Now = visit.ScheduledFor.AddMinutes(-1);

        await _agenda.Commands.Handle(new RescheduleFollowUpCommand(visit.Id.Value, PractitionerId,
            DateTimeOffset.UtcNow.AddDays(7)));
        _agenda.Clock.Now = DateTimeOffset.UtcNow.AddDays(1);
        var view = await _agenda.CheckInQueries.Handle(new GetPreVisitCheckInByFollowUpIdQuery(visit.Id.Value));

        Assert.NotNull(view);
        Assert.Equal("Fair", view.CheckIn.Feeling.Value);
        Assert.False(view.IsLocked);
        Assert.Single(_agenda.CheckIns);
    }
}
