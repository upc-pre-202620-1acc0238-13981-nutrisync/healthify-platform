using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Persistence.EFC.Repositories;

/// <summary>
///     The windows.
/// </summary>
/// <remarks>
///     Business rule: Evaluated Data Is Preserved (Subflow 5.11). Nothing here overrides or calls the
///     inherited Remove.
/// </remarks>
public class EvaluationWindowRepository(AppDbContext context)
    : BaseRepository<EvaluationWindow>(context), IEvaluationWindowRepository
{
    /// <summary>Compared through the value converter; EF cannot translate a member of a converted type.</summary>
    private static readonly WindowState OpenState = new(WindowState.Open);

    public new async Task<EvaluationWindow?> FindByIdAsync(int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var windowId = new WindowId(id);
        return await Context.Set<EvaluationWindow>()
            .FirstOrDefaultAsync(w => w.Id == windowId, cancellationToken);
    }

    public async Task<EvaluationWindow?> FindOpenByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        if (patientId <= 0) return null;
        return await Context.Set<EvaluationWindow>()
            .FirstOrDefaultAsync(w => w.PatientId == patientId && w.State == OpenState,
                cancellationToken);
    }

    public async Task<IEnumerable<EvaluationWindow>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<EvaluationWindow>()
            .Where(w => w.PatientId == patientId)
            .OrderByDescending(w => w.FromDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<EvaluationWindow>> ListOpenAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<EvaluationWindow>()
            .Where(w => w.State == OpenState)
            .OrderBy(w => w.FromDate)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    Task<EvaluationWindow?> IBaseRepository<EvaluationWindow>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

public class DeviationRepository(AppDbContext context)
    : BaseRepository<Deviation>(context), IDeviationRepository
{
    public new async Task<Deviation?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var deviationId = new DeviationId(id);
        return await Context.Set<Deviation>()
            .FirstOrDefaultAsync(d => d.Id == deviationId, cancellationToken);
    }

    public async Task<IEnumerable<Deviation>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<Deviation>()
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.DetectedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Deviation?> FindLatestByWindowAndDirectionAsync(WindowId windowId,
        DeviationDirection direction, CancellationToken cancellationToken = default)
    {
        return await Context.Set<Deviation>()
            .Where(d => d.WindowRef == windowId && d.Direction == direction)
            .OrderByDescending(d => d.DetectedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    Task<Deviation?> IBaseRepository<Deviation>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

public class ConsistencyIndexRepository(AppDbContext context)
    : BaseRepository<ConsistencyIndex>(context), IConsistencyIndexRepository
{
    /// <summary>Compared through the value converter; EF cannot translate a member of a converted type.</summary>
    private static readonly ConsistencyState AlertState = new(ConsistencyState.Alert);

    /// <summary>The patient is the key, so finding by identifier and finding by patient are the same.</summary>
    public async Task<ConsistencyIndex?> FindByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        if (patientId <= 0) return null;
        return await Context.Set<ConsistencyIndex>()
            .FirstOrDefaultAsync(i => i.PatientId == patientId, cancellationToken);
    }

    /// <summary>
    ///     Business rules: Patient Prompt Required Before Escalation and Three Weeks In Alert
    ///     Required (Subflow 5.8), expressed as a query so the time-driven policy never sees a row it
    ///     is not allowed to act on.
    /// </summary>
    public async Task<IEnumerable<ConsistencyIndex>> ListEscalatableAsync(
        DateTimeOffset alertSinceBefore, int batchSize, CancellationToken cancellationToken = default)
    {
        return await Context.Set<ConsistencyIndex>()
            .Where(i => i.State == AlertState
                        && i.ShownToPatientAt != null
                        && i.EscalatedAt == null
                        && i.AlertSinceAt != null
                        && i.AlertSinceAt <= alertSinceBefore)
            .OrderBy(i => i.AlertSinceAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }
}

public class ReferralRepository(AppDbContext context)
    : BaseRepository<Referral>(context), IReferralRepository
{
    public new async Task<Referral?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var referralId = new ReferralId(id);
        return await Context.Set<Referral>()
            .FirstOrDefaultAsync(r => r.Id == referralId, cancellationToken);
    }

    public async Task<IEnumerable<Referral>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<Referral>()
            .Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.IssuedAt)
            .ToListAsync(cancellationToken);
    }

    Task<Referral?> IBaseRepository<Referral>.FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

public class ScheduledFollowUpRepository(AppDbContext context)
    : BaseRepository<ScheduledFollowUp>(context), IScheduledFollowUpRepository
{
    /// <summary>Compared through the value converter; EF cannot translate a member of a converted type.</summary>
    private static readonly FollowUpState ScheduledState = new(FollowUpState.Scheduled);
    private static readonly FollowUpState MissedState = new(FollowUpState.Missed);

    public new async Task<ScheduledFollowUp?> FindByIdAsync(int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var followUpId = new FollowUpId(id);
        return await Context.Set<ScheduledFollowUp>()
            .FirstOrDefaultAsync(f => f.Id == followUpId, cancellationToken);
    }

    public async Task<ScheduledFollowUp?> FindScheduledByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        if (patientId <= 0) return null;
        return await Context.Set<ScheduledFollowUp>()
            .FirstOrDefaultAsync(f => f.PatientId == patientId && f.State == ScheduledState,
                cancellationToken);
    }

    public async Task<IEnumerable<ScheduledFollowUp>> ListByPractitionerIdAsync(int practitionerId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<ScheduledFollowUp>()
            .Where(f => f.PractitionerId == practitionerId)
            .OrderBy(f => f.ScheduledFor)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ScheduledFollowUp>> ListByPractitionerIdAsync(int practitionerId,
        FollowUpState? state, DateTimeOffset? from, CancellationToken cancellationToken = default)
    {
        var query = Context.Set<ScheduledFollowUp>().Where(f => f.PractitionerId == practitionerId);
        if (state is not null) query = query.Where(f => f.State == state);
        if (from is not null) query = query.Where(f => f.ScheduledFor >= from.Value);

        return await query.OrderBy(f => f.ScheduledFor).ToListAsync(cancellationToken);
    }

    public async Task<ScheduledFollowUp?> FindOpenForDayAsync(int patientId, int practitionerId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        return await Context.Set<ScheduledFollowUp>()
            .Where(f => f.PatientId == patientId && f.PractitionerId == practitionerId &&
                        (f.State == ScheduledState || f.State == MissedState) &&
                        f.ScheduledFor >= from && f.ScheduledFor < to)
            .OrderBy(f => f.ScheduledFor)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<ScheduledFollowUp>> ListScheduledByPatientIdsAsync(
        IReadOnlyCollection<int> patientIds, CancellationToken cancellationToken = default)
    {
        var ids = patientIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return [];

        return await Context.Set<ScheduledFollowUp>()
            .Where(f => ids.Contains(f.PatientId) && f.State == ScheduledState)
            .OrderBy(f => f.ScheduledFor)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ScheduledFollowUp>> ListOpenByPatientAndPractitionerAsync(int patientId,
        int practitionerId, CancellationToken cancellationToken = default)
    {
        if (patientId <= 0 || practitionerId <= 0) return [];
        return await Context.Set<ScheduledFollowUp>()
            .Where(f => f.PatientId == patientId && f.PractitionerId == practitionerId &&
                        (f.State == ScheduledState || f.State == MissedState))
            .OrderByDescending(f => f.ScheduledFor)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ScheduledFollowUp>> ListByPatientIdAsync(int patientId, FollowUpState? state,
        CancellationToken cancellationToken = default)
    {
        if (patientId <= 0) return [];
        var query = Context.Set<ScheduledFollowUp>().Where(f => f.PatientId == patientId);
        if (state is not null) query = query.Where(f => f.State == state);

        return await query.OrderByDescending(f => f.ScheduledFor).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ScheduledFollowUp>> ListOverdueAsync(DateTimeOffset asOf,
        int batchSize, CancellationToken cancellationToken = default)
    {
        return await Context.Set<ScheduledFollowUp>()
            .Where(f => f.State == ScheduledState && f.ScheduledFor <= asOf)
            .OrderBy(f => f.ScheduledFor)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    Task<ScheduledFollowUp?> IBaseRepository<ScheduledFollowUp>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

/// <summary>MA-4. The check ins. Nothing here removes one.</summary>
public class PreVisitCheckInRepository(AppDbContext context)
    : BaseRepository<PreVisitCheckIn>(context), IPreVisitCheckInRepository
{
    public new async Task<PreVisitCheckIn?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var checkInId = new PreVisitCheckInId(id);
        return await Context.Set<PreVisitCheckIn>()
            .FirstOrDefaultAsync(c => c.Id == checkInId, cancellationToken);
    }

    public async Task<PreVisitCheckIn?> FindByFollowUpIdAsync(int followUpId,
        CancellationToken cancellationToken = default)
    {
        if (followUpId <= 0) return null;
        return await Context.Set<PreVisitCheckIn>()
            .FirstOrDefaultAsync(c => c.FollowUpId == followUpId, cancellationToken);
    }

    public async Task<IEnumerable<PreVisitCheckIn>> ListByFollowUpIdsAsync(IReadOnlyCollection<int> followUpIds,
        CancellationToken cancellationToken = default)
    {
        var ids = followUpIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return [];

        return await Context.Set<PreVisitCheckIn>()
            .Where(c => ids.Contains(c.FollowUpId))
            .ToListAsync(cancellationToken);
    }

    Task<PreVisitCheckIn?> IBaseRepository<PreVisitCheckIn>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

/// <summary>IA-2. The weekly summaries. Removing one is allowed: it is generated content, not evaluated data.</summary>
public class WeeklySummaryRepository(AppDbContext context)
    : BaseRepository<WeeklySummary>(context), IWeeklySummaryRepository
{
    public new async Task<WeeklySummary?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var summaryId = new WeeklySummaryId(id);
        return await Context.Set<WeeklySummary>()
            .FirstOrDefaultAsync(s => s.Id == summaryId, cancellationToken);
    }

    public async Task<WeeklySummary?> FindLatestByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<WeeklySummary>()
            .Where(s => s.PatientId == patientId)
            .OrderByDescending(s => s.WeekStart)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<WeeklySummary?> FindByPatientIdAndWeekStartAsync(int patientId, DateOnly weekStart,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<WeeklySummary>()
            .FirstOrDefaultAsync(s => s.PatientId == patientId && s.WeekStart == weekStart, cancellationToken);
    }

    public async Task<IEnumerable<WeeklySummary>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<WeeklySummary>()
            .Where(s => s.PatientId == patientId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<WeeklySummary>> ListGeneratedBeforeAsync(DateTimeOffset cut, int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<WeeklySummary>()
            .Where(s => s.GeneratedAt < cut)
            .OrderBy(s => s.GeneratedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    Task<WeeklySummary?> IBaseRepository<WeeklySummary>.FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}
