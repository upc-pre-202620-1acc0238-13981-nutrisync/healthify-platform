namespace Healthify.Platform.MonitoringAdherence.Application.CommandServices;

/// <summary>One cycle of the appointment reminder policy.</summary>
/// <param name="Now">The moment the cycle evaluates the calendar at.</param>
public record SendDueFollowUpRemindersCommand(DateTimeOffset Now);

/// <summary>What one cycle did.</summary>
/// <param name="Evaluated">Visits read.</param>
/// <param name="Sent">Reminders delivered and recorded.</param>
/// <param name="Failed">Reminders that could not be delivered; the next cycle tries them again.</param>
public record FollowUpReminderCycleResult(int Evaluated, int Sent, int Failed)
{
    public static FollowUpReminderCycleResult Idle { get; } = new(0, 0, 0);
}

/// <summary>
///     Policy «When The Visit Is Near» of Monitoring and Adherence: emails the patient (and, when configured, the
///     practitioner) a reminder before each scheduled visit.
/// </summary>
public interface IFollowUpReminderCommandService
{
    Task<FollowUpReminderCycleResult> Handle(SendDueFollowUpRemindersCommand command,
        CancellationToken cancellationToken = default);
}
