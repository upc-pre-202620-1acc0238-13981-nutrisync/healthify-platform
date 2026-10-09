using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;

/// <summary>Subflow 4.1. Stays inside Intake and Body Response.</summary>
/// <param name="PatientId">Whose cache was refreshed.</param>
/// <param name="PlanVersion">Version of the published contract now cached.</param>
public record ActiveTargetsCacheRefreshed(int PatientId, int PlanVersion) : DomainEventBase;

/// <summary>
///     Subflows 4.2, 4.3 and 4.4. Integration event 7 of 13: it crosses to Monitoring and Adherence
///     Subflow 5.4, where the day it belongs to is evaluated.
/// </summary>
/// <remarks>
///     The proposed estimate travels on the event so that the photo estimation policy of Subflow 4.2
///     has what the device computed without going back to the device. It is null for a manual log,
///     which is confirmed from the start, and for an off-plan log, which has no detail at all.
/// </remarks>
/// <param name="DiaryEntryId">The entry that was created.</param>
/// <param name="PatientId">Who logged it.</param>
/// <param name="LocalTimestamp">The moment the patient declared. Never rewritten.</param>
/// <param name="Provenance">Photo, Manual or OffPlan.</param>
/// <param name="ProposedReferenceFoodId">What the on-device estimator proposed, when it ran.</param>
/// <param name="ProposedPortionGrams">The portion the on-device estimator proposed, when it ran.</param>
/// <param name="ProposedConfidence">How sure the on-device estimator was, when it ran.</param>
/// <param name="PlanAdherence">IN-1. InPlan, OffPlan or NotAnswered while the entry waits to be confirmed.</param>
/// <param name="ConfirmedOnCreation">
///     MA-1. True when the entry was confirmed in the same request that created it (a manual log, or
///     a photo log confirmed on the device). An <see cref="EstimateConfirmedByPatient" /> for the same
///     entry follows in that request, so subscribers that only need the day's final totals, or that
///     would store a proposal, can leave the work to it.
/// </param>
/// <param name="ViaSynchronization">
///     MA-1. True when the entry arrived in an offline synchronisation batch. The batch announces the
///     days it touched once, with <see cref="DiaryBatchSynchronized" />, and that is where the day is
///     evaluated.
/// </param>
public record MealLogged(
    int DiaryEntryId,
    int PatientId,
    DateTimeOffset LocalTimestamp,
    string Provenance,
    int? ProposedReferenceFoodId,
    decimal? ProposedPortionGrams,
    decimal? ProposedConfidence,
    string PlanAdherence = ValueObjects.PlanAdherence.NotAnswered,
    bool ConfirmedOnCreation = false,
    bool ViaSynchronization = false) : DomainEventBase;

/// <summary>
///     Subflow 4.2. Deliberately does not cross a boundary: a proposal is not intake, and letting it
///     reach Monitoring would let a model's guess be evaluated as if the patient had said it.
/// </summary>
/// <param name="DiaryEntryId">The entry the proposal was stored on.</param>
/// <param name="PatientId">Whose entry it is.</param>
/// <param name="ReferenceFoodId">The food the on-device estimator proposed.</param>
/// <param name="PortionGrams">The portion it proposed.</param>
/// <param name="Confidence">How sure it was. Always present.</param>
public record EstimateProposed(
    int DiaryEntryId,
    int PatientId,
    int ReferenceFoodId,
    decimal PortionGrams,
    decimal Confidence) : DomainEventBase;

/// <summary>
///     Subflow 4.2. Integration event 8 of 13: it crosses to Monitoring and Adherence Subflow 5.4.
/// </summary>
/// <param name="DiaryEntryId">The entry the patient confirmed.</param>
/// <param name="PatientId">Who confirmed it.</param>
/// <param name="LocalTimestamp">The moment the patient declared. Never rewritten.</param>
/// <param name="PlanAdherence">IN-1. The answer given with the confirmation: InPlan or OffPlan.</param>
/// <param name="EvaluatesDay">
///     IN-6. False when the entry is one of a meal logged in a group: the group announces its day once, with
///     <see cref="MealGroupLogged" />, so the day is evaluated once rather than once per ingredient.
/// </param>
public record EstimateConfirmedByPatient(
    int DiaryEntryId,
    int PatientId,
    DateTimeOffset LocalTimestamp,
    string PlanAdherence = ValueObjects.PlanAdherence.NotAnswered,
    bool EvaluatesDay = true) : DomainEventBase;

