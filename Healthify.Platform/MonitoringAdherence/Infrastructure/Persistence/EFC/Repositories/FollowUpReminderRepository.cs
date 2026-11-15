using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Persistence.EFC.Repositories;

public class FollowUpReminderRepository(AppDbContext context) : IFollowUpReminderRepository
{
    /// <summary>Compared through the value converter; EF cannot translate a member of a converted type.</summary>
    private static readonly FollowUpState ScheduledState = new(FollowUpState.Scheduled);

    public async Task<IReadOnlyList<ScheduledFollowUp>> ListScheduledBetweenAsync(DateTimeOffset after,
        DateTimeOffset until, int batchSize, CancellationToken cancellationToken = default)
    {
        return await context.Set<ScheduledFollowUp>()
            .Where(f => f.State == ScheduledState && f.ScheduledFor > after && f.ScheduledFor <= until)
            .OrderBy(f => f.ScheduledFor)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FollowUpReminderLog>> ListLogsAsync(IReadOnlyCollection<int> followUpIds,
        CancellationToken cancellationToken = default)
    {
        if (followUpIds.Count == 0) return [];
        var ids = followUpIds.ToList();
        return await context.Set<FollowUpReminderLog>()
            .Where(l => ids.Contains(l.FollowUpId))
            .ToListAsync(cancellationToken);
    }

    public async Task AddLogAsync(FollowUpReminderLog log, CancellationToken cancellationToken = default)
    {
        await context.Set<FollowUpReminderLog>().AddAsync(log, cancellationToken);
    }
}
