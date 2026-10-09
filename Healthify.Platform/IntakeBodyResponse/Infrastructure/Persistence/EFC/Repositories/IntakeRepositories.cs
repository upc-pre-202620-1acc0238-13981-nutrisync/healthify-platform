using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Repositories;

public class ActiveTargetsCacheRepository(AppDbContext context)
    : BaseRepository<ActiveTargetsCache>(context), IActiveTargetsCacheRepository
{
    /// <summary>The patient is the key, so finding by identifier and finding by patient are the same.</summary>
    public async Task<ActiveTargetsCache?> FindByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        if (patientId <= 0) return null;
        return await Context.Set<ActiveTargetsCache>()
            .FirstOrDefaultAsync(c => c.PatientId == patientId, cancellationToken);
    }
}

/// <summary>
///     The diary.
/// </summary>
/// <remarks>
///     Business rule: Entry Never Deleted (Subflow 4.2). Nothing here overrides or calls the
///     inherited Remove.
/// </remarks>
public class DiaryEntryRepository(AppDbContext context)
    : BaseRepository<DiaryEntry>(context), IDiaryEntryRepository
{
    /// <summary>Compared through the value converter; EF cannot translate a member of a converted type.</summary>
    private static readonly SyncState PendingState = new(SyncState.Pending);

    private static readonly SyncState ConflictedState = new(SyncState.Conflicted);

    public new async Task<DiaryEntry?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var diaryEntryId = new DiaryEntryId(id);
        return await Context.Set<DiaryEntry>()
            .FirstOrDefaultAsync(e => e.Id == diaryEntryId, cancellationToken);
    }

    public async Task<IEnumerable<DiaryEntry>> ListByPatientIdAsync(int patientId, DateOnly? date,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Set<DiaryEntry>().Where(e => e.PatientId == patientId);

        if (date is not null)
        {
            // Filtered on the wall clock the patient was reading, so a meal logged at nine in the
            // evening in Lima belongs to that evening and not to the following day in UTC.
            var from = date.Value.ToDateTime(TimeOnly.MinValue);
            var to = from.AddDays(1);
            query = query.Where(e => e.LocalTimestamp >= from && e.LocalTimestamp < to);
        }

        return await query
            .OrderByDescending(e => e.LocalTimestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<DiaryEntry?> FindByClientEntryIdAsync(Guid clientEntryId,
        CancellationToken cancellationToken = default)
    {
        if (clientEntryId == Guid.Empty) return null;
        return await Context.Set<DiaryEntry>()
            .FirstOrDefaultAsync(e => e.ClientEntryId == clientEntryId, cancellationToken);
    }

    public async Task<IEnumerable<DiaryEntry>> ListUnreconciledByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<DiaryEntry>()
            .Where(e => e.PatientId == patientId
                        && (e.SyncState == PendingState || e.SyncState == ConflictedState))
            .OrderBy(e => e.LocalTimestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> ListMostLoggedReferenceFoodIdsAsync(int minimumPatients, int max,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<DiaryEntry>()
            .Where(e => e.ConfirmedReferenceFoodId != null)
            .GroupBy(e => e.ConfirmedReferenceFoodId!.Value)
            .Select(g => new { FoodId = g.Key, Patients = g.Select(e => e.PatientId).Distinct().Count(), Logs = g.Count() })
            .Where(g => g.Patients >= minimumPatients)
            .OrderByDescending(g => g.Logs)
            .ThenBy(g => g.FoodId)
            .Take(Math.Clamp(max, 1, 1000))
            .Select(g => g.FoodId)
            .ToListAsync(cancellationToken);
    }

    public async Task<DiaryEntry?> FindByMealPhotoAnalysisIdAsync(Guid analysisId,
        CancellationToken cancellationToken = default)
    {
        if (analysisId == Guid.Empty) return null;
        return await Context.Set<DiaryEntry>()
            .FirstOrDefaultAsync(e => e.MealPhotoAnalysisId == analysisId, cancellationToken);
    }

    Task<DiaryEntry?> IBaseRepository<DiaryEntry>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

/// <summary>IN-7. The temporary photo analyses. Deleting them is the point: they expire, and they go with the consent.</summary>
public class MealPhotoAnalysisRepository(AppDbContext context)
    : BaseRepository<MealPhotoAnalysis>(context), IMealPhotoAnalysisRepository
{
    public async Task<MealPhotoAnalysis?> FindByIdAsync(Guid analysisId,
        CancellationToken cancellationToken = default)
    {
        if (analysisId == Guid.Empty) return null;
        return await Context.Set<MealPhotoAnalysis>()
            .FirstOrDefaultAsync(a => a.Id == analysisId, cancellationToken);
    }

    public async Task<int> DeleteByPatientIdAsync(int patientId, CancellationToken cancellationToken = default)
    {
        return await Context.Set<MealPhotoAnalysis>()
            .Where(a => a.PatientId == patientId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return await Context.Set<MealPhotoAnalysis>()
            .Where(a => a.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>The identity is a Guid: there is no analysis with an integer identifier.</summary>
    Task<MealPhotoAnalysis?> IBaseRepository<MealPhotoAnalysis>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<MealPhotoAnalysis?>(null);
    }
}

public class SelfWeighInRepository(AppDbContext context)
    : BaseRepository<SelfWeighIn>(context), ISelfWeighInRepository
{
    public new async Task<SelfWeighIn?> FindByIdAsync(int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var selfWeighInId = new SelfWeighInId(id);
        return await Context.Set<SelfWeighIn>()
            .FirstOrDefaultAsync(w => w.Id == selfWeighInId, cancellationToken);
    }

    /// <summary>
    ///     Every reading, including the ones that do not smooth the trend: the aggregate is the one
    ///     that decides what counts, so it has to be given everything.
    /// </summary>
    public async Task<IEnumerable<SelfWeighIn>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<SelfWeighIn>()
            .Where(w => w.PatientId == patientId)
            .OrderBy(w => w.LocalTimestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<SelfWeighIn?> FindByClientEntryIdAsync(int patientId, Guid clientEntryId,
        CancellationToken cancellationToken = default)
    {
        if (clientEntryId == Guid.Empty) return null;
        return await Context.Set<SelfWeighIn>()
            .FirstOrDefaultAsync(w => w.PatientId == patientId && w.ClientEntryId == clientEntryId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<int>> ListPatientIdsAsync(CancellationToken cancellationToken = default)
    {
        return await Context.Set<SelfWeighIn>()
            .Select(w => w.PatientId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
    }

    Task<SelfWeighIn?> IBaseRepository<SelfWeighIn>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

public class WeightTrendRepository(AppDbContext context)
    : BaseRepository<WeightTrend>(context), IWeightTrendRepository
{
    /// <summary>The patient is the key, so finding by identifier and finding by patient are the same.</summary>
    public async Task<WeightTrend?> FindByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        if (patientId <= 0) return null;
        return await Context.Set<WeightTrend>()
            .FirstOrDefaultAsync(t => t.PatientId == patientId, cancellationToken);
    }
}