/// <summary>
///     IN-6. A meal logged as several entries of one moment («Registrar esta comida» from an idea of IA-3), each
///     already confirmed. Published once, after the entries were committed and their own events left. It crosses to
///     Monitoring and Adherence Subflow 5.4, where the day the meal declared is evaluated once.
/// </summary>
/// <param name="MealGroupId">The group the entries share.</param>
/// <param name="PatientId">Who logged it.</param>
/// <param name="LocalTimestamp">The moment the patient declared, the same for every entry.</param>
/// <param name="DiaryEntryIds">The entries, in the order of the items.</param>
/// <param name="Origin">MealIdea, or null.</param>
public record MealGroupLogged(
    Guid MealGroupId,
    int PatientId,
    DateTimeOffset LocalTimestamp,
    IReadOnlyList<int> DiaryEntryIds,
    string? Origin) : DomainEventBase;

/// <summary>
///     Subflow 4.2. Stays inside this context: an adjustment is a confirmation with a correction in
///     it, and Subflow 5.4 already reacts to the confirmation.
/// </summary>
/// <param name="DiaryEntryId">The entry the patient corrected.</param>
/// <param name="PatientId">Who corrected it.</param>
/// <param name="ReferenceFoodId">The food the patient chose instead.</param>
/// <param name="PortionGrams">The portion the patient chose instead.</param>
/// <param name="PlanAdherence">IN-1. The answer given with the correction: InPlan or OffPlan.</param>
public record EstimateAdjustedByPatient(
    int DiaryEntryId,
    int PatientId,
    int ReferenceFoodId,
    decimal PortionGrams,
    string PlanAdherence = ValueObjects.PlanAdherence.NotAnswered) : DomainEventBase;

/// <summary>
///     Subflow 4.4. Integration event 9 of 13: it crosses to Monitoring and Adherence Subflow 5.4.
/// </summary>
/// <remarks>
///     It carries no detail beyond the fact and the moment, and it carries no judgement. Monitoring
///     counts the day as logged; nothing computes a deviation from an off-plan entry.
///     Deprecated by IN-1: published only by the deprecated one-tap endpoint and by legacy offline
///     queues. An off-plan meal logged with food now travels as <see cref="MealLogged" /> and
///     <see cref="EstimateConfirmedByPatient" /> with PlanAdherence OffPlan.
/// </remarks>
/// <param name="DiaryEntryId">The entry that was created.</param>
/// <param name="PatientId">Who logged it.</param>
/// <param name="LocalTimestamp">The moment the patient declared. Never rewritten.</param>
/// <param name="ViaSynchronization">MA-1. As on <see cref="MealLogged" />: the batch evaluates the day.</param>
public record OffPlanEntryLogged(
    int DiaryEntryId,
    int PatientId,
    DateTimeOffset LocalTimestamp,
    bool ViaSynchronization = false) : DomainEventBase;

/// <summary>
///     Subflow 4.5. Deliberately does not cross a boundary: what leaves this context about body
///     weight is the trend, never a single reading.
/// </summary>
/// <param name="SelfWeighInId">The reading that was recorded.</param>
/// <param name="PatientId">Who recorded it.</param>
/// <param name="FollowsProtocol">Whether the reading meets the protocol in force (IN-3: fasted).</param>
/// <param name="ViaSynchronization">
///     IN-4. True when the reading arrived in an offline synchronisation batch. The batch recalculates the
///     trend once, with <see cref="SelfWeighInBatchSynchronized" />, instead of once per reading.
/// </param>
public record SelfWeighInRecorded(
    int SelfWeighInId,
    int PatientId,
    bool FollowsProtocol,
    bool ViaSynchronization = false) : DomainEventBase;

