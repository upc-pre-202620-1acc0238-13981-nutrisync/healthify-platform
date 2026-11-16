using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Services;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     Which appointment reminders are due: windows, the visit's state, bookings made after a window opened,
///     duplicates, and a rescheduled visit.
/// </summary>
public class FollowUpReminderPlannerTests
{
    private const int Day = 1440;
    private const int Hour = 60;
    private static readonly DateTimeOffset Visit = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongAgo = Visit.AddDays(-30);

    private static ReminderCandidate Candidate(int id = 1, DateTimeOffset? scheduledFor = null,
        DateTimeOffset? bookedAt = null, bool scheduled = true)
    {
        return new ReminderCandidate(id, scheduledFor ?? Visit, bookedAt ?? LongAgo, scheduled);
    }

    private static IReadOnlyList<PlannedReminder> Plan(DateTimeOffset now, ReminderCandidate candidate,
        int[]? leads = null, bool practitioner = false, params SentReminder[] sent)
    {
        return FollowUpReminderPlanner.Plan([candidate], leads ?? [Day, Hour], practitioner, now, sent);
    }

    [Fact]
    public void Nothing_is_due_before_the_first_window_opens()
    {
        Assert.Empty(Plan(Visit.AddHours(-25), Candidate()));
    }

    [Fact]
    public void The_day_before_window_opens_exactly_24_hours_ahead()
    {
        var planned = Assert.Single(Plan(Visit.AddHours(-24), Candidate()));

        Assert.Equal(Day, planned.LeadMinutes);
        Assert.Equal(ReminderRecipient.Patient, planned.Recipient);
        Assert.Equal(Visit, planned.VisitMoment);
        Assert.False(planned.Rescheduled);
    }

    [Fact]
    public void Between_the_two_windows_only_the_day_window_is_open()
    {
        var planned = Assert.Single(Plan(Visit.AddHours(-5), Candidate()));

        Assert.Equal(Day, planned.LeadMinutes);
    }

    [Fact]
    public void The_one_hour_window_opens_one_hour_ahead()
    {
        var planned = Assert.Single(Plan(Visit.AddMinutes(-59), Candidate()));

        Assert.Equal(Hour, planned.LeadMinutes);
    }

    [Fact]
    public void When_both_windows_are_open_only_the_nearest_is_sent()
    {
        // The worker was down for a day: a «tomorrow» reminder 30 minutes before the visit would be noise.
        var planned = Assert.Single(Plan(Visit.AddMinutes(-30), Candidate()));

        Assert.Equal(Hour, planned.LeadMinutes);
    }

    [Fact]
    public void A_visit_that_already_started_is_not_reminded()
    {
        Assert.Empty(Plan(Visit, Candidate()));
        Assert.Empty(Plan(Visit.AddMinutes(10), Candidate()));
    }

    [Fact]
    public void A_cancelled_or_otherwise_closed_visit_is_never_reminded()
    {
        Assert.Empty(Plan(Visit.AddHours(-24), Candidate(scheduled: false)));
        Assert.Empty(Plan(Visit.AddMinutes(-30), Candidate(scheduled: false)));
    }

    [Fact]
    public void A_window_that_opened_before_the_visit_was_booked_is_skipped()
    {
        // Booked 2 hours before the visit: «tomorrow» never applied, and the one-hour window is still ahead.
        var booked = Visit.AddHours(-2);

        Assert.Empty(Plan(Visit.AddHours(-2), Candidate(bookedAt: booked)));
        var planned = Assert.Single(Plan(Visit.AddMinutes(-60), Candidate(bookedAt: booked)));
        Assert.Equal(Hour, planned.LeadMinutes);
    }

    [Fact]
    public void A_visit_booked_inside_the_last_hour_gets_no_reminder_at_all()
    {
        var booked = Visit.AddMinutes(-20);

        Assert.Empty(Plan(Visit.AddMinutes(-10), Candidate(bookedAt: booked)));
    }

    [Fact]
    public void A_reminder_that_was_already_sent_is_not_sent_again()
    {
        var sent = new SentReminder(1, ReminderRecipient.Patient, Day, Visit);

        Assert.Empty(Plan(Visit.AddHours(-20), Candidate(), sent: sent));
    }

