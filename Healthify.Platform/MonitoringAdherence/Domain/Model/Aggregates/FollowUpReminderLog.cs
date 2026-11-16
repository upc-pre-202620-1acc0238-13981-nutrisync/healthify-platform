using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

/// <summary>
///     The record that one appointment reminder email was delivered, so no cycle of the time-driven policy sends it
///     twice.
/// </summary>
/// <remarks>
///     NOTE: technical record, not part of the domain model. A reminder is identified by the visit, who it was
///     for, how long before the visit it is, and the moment of the visit it was sent for. Keeping
///     <see cref="VisitMoment" /> in the key is what lets a rescheduled visit be reminded again: the earlier
///     reminder was about another moment, so it no longer blocks the new one. A visit that is cancelled has no
///     reminders at all, because the policy only reads visits that are still scheduled.
/// </remarks>
public class FollowUpReminderLog
{
    /// <summary>Required by EF Core.</summary>
    protected FollowUpReminderLog()
    {
    }

    public FollowUpReminderLog(int followUpId, string recipient, int leadMinutes, DateTimeOffset visitMoment,
        DateTimeOffset sentAt)
    {
        if (followUpId <= 0) throw new ArgumentOutOfRangeException(nameof(followUpId));
        if (leadMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(leadMinutes));

        FollowUpId = followUpId;
        Recipient = new ReminderRecipient(recipient).Value;
        LeadMinutes = leadMinutes;
        VisitMoment = visitMoment;
        SentAt = sentAt;
    }

    public int Id { get; private set; }

    /// <summary>Cross-aggregate reference to the <see cref="ScheduledFollowUp" />. A plain int, no EF navigation.</summary>
    public int FollowUpId { get; private set; }

    /// <summary><see cref="ReminderRecipient.Patient" /> or <see cref="ReminderRecipient.Practitioner" />.</summary>
    public string Recipient { get; private set; } = string.Empty;

    /// <summary>How many minutes before the visit this reminder is (1440 for «24 h before»).</summary>
    public int LeadMinutes { get; private set; }

    /// <summary>The moment of the visit when this reminder was sent.</summary>
    public DateTimeOffset VisitMoment { get; private set; }

    public DateTimeOffset SentAt { get; private set; }
}