/// <summary>
///     IN-4. Published once at the end of a self weigh-in synchronisation batch that created at least one
///     reading. Stays inside this context: its only subscriber recalculates the trend, once.
/// </summary>
/// <remarks>
///     Each reading is still its own committed step. If a batch is interrupted before this event leaves,
///     the device resends, the readings come back as already present, and the trend is recalculated by the
///     next reading or batch.
/// </remarks>
/// <param name="PatientId">Whose readings were synchronised.</param>
/// <param name="SelfWeighInIds">The readings the batch created.</param>
public record SelfWeighInBatchSynchronized(int PatientId, IReadOnlyList<int> SelfWeighInIds) : DomainEventBase;

/// <summary>
///     Subflow 4.5. Integration event 10 of 13: it crosses to Monitoring and Adherence Subflow 5.7,
///     where it is one of the two series the consistency index needs.
/// </summary>
/// <param name="PatientId">Whose trend was recalculated.</param>
/// <param name="PointCount">How many points the smoothed series now holds.</param>
public record WeightTrendRecalculated(int PatientId, int PointCount) : DomainEventBase;

/// <summary>
///     Subflow 4.5. Stays inside this context.
/// </summary>
/// <remarks>
///     Being excluded from the smoothing is the entire consequence of a reading taken outside the
///     protocol. The reading is kept in full, nothing is flagged, and nobody is told they did it
///     wrong. This event exists so the client can explain the gap, not so anything can act on it.
/// </remarks>
/// <param name="SelfWeighInId">The reading that was kept but did not smooth the trend.</param>
/// <param name="PatientId">Whose reading it is.</param>
public record SelfWeighInExcludedFromTrend(int SelfWeighInId, int PatientId) : DomainEventBase;

/// <summary>
///     Subflow 4.6. Stays inside this context. Records that an entry reached the server carrying the
///     state it had on the device, which is where the outbox lives.
/// </summary>
/// <param name="DiaryEntryId">The entry that had been queued.</param>
/// <param name="PatientId">Whose entry it is.</param>
/// <param name="ClientEntryId">The identifier the device generated while offline.</param>
public record EntryQueuedOffline(
    int DiaryEntryId,
    int PatientId,
    Guid ClientEntryId) : DomainEventBase;

/// <summary>
///     Subflow 4.6. Integration event 11 of 13: it crosses to Monitoring and Adherence Subflow 5.5,
///     where a late entry re-evaluates its own day and nothing else.
/// </summary>
/// <param name="DiaryEntryId">The entry that was reconciled.</param>
/// <param name="PatientId">Whose entry it is.</param>
/// <param name="LocalTimestamp">The moment the patient declared, unchanged by the journey.</param>
/// <param name="ReEvaluatesDay">
///     MA-1. False when the entry is part of a batch that announces its days once, with
///     <see cref="DiaryBatchSynchronized" />, so that a day with several queued entries is
///     re-evaluated once rather than once per entry.
/// </param>
public record EntrySynchronized(
    int DiaryEntryId,
    int PatientId,
    DateTimeOffset LocalTimestamp,
    bool ReEvaluatesDay = true) : DomainEventBase;

/// <summary>
///     Subflow 4.6, MA-1. Published once at the end of a synchronisation batch with the distinct days
///     its reconciled entries declared. It crosses to Monitoring and Adherence Subflow 5.5, where each
///     of those days, and only those, is re-evaluated once.
/// </summary>
/// <remarks>
///     Each entry is still its own committed step. If a batch is interrupted before this event leaves,
///     the device resends the entries, they come back as already present, and the next batch announces
///     their days.
/// </remarks>
/// <param name="PatientId">Whose diary was synchronised.</param>
/// <param name="Days">The local calendar days the reconciled entries declared, each once.</param>
public record DiaryBatchSynchronized(int PatientId, IReadOnlyList<DateOnly> Days) : DomainEventBase;

/// <summary>
///     Subflow 4.6. Stays inside this context.
/// </summary>
/// <param name="DiaryEntryId">The entry whose stored copy disagreed with the incoming one.</param>
/// <param name="PatientId">Whose entry it is.</param>
/// <param name="Resolution">How it was resolved. Last write wins on the estimate, never on the moment.</param>
public record SyncConflictResolved(
    int DiaryEntryId,
    int PatientId,
    string Resolution) : DomainEventBase;