    [Fact]
    public void A_sent_day_reminder_does_not_block_the_one_hour_reminder()
    {
        var sent = new SentReminder(1, ReminderRecipient.Patient, Day, Visit);

        var planned = Assert.Single(Plan(Visit.AddMinutes(-45), Candidate(), sent: sent));
        Assert.Equal(Hour, planned.LeadMinutes);
    }

    [Fact]
    public void When_the_nearest_window_was_already_sent_the_larger_one_is_not_used_as_a_fallback()
    {
        var sent = new SentReminder(1, ReminderRecipient.Patient, Hour, Visit);

        Assert.Empty(Plan(Visit.AddMinutes(-20), Candidate(), sent: sent));
    }

    [Fact]
    public void A_rescheduled_visit_is_reminded_again_for_its_new_moment_and_says_it_moved()
    {
        var newMoment = Visit.AddDays(2);
        var sentForOldMoment = new SentReminder(1, ReminderRecipient.Patient, Day, Visit);

        var planned = Assert.Single(Plan(newMoment.AddHours(-24), Candidate(scheduledFor: newMoment),
            sent: sentForOldMoment));

        Assert.Equal(newMoment, planned.VisitMoment);
        Assert.True(planned.Rescheduled);
    }

    [Fact]
    public void A_visit_moved_closer_is_reminded_with_the_window_that_is_open_now()
    {
        // The visit of Friday 15:00 was moved to the next morning, 3 hours away: the day window is open.
        var newMoment = Visit.AddHours(-21);
        var sentForOldMoment = new SentReminder(1, ReminderRecipient.Patient, Day, Visit);

        var planned = Assert.Single(Plan(newMoment.AddHours(-3), Candidate(scheduledFor: newMoment),
            sent: sentForOldMoment));

        Assert.Equal(Day, planned.LeadMinutes);
        Assert.True(planned.Rescheduled);
    }

    [Fact]
    public void The_practitioner_is_reminded_only_when_asked_for()
    {
        var now = Visit.AddHours(-24);

        Assert.Equal([ReminderRecipient.Patient],
            Plan(now, Candidate()).Select(p => p.Recipient).ToArray());
        Assert.Equal([ReminderRecipient.Patient, ReminderRecipient.Practitioner],
            Plan(now, Candidate(), practitioner: true).Select(p => p.Recipient).ToArray());
    }

    [Fact]
    public void Each_recipient_is_deduplicated_on_its_own()
    {
        var sentToPatient = new SentReminder(1, ReminderRecipient.Patient, Day, Visit);

        var planned = Assert.Single(Plan(Visit.AddHours(-24), Candidate(), null, true, sentToPatient));

        Assert.Equal(ReminderRecipient.Practitioner, planned.Recipient);
        Assert.False(planned.Rescheduled);
    }

    [Fact]
    public void Visits_are_evaluated_independently()
    {
        var now = Visit.AddHours(-24);
        var candidates = new[]
        {
            Candidate(1),
            Candidate(2, Visit.AddDays(3)),
            Candidate(3, scheduled: false)
        };

        var planned = FollowUpReminderPlanner.Plan(candidates, [Day, Hour], false, now, []);

        Assert.Equal([1], planned.Select(p => p.FollowUpId).ToArray());
    }

    [Fact]
    public void No_windows_means_no_reminders()
    {
        Assert.Empty(FollowUpReminderPlanner.Plan([Candidate()], [], false, Visit.AddMinutes(-30), []));
        Assert.Empty(FollowUpReminderPlanner.Plan([Candidate()], [0, -5], false, Visit.AddMinutes(-30), []));
    }

    [Fact]
    public void Offsets_do_not_matter_only_the_instant_does()
    {
        var limaVisit = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(-5)); // 15:00 UTC
        var now = new DateTimeOffset(2026, 11, 19, 16, 0, 0, TimeSpan.FromHours(1)); // 15:00 UTC the day before

        var planned = Assert.Single(FollowUpReminderPlanner.Plan([Candidate(scheduledFor: limaVisit)], [Day, Hour],
            false, now, []));
        Assert.Equal(Day, planned.LeadMinutes);
    }
}
