using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-2. A visit ends Completed (a consultation was published for it) or Cancelled. Cancel and Reschedule
///     exist in the domain without endpoints (MA-5).
/// </summary>
public class FollowUpStateTransitionsTests
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 9, 18, 15, 45, 0, TimeSpan.Zero);

    [Fact]
    public void A_scheduled_visit_is_completed_by_the_consultation()
    {
        var followUp = Scheduled();

        Assert.True(followUp.MarkCompleted(77, PublishedAt));

        Assert.Equal(FollowUpState.Completed, followUp.State.Value);
        Assert.Equal(77, followUp.CompletedByConsultationId);
        Assert.Equal(PublishedAt, followUp.CompletedAt);
    }

    [Fact]
    public void Completing_twice_changes_nothing()
    {
        var followUp = Scheduled();
        followUp.MarkCompleted(77, PublishedAt);

        Assert.False(followUp.MarkCompleted(78, PublishedAt.AddHours(1)));
        Assert.Equal(77, followUp.CompletedByConsultationId);
    }

    [Fact]
    public void A_visit_flagged_missed_before_the_consultation_was_published_is_completed_and_keeps_its_history()
    {
        var followUp = Scheduled();
        typeof(ScheduledFollowUp).GetProperty(nameof(ScheduledFollowUp.ScheduledFor))!
            .SetValue(followUp, PublishedAt.AddHours(-1));
        Assert.True(followUp.MarkMissed(PublishedAt.AddMinutes(-40)));

        Assert.True(followUp.MarkCompleted(77, PublishedAt));

        Assert.Equal(FollowUpState.Completed, followUp.State.Value);
        Assert.NotNull(followUp.MissedAt);
    }

    [Fact]
    public void A_completed_visit_is_never_flagged_missed()
    {
        var followUp = Scheduled();
        followUp.MarkCompleted(77, PublishedAt);

        Assert.False(followUp.MarkMissed(DateTimeOffset.UtcNow.AddYears(1)));
        Assert.Equal(FollowUpState.Completed, followUp.State.Value);
    }

    [Fact]
    public void A_cancelled_visit_is_not_completed()
    {
        var followUp = Scheduled();
        followUp.Cancel("Discharged");

        Assert.Throws<InvalidOperationException>(() => followUp.MarkCompleted(77, PublishedAt));
        Assert.Equal(FollowUpState.Cancelled, followUp.State.Value);
    }

    [Fact]
    public void Cancelling_records_when_and_why()
    {
        var followUp = Scheduled();

        followUp.Cancel("  Discharged ");

        Assert.Equal(FollowUpState.Cancelled, followUp.State.Value);
        Assert.Equal("Discharged", followUp.CancellationReason);
        Assert.NotNull(followUp.CancelledAt);
        Assert.False(followUp.IsScheduled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A reason far longer than thirty characters")]
    public void A_cancellation_needs_a_short_reason(string reason)
    {
        Assert.Throws<ArgumentException>(() => Scheduled().Cancel(reason));
    }

    [Fact]
    public void Only_a_scheduled_visit_is_cancelled_or_rescheduled()
    {
        var followUp = Scheduled();
        followUp.MarkCompleted(77, PublishedAt);

        Assert.Throws<InvalidOperationException>(() => followUp.Cancel("Discharged"));
        Assert.Throws<InvalidOperationException>(() => followUp.Reschedule(DateTimeOffset.UtcNow.AddDays(5)));
    }

    [Fact]
    public void Rescheduling_moves_the_visit_to_another_future_moment()
    {
        var followUp = Scheduled();
        var later = DateTimeOffset.UtcNow.AddDays(10);

        followUp.Reschedule(later);

        Assert.Equal(later, followUp.ScheduledFor);
        Assert.True(followUp.IsScheduled);
        Assert.Throws<ArgumentException>(() => followUp.Reschedule(DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    private static ScheduledFollowUp Scheduled()
    {
        return new ScheduledFollowUp(new ScheduleFollowUpCommand(10, 20, DateTimeOffset.UtcNow.AddDays(3)));
    }
}
