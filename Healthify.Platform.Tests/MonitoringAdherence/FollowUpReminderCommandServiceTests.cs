using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static Healthify.Platform.Tests.TestSupport.InMemoryFollowUpReminders;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     The appointment reminder policy end to end over in-memory repositories: who gets what and when, no duplicates,
///     cancelled and rescheduled visits, and a mail server that fails. No real email is ever sent.
/// </summary>
public class FollowUpReminderCommandServiceTests
{
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly InMemoryFollowUpReminders _reminders = new();

    [Fact]
    public async Task The_patient_gets_the_day_before_reminder_with_everything_the_email_needs()
    {
        var visit = _reminders.Visit(_now.AddHours(20), PatientId, PreparationInstruction.Fasting);

        var result = await _reminders.RunAt(_now);

        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Failed);
        var message = Assert.Single(_reminders.Sent);
        Assert.Equal(ReminderRecipient.Patient, message.Recipient);
        Assert.Equal(PatientEmail, message.RecipientEmail);
        Assert.Equal("Ana Flores", message.RecipientName);
        Assert.Equal("Ana Flores", message.PatientName);
        Assert.Equal("Marta Quispe", message.PractitionerName);
        Assert.Equal(1440, message.LeadMinutes);
        Assert.Equal(ConsultationModality.InPerson, message.Modality);
        Assert.Equal([PreparationInstruction.Fasting], message.Preparation);
        Assert.False(message.Rescheduled);
        // The visit is written in the time zone of the practice (UTC−5), at the same instant.
        Assert.Equal(TimeSpan.FromHours(-5), message.LocalStart.Offset);
        Assert.Equal(visit.ScheduledFor.UtcDateTime, message.LocalStart.UtcDateTime);

        var log = Assert.Single(_reminders.Logs);
        Assert.Equal(visit.Id.Value, log.FollowUpId);
        Assert.Equal(1440, log.LeadMinutes);
        Assert.Equal(visit.ScheduledFor, log.VisitMoment);
        await _reminders.UnitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_is_sent_before_the_window_opens_or_for_a_visit_beyond_every_window()
    {
        _reminders.Visit(_now.AddHours(30));
        _reminders.Visit(_now.AddDays(10), 11);

        var result = await _reminders.RunAt(_now);

        Assert.Equal(0, result.Sent);
        Assert.Empty(_reminders.Sent);
        Assert.Empty(_reminders.Logs);
    }

    [Fact]
    public async Task Running_the_cycle_again_does_not_send_the_same_reminder_twice()
    {
        _reminders.Visit(_now.AddHours(20));

        await _reminders.RunAt(_now);
        var second = await _reminders.RunAt(_now.AddMinutes(10));
        var third = await _reminders.RunAt(_now.AddMinutes(20));

        Assert.Equal(0, second.Sent);
        Assert.Equal(0, third.Sent);
        Assert.Single(_reminders.Sent);
        Assert.Single(_reminders.Logs);
    }

    [Fact]
    public async Task The_one_hour_reminder_follows_the_day_before_one_for_the_same_visit()
    {
        var visit = _reminders.Visit(_now.AddHours(20));

        await _reminders.RunAt(_now);
        var hourBefore = await _reminders.RunAt(visit.ScheduledFor.AddMinutes(-50));
        var again = await _reminders.RunAt(visit.ScheduledFor.AddMinutes(-40));

        Assert.Equal(1, hourBefore.Sent);
        Assert.Equal(0, again.Sent);
        Assert.Equal([1440, 60], _reminders.Sent.Select(m => m.LeadMinutes).ToArray());
        Assert.Equal(2, _reminders.Logs.Count);
    }

    [Fact]
    public async Task A_cancelled_visit_gets_no_reminder_even_after_one_was_sent_for_it()
    {
        var visit = _reminders.Visit(_now.AddHours(20));
        await _reminders.RunAt(_now);

        await _reminders.Agenda.Commands.Handle(new CancelFollowUpCommand(visit.Id.Value, PractitionerId, "Viaje"));
        var result = await _reminders.RunAt(visit.ScheduledFor.AddMinutes(-50));

        Assert.Equal(0, result.Sent);
        Assert.Single(_reminders.Sent);
    }

