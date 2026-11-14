using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

namespace Healthify.Platform.MonitoringAdherence.Domain.Repositories;

/// <summary>Reads the visits the reminder policy evaluates and records the reminders it sends.</summary>
public interface IFollowUpReminderRepository
{
    /// <summary>
    ///     The visits still scheduled whose moment falls after <paramref name="after" /> and no later than
    ///     <paramref name="until" />, soonest first.
    /// </summary>
    Task<IReadOnlyList<ScheduledFollowUp>> ListScheduledBetweenAsync(DateTimeOffset after, DateTimeOffset until,
        int batchSize, CancellationToken cancellationToken = default);

    /// <summary>The reminders already delivered for these visits.</summary>
    Task<IReadOnlyList<FollowUpReminderLog>> ListLogsAsync(IReadOnlyCollection<int> followUpIds,
        CancellationToken cancellationToken = default);

    Task AddLogAsync(FollowUpReminderLog log, CancellationToken cancellationToken = default);
}
