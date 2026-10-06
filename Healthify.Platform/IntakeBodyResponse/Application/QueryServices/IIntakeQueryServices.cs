using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;

namespace Healthify.Platform.IntakeBodyResponse.Application.QueryServices;

/// <summary>Read model My Daily Targets.</summary>
public interface IActiveTargetsCacheQueryService
{
    Task<ActiveTargetsCache?> Handle(GetActiveTargetsByPatientIdQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Read models Daily Diary, Estimate Preview Card and Pending Sync Queue.</summary>
public interface IDiaryEntryQueryService
{
    Task<DiaryEntry?> Handle(GetDiaryEntryByIdQuery query, CancellationToken cancellationToken = default);

    Task<IEnumerable<DiaryEntry>> Handle(GetDiaryEntriesByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<DiaryEntry>> Handle(GetPendingSyncQueueByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     IN-1. Local names by catalog identifier, each distinct food looked up once. Identifiers the
    ///     catalog cannot resolve are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<int, string>> Handle(GetFoodNamesQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Every reading, including the ones excluded from the trend.</summary>
public interface ISelfWeighInQueryService
{
    /// <summary>IN-3. The patients with at least one reading.</summary>
    Task<IReadOnlyList<int>> Handle(GetPatientIdsWithSelfWeighInsQuery query,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<SelfWeighIn>> Handle(GetSelfWeighInsByPatientIdQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Read model Weight Trend Chart.</summary>
public interface IWeightTrendQueryService
{
    /// <summary>IN-5. The trend and the summary of its last weeks, or null when there is no trend.</summary>
    Task<WeightTrendView?> Handle(GetWeightTrendRangeByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>IA-2. The trend over an explicit range of local days, or null when there is no trend.</summary>
    Task<WeightTrendView?> Handle(GetWeightTrendBetweenByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    Task<WeightTrend?> Handle(GetWeightTrendByPatientIdQuery query,
        CancellationToken cancellationToken = default);
}