    [Fact]
    public async Task A_visit_cancelled_before_its_window_is_never_reminded()
    {
        var visit = _reminders.Visit(_now.AddHours(40));
        await _reminders.Agenda.Commands.Handle(new CancelFollowUpCommand(visit.Id.Value, PractitionerId));

        var result = await _reminders.RunAt(visit.ScheduledFor.AddHours(-23));

        Assert.Equal(0, result.Evaluated);
        Assert.Empty(_reminders.Sent);
    }

    [Fact]
    public async Task A_rescheduled_visit_is_reminded_for_its_new_moment_and_the_email_says_it_moved()
    {
        var visit = _reminders.Visit(_now.AddHours(20));
        await _reminders.RunAt(_now);
        var newMoment = _now.AddDays(3);

        await _reminders.Agenda.Commands.Handle(new RescheduleFollowUpCommand(visit.Id.Value, PractitionerId,
            newMoment));
        var beforeTheNewWindow = await _reminders.RunAt(newMoment.AddHours(-30));
        var inTheNewWindow = await _reminders.RunAt(newMoment.AddHours(-23));
        var duplicate = await _reminders.RunAt(newMoment.AddHours(-22));

        Assert.Equal(0, beforeTheNewWindow.Sent);
        Assert.Equal(1, inTheNewWindow.Sent);
        Assert.Equal(0, duplicate.Sent);
        var message = _reminders.Sent.Last();
        Assert.True(message.Rescheduled);
        Assert.Equal(newMoment.UtcDateTime, message.LocalStart.UtcDateTime);
        Assert.Equal(2, _reminders.Logs.Count);
        Assert.Equal(newMoment, _reminders.Logs.Last().VisitMoment);
    }

    [Fact]
    public async Task A_failed_delivery_is_not_recorded_and_is_tried_again_in_the_next_cycle()
    {
        _reminders.Visit(_now.AddHours(20));
        _reminders.FailNextDeliveries = 1;

        var failed = await _reminders.RunAt(_now);
        var recordedAfterTheFailure = _reminders.Logs.Count;
        var retried = await _reminders.RunAt(_now.AddMinutes(10));

        Assert.Equal(0, failed.Sent);
        Assert.Equal(1, failed.Failed);
        Assert.Equal(0, recordedAfterTheFailure);
        Assert.Equal(1, retried.Sent);
        Assert.Single(_reminders.Sent);
        Assert.Single(_reminders.Logs);
    }

