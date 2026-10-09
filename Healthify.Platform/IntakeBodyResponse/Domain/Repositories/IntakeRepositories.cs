using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Repositories;

/// <summary>One cache per patient, keyed by the patient.</summary>
public interface IActiveTargetsCacheRepository : IBaseRepository<ActiveTargetsCache>
{
    Task<ActiveTargetsCache?> FindByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     The diary.
/// </summary>
/// <remarks>
///     Business rule: Entry Never Deleted (Subflow 4.2). Nothing in this context calls the inherited
///     <c>Remove</c>, no command service references it and no endpoint reaches it. The rule is kept
///     by there being no code path to deletion, and it is verified by grep at the close of the phase.
/// </remarks>
public interface IDiaryEntryRepository : IBaseRepository<DiaryEntry>
{
    /// <summary>Read model Daily Diary. Filtered on the declared local day, not the server day.</summary>
    Task<IEnumerable<DiaryEntry>> ListByPatientIdAsync(int patientId, DateOnly? date,
        CancellationToken cancellationToken = default);

    /// <summary>Rule: Idempotency By Aggregate Id (Subflow 4.6).</summary>
    Task<DiaryEntry?> FindByClientEntryIdAsync(Guid clientEntryId,
        CancellationToken cancellationToken = default);

    /// <summary>Read model Pending Sync Queue: entries not yet reconciled.</summary>
    Task<IEnumerable<DiaryEntry>> ListUnreconciledByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     IN-7. The foods most confirmed across the whole platform, among those confirmed by at least
    ///     <paramref name="minimumPatients" /> different patients, most logged first. A statistic of the catalog's use:
    ///     it names no patient and is never read for one.
    /// </summary>
    Task<IReadOnlyList<int>> ListMostLoggedReferenceFoodIdsAsync(int minimumPatients, int max,
        CancellationToken cancellationToken = default);

    /// <summary>IN-7. The entry a photo analysis was logged as, if any (one analysis, one entry).</summary>
    Task<DiaryEntry?> FindByMealPhotoAnalysisIdAsync(Guid analysisId, CancellationToken cancellationToken = default);
}

/// <summary>IN-7. The temporary photo analyses (24 hours by default). They hold no image.</summary>
public interface IMealPhotoAnalysisRepository : IBaseRepository<MealPhotoAnalysis>
{
    Task<MealPhotoAnalysis?> FindByIdAsync(Guid analysisId, CancellationToken cancellationToken = default);

    /// <summary>§12-#14. Deletes every analysis of the patient.</summary>
    /// <returns>Rows deleted.</returns>
    Task<int> DeleteByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the analyses whose lifetime has passed.</summary>
    /// <returns>Rows deleted.</returns>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>Every reading, including the ones that do not smooth the trend.</summary>
public interface ISelfWeighInRepository : IBaseRepository<SelfWeighIn>
{
    Task<IEnumerable<SelfWeighIn>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    /// <summary>IN-4. Rule: Idempotency By Aggregate Id (Subflow 4.6), unique per patient.</summary>
    Task<SelfWeighIn?> FindByClientEntryIdAsync(int patientId, Guid clientEntryId,
        CancellationToken cancellationToken = default);

    /// <summary>IN-3. Every patient with at least one reading, ascending.</summary>
    Task<IReadOnlyList<int>> ListPatientIdsAsync(CancellationToken cancellationToken = default);
}

/// <summary>One trend per patient, keyed by the patient.</summary>
public interface IWeightTrendRepository : IBaseRepository<WeightTrend>
{
    Task<WeightTrend?> FindByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);
}
