using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Services;

/// <summary>A visit as the reminder policy reads it.</summary>
/// <param name="FollowUpId">The visit.</param>
/// <param name="ScheduledFor">Its current moment (after any reschedule).</param>
/// <param name="BookedAt">When it was put on the calendar.</param>
/// <param name="IsScheduled">False once it was cancelled, completed or flagged missed.</param>
public sealed record ReminderCandidate(int FollowUpId, DateTimeOffset ScheduledFor, DateTimeOffset BookedAt,
    bool IsScheduled);

/// <summary>A reminder that was already delivered.</summary>
/// <param name="FollowUpId">The visit.</param>
/// <param name="Recipient">Who it was for.</param>
/// <param name="LeadMinutes">How long before the visit it is.</param>
/// <param name="VisitMoment">The moment of the visit it was sent for.</param>
public sealed record SentReminder(int FollowUpId, string Recipient, int LeadMinutes, DateTimeOffset VisitMoment);

/// <summary>A reminder the policy decided to send now.</summary>
/// <param name="FollowUpId">The visit.</param>
/// <param name="Recipient">Who it is for.</param>
/// <param name="LeadMinutes">The window it belongs to.</param>
/// <param name="VisitMoment">The moment of the visit it is about.</param>
/// <param name="Rescheduled">
///     True when this recipient was already reminded about an earlier moment of the same visit, so the email says
///     the visit moved.
/// </param>
public sealed record PlannedReminder(int FollowUpId, string Recipient, int LeadMinutes, DateTimeOffset VisitMoment,
    bool Rescheduled);

/// <summary>
///     Which appointment reminders are due at a given moment. Pure: no clock, no database, no email.
/// </summary>
/// <remarks>
///     Rules:
///     1. Only a visit that is still scheduled and has not started is reminded. A cancelled visit never is.
///     2. A window (<c>lead</c> minutes before the visit) is open from <c>ScheduledFor − lead</c> until the visit.
///     3. A window that opened before the visit was booked is skipped: nobody needs «tomorrow» as a reminder of
///     something they just arranged for this afternoon.
///     4. When several windows are open at once (the worker was down), only the nearest one is sent.
///     5. Each (visit, recipient, window, moment) is sent once. A reschedule changes the moment, so the new moment
///     is reminded again and the email says that the visit moved.
/// </remarks>
public static class FollowUpReminderPlanner
{
    /// <param name="candidates">Visits to evaluate.</param>
    /// <param name="leadMinutes">The configured windows, in minutes before the visit.</param>
    /// <param name="notifyPractitioner">Whether the practitioner is also reminded.</param>
    /// <param name="now">The moment of evaluation (UTC or any offset).</param>
    /// <param name="alreadySent">The delivered reminders of these visits.</param>
    public static IReadOnlyList<PlannedReminder> Plan(IEnumerable<ReminderCandidate> candidates,
        IReadOnlyCollection<int> leadMinutes, bool notifyPractitioner, DateTimeOffset now,
        IReadOnlyCollection<SentReminder> alreadySent)
    {
        var windows = leadMinutes.Where(m => m > 0).Distinct().OrderBy(m => m).ToList();
        if (windows.Count == 0) return [];

        var recipients = notifyPractitioner
            ? new[] { ReminderRecipient.Patient, ReminderRecipient.Practitioner }
            : [ReminderRecipient.Patient];

        var planned = new List<PlannedReminder>();
        foreach (var visit in candidates)
        {
            if (!visit.IsScheduled || visit.ScheduledFor <= now) continue;

            var lead = windows.FirstOrDefault(w =>
            {
                var opensAt = visit.ScheduledFor - TimeSpan.FromMinutes(w);
                return opensAt <= now && visit.BookedAt <= opensAt;
            });
            if (lead == 0) continue;

            foreach (var recipient in recipients)
            {
                var mine = alreadySent.Where(s => s.FollowUpId == visit.FollowUpId && s.Recipient == recipient)
                    .ToList();
                if (mine.Any(s => s.LeadMinutes == lead && s.VisitMoment == visit.ScheduledFor)) continue;

                var rescheduled = mine.Any(s => s.VisitMoment != visit.ScheduledFor);
                planned.Add(new PlannedReminder(visit.FollowUpId, recipient, lead, visit.ScheduledFor, rescheduled));
            }
        }

        return planned;
    }
}
