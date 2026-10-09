using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;

/// <summary>Read model My Daily Targets. Available offline on the device.</summary>
public record GetActiveTargetsByPatientIdQuery(int PatientId);

/// <summary>Read model Daily Diary. A null date means the whole diary, most recent first.</summary>
public record GetDiaryEntriesByPatientIdQuery(int PatientId, DateOnly? Date);

/// <summary>Read model Daily Diary, one entry.</summary>
public record GetDiaryEntryByIdQuery(int DiaryEntryId);

/// <summary>Read model Weight Trend Chart. Never a single day as the headline.</summary>
public record GetWeightTrendByPatientIdQuery(int PatientId);

/// <summary>
///     IN-5. Read model Weight Trend Chart with the summary of its last <paramref name="Weeks" /> weeks
///     (<c>?weeks=4</c> by default) and the count of excluded readings in them.
/// </summary>
public record GetWeightTrendRangeByPatientIdQuery(int PatientId, int Weeks = WeightTrendRange.DefaultWeeks);

/// <summary>
///     IA-2. The trend over an explicit range of local days (<paramref name="From" /> to <paramref name="To" />, both
///     included), independent of the day it is asked.
/// </summary>
public record GetWeightTrendBetweenByPatientIdQuery(int PatientId, DateOnly From, DateOnly To);

/// <summary>
///     Read model Pending Sync Queue.
/// </summary>
/// <remarks>
///     A reconciliation view for the device. An empty queue is the healthy answer: entries only stay
///     here when a synchronisation batch was interrupted after accepting some of its items.
/// </remarks>
public record GetPendingSyncQueueByPatientIdQuery(int PatientId);

/// <summary>
///     IN-1. The local names of the given catalog foods, so the diary reads «Ceviche · 280 g» rather
///     than an identifier. A food the catalog cannot resolve keeps its identifier and loses only the
///     label.
/// </summary>
public record GetFoodNamesQuery(IReadOnlyCollection<int> ReferenceFoodIds);

/// <summary>Every reading of one patient. Input to the trend recalculation.</summary>
public record GetSelfWeighInsByPatientIdQuery(int PatientId);

/// <summary>IN-3. The patients who have at least one reading, ascending. Input to the one-shot recalculation.</summary>
public record GetPatientIdsWithSelfWeighInsQuery;