    [Fact]
    public async Task One_failing_recipient_does_not_stop_the_others()
    {
        _reminders.Accounts[11] = new Healthify.Platform.Iam.Interfaces.Acl.UserIdentityItem(11, "luis@example.com",
            "Patient", "Luis", "Rojas");
        _reminders.Languages[11] = "es";
        _reminders.Visit(_now.AddHours(20));
        _reminders.Visit(_now.AddHours(21), 11);
        _reminders.FailNextDeliveries = 1;

        var result = await _reminders.RunAt(_now);

        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Failed);
        Assert.Equal("Luis Rojas", Assert.Single(_reminders.Sent).RecipientName);
    }

    [Fact]
    public async Task Without_mail_configuration_the_cycle_does_nothing_and_does_not_even_read_the_agenda()
    {
        _reminders.Notifier.IsConfigured.Returns(false);
        _reminders.Visit(_now.AddHours(20));

        var result = await _reminders.RunAt(_now);

        Assert.Equal(0, result.Evaluated);
        Assert.Empty(_reminders.Sent);
        await _reminders.Notifier.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task A_notifier_that_sends_nothing_leaves_no_record_so_the_reminder_is_not_lost()
    {
        _reminders.Visit(_now.AddHours(20));
        _reminders.Notifier.SendAsync(Arg.Any<FollowUpReminderMessage>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _reminders.RunAt(_now);

        Assert.Equal(0, result.Sent);
        Assert.Empty(_reminders.Logs);
    }

    [Fact]
    public async Task The_policy_can_be_switched_off()
    {
        var off = new InMemoryFollowUpReminders(("Enabled", "false"));
        off.Visit(_now.AddHours(20));

        var result = await off.RunAt(_now);

        Assert.Equal(0, result.Evaluated);
        Assert.Empty(off.Sent);
    }

    [Fact]
    public async Task The_windows_come_from_configuration()
    {
        var custom = new InMemoryFollowUpReminders(("LeadHours", "48"));
        custom.Visit(_now.AddHours(40));

        var result = await custom.RunAt(_now);

        Assert.Equal(1, result.Sent);
        Assert.Equal(2880, Assert.Single(custom.Sent).LeadMinutes);
    }

    [Fact]
    public async Task The_practitioner_is_also_reminded_when_configured_in_their_own_language()
    {
        var both = new InMemoryFollowUpReminders(("NotifyPractitioner", "true"));
        both.Languages[PractitionerId] = "en";
        both.Visit(_now.AddHours(20));

        var result = await both.RunAt(_now);

        Assert.Equal(2, result.Sent);
        var toPatient = both.Sent.Single(m => m.Recipient == ReminderRecipient.Patient);
        var toPractitioner = both.Sent.Single(m => m.Recipient == ReminderRecipient.Practitioner);
        Assert.Equal(PatientEmail, toPatient.RecipientEmail);
        Assert.Equal("es", toPatient.Language);
        Assert.Equal(PractitionerEmail, toPractitioner.RecipientEmail);
        Assert.Equal("en", toPractitioner.Language);
        Assert.Equal("Ana Flores", toPractitioner.PatientName);
        Assert.Equal(2, both.Logs.Count);
    }

    [Fact]
    public async Task The_language_defaults_to_spanish_when_the_account_has_none()
    {
        _reminders.Languages.Remove(PatientId);
        _reminders.Visit(_now.AddHours(20));

        await _reminders.RunAt(_now);

        Assert.Equal("es", Assert.Single(_reminders.Sent).Language);
    }

    [Fact]
    public async Task A_visit_whose_account_is_gone_is_skipped_and_counted_as_failed()
    {
        _reminders.Accounts.Remove(PatientId);
        _reminders.Visit(_now.AddHours(20));

        var result = await _reminders.RunAt(_now);

        Assert.Equal(0, result.Sent);
        Assert.Equal(1, result.Failed);
        Assert.Empty(_reminders.Sent);
    }

    [Fact]
    public async Task A_delivered_reminder_that_cannot_be_recorded_is_counted_as_failed_without_throwing()
    {
        _reminders.Visit(_now.AddHours(20));
        _reminders.UnitOfWork.CompleteAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        var result = await _reminders.RunAt(_now);

        Assert.Equal(0, result.Sent);
        Assert.Equal(1, result.Failed);
        Assert.Single(_reminders.Sent);
    }

    [Fact]
    public async Task Several_visits_in_one_cycle_each_get_their_own_email()
    {
        for (var id = 11; id <= 13; id++)
        {
            _reminders.Accounts[id] = new Healthify.Platform.Iam.Interfaces.Acl.UserIdentityItem(id,
                $"p{id}@example.com", "Patient", $"Paciente{id}", "Prueba");
            _reminders.Visit(_now.AddHours(10 + id), id);
        }

        var result = await _reminders.RunAt(_now);

        Assert.Equal(3, result.Sent);
        Assert.Equal(["p11@example.com", "p12@example.com", "p13@example.com"],
            _reminders.Sent.Select(m => m.RecipientEmail).Order().ToArray());
    }

    [Fact]
    public async Task A_cancellation_requested_while_sending_stops_the_cycle()
    {
        _reminders.Visit(_now.AddHours(20));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _reminders.Service.Handle(new Healthify.Platform.MonitoringAdherence.Application.CommandServices
                .SendDueFollowUpRemindersCommand(_now), cts.Token));
        Assert.Empty(_reminders.Sent);
    }
}
