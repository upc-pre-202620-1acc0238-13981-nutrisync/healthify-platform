namespace Healthify.Platform.IntakeBodyResponse.Application.Internal;

/// <summary>What happened to one item of a synchronisation batch.</summary>
/// <param name="ClientEntryId">The identifier the device generated while offline.</param>
/// <param name="DiaryEntryId">The entry it became, when it became one.</param>
/// <param name="Outcome">Created, AlreadyPresent, ConflictResolved or Rejected.</param>
/// <param name="Reason">Why it was rejected, when it was.</param>
public record SyncedEntryOutcome(Guid ClientEntryId, int? DiaryEntryId, string Outcome, string? Reason)
{
    public const string Created = "Created";
    public const string AlreadyPresent = "AlreadyPresent";
    public const string ConflictResolved = "ConflictResolved";
    public const string Rejected = "Rejected";
}

/// <summary>
///     What one run of Sync Pending Entries did.
/// </summary>
/// <remarks>
///     Every item is reported individually and the batch never fails as a whole. An entry the server
///     cannot accept must not take the rest of the patient's week down with it.
/// </remarks>
public record SyncOutcome(
    int PatientId,
    int Created,
    int AlreadyPresent,
    int ConflictsResolved,
    int Rejected,
    IReadOnlyList<SyncedEntryOutcome> Entries);

/// <summary>IN-4. What happened to one queued self weigh-in.</summary>
/// <param name="ClientEntryId">The identifier the device generated while offline.</param>
/// <param name="SelfWeighInId">The reading it became or already was, when there is one.</param>
/// <param name="Outcome">Created, AlreadyPresent or Rejected (the values of <see cref="SyncedEntryOutcome" />).</param>
/// <param name="Reason">Why it was rejected, when it was.</param>
public record SyncedSelfWeighInOutcome(Guid ClientEntryId, int? SelfWeighInId, string Outcome, string? Reason);

/// <summary>
///     IN-4. What one run of Sync Pending Self Weigh Ins did.
/// </summary>
/// <remarks>
///     A duplicate, resent or repeated inside the batch, is resolved in silence as AlreadyPresent: the device
///     drops it from its queue and nothing is stored twice. There is no ConflictResolved: a reading is never
///     edited, so the first copy stands.
/// </remarks>
public record SelfWeighInSyncOutcome(
    int PatientId,
    int Created,
    int AlreadyPresent,
    int Rejected,
    IReadOnlyList<SyncedSelfWeighInOutcome> Entries);
