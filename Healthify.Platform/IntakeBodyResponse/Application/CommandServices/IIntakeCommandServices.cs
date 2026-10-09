using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.IntakeBodyResponse.Application.CommandServices;

/// <summary>Subflow 4.1. Driven by a policy; there is no endpoint that writes the cache.</summary>
public interface IActiveTargetsCacheCommandService
{
    Task<Result<ActiveTargetsCache, IntakeError>> Handle(RefreshActiveTargetsCacheCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Subflows 4.2, 4.3, 4.4 and 4.6. Every one of them is written by the patient and by nobody else.
/// </summary>
public interface IDiaryEntryCommandService
{
    /// <summary>Subflow 4.2 - Log Meal By Photo.</summary>
    Task<Result<DiaryEntry, IntakeError>> Handle(LogMealByPhotoCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 4.2 - Estimate Portion. Invoked by the photo estimation policy only.</summary>
    Task<Result<DiaryEntry, IntakeError>> Handle(EstimatePortionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 4.2 - Confirm Estimate.</summary>
    Task<Result<DiaryEntry, IntakeError>> Handle(ConfirmEstimateCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 4.2 - Adjust Estimate.</summary>
    Task<Result<DiaryEntry, IntakeError>> Handle(AdjustEstimateCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 4.3 - Log Meal Manually.</summary>
    Task<Result<DiaryEntry, IntakeError>> Handle(LogMealManuallyCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>IN-6 - Log a meal in a group: one entry per food, one transaction, the day evaluated once.</summary>
    Task<Result<MealGroupLogOutcome, IntakeError>> Handle(LogMealGroupManuallyCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 4.4 - Log Off Plan Meal. One tap, no detail requested.</summary>
    Task<Result<DiaryEntry, IntakeError>> Handle(LogOffPlanMealCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 4.6 - Sync Pending Entries.</summary>
    Task<Result<SyncOutcome, IntakeError>> Handle(SyncPendingEntriesCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Subflow 4.5 - Record Self Weigh In, and IN-4 - Sync Pending Self Weigh Ins.</summary>
public interface ISelfWeighInCommandService
{
    Task<Result<SelfWeighIn, IntakeError>> Handle(RecordSelfWeighInCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>IN-4. Idempotent by client identifier; one trend recalculation per batch.</summary>
    Task<Result<SelfWeighInSyncOutcome, IntakeError>> Handle(SyncSelfWeighInsCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Subflow 4.5 - Recalculate Weight Trend. Driven by a policy; there is no endpoint.</summary>
public interface IWeightTrendCommandService
{
    Task<Result<WeightTrend, IntakeError>> Handle(RecalculateWeightTrendCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>IA-3 - Meal ideas within what is left today, and their purge.</summary>
public interface IMealIdeasCommandService
{
    /// <summary>Two or three ideas, an <c>IntakeError</c> (nothing left, no targets…) or an <c>AiError</c>.</summary>
    Task<Result<MealIdeasView, IntakeAiFailure>> Handle(GenerateMealIdeasCommand command,
        CancellationToken cancellationToken = default);

    /// <returns>The cached entries removed.</returns>
    Task<Result<int, IntakeError>> Handle(PurgeMealIdeasCommand command, CancellationToken cancellationToken = default);
}

/// <summary>IN-7 - Meal photo recognition: the analysis of a photo, and the purges of the temporary analyses.</summary>
public interface IMealPhotoAnalysisCommandService
{
    /// <summary>The analysis, an <c>IntakeError</c> (photo invalid, not recognized…) or an <c>AiError</c>.</summary>
    Task<Result<MealPhotoAnalysisView, IntakeAiFailure>> Handle(AnalyzeMealPhotoCommand command,
        CancellationToken cancellationToken = default);

    /// <returns>The analyses deleted.</returns>
    Task<Result<int, IntakeError>> Handle(PurgeMealPhotoAnalysesCommand command,
        CancellationToken cancellationToken = default);

    /// <returns>The analyses deleted.</returns>
    Task<Result<int, IntakeError>> Handle(PurgeExpiredMealPhotoAnalysesCommand command,
        CancellationToken cancellationToken = default);
}
