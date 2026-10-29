using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;

/// <summary>
///     Subflow 4.1 - Refresh Active Targets Cache. Issued by the policy that reacts to Active Targets
///     Updated, never by a user and never by an endpoint.
/// </summary>
/// <remarks>
///     Every field here comes from the published contract. There is no diagnosis, no rationale and no
///     calculation basis among them, and there is nowhere to put one.
///     <c>GuidelineItems</c> tells catalog codes from custom texts; <c>LegacyRestrictions</c> are the
///     free text restrictions from before the closed list, which the patient keeps seeing.
/// </remarks>
public record RefreshActiveTargetsCacheCommand(
    int PatientId,
    int PlanVersion,
    DateTimeOffset ValidFrom,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<CachedGuideline>? GuidelineItems = null,
    IReadOnlyList<string>? LegacyRestrictions = null,
    IReadOnlyList<CachedPlanChange>? ChangesFromPrevious = null,
    string? PatientMessage = null);

/// <summary>
///     Subflow 4.2 - Log Meal By Photo.
/// </summary>
/// <remarks>
///     With <c>AnalysisId</c>, the proposal (food, portion, confidence) is the one the AI made on the server
///     from the photo (<see cref="AnalyzeMealPhotoCommand" />) and the body's are ignored. Without it, the legacy
///     flow: the reference food, the portion and the confidence arrive already computed on the device.
///     <c>ClientEntryId</c> makes the log idempotent: the same identifier of the same patient returns the entry
///     already stored.
/// </remarks>
/// <param name="PatientId">The patient logging the meal.</param>
/// <param name="LocalTimestamp">When it happened, as the device read it.</param>
/// <param name="PhotoRef">Where the photo lives. The server never stores the image.</param>
/// <param name="ReferenceFoodId">The food the on-device estimator matched.</param>
/// <param name="PortionGrams">The portion the on-device estimator proposed.</param>
/// <param name="Confidence">How sure the on-device estimator was.</param>
/// <param name="Confirmation">
///     What the patient already decided on the confirmation screen, or null to leave the
///     entry waiting to be confirmed, which is the behaviour of older clients.
/// </param>
/// <param name="PlanAdherence">Required when <paramref name="Confirmation" /> is present.</param>
/// <param name="AnalysisId">The photo analysis the proposal comes from, or null for the legacy flow.</param>
/// <param name="ClientEntryId">The device's identifier of this log, for idempotency, or null.</param>
public record LogMealByPhotoCommand(
    int PatientId,
    DateTimeOffset LocalTimestamp,
    string? PhotoRef,
    int ReferenceFoodId,
    decimal PortionGrams,
    decimal Confidence,
    PhotoConfirmation? Confirmation = null,
    string? PlanAdherence = null,
    Guid? AnalysisId = null,
    Guid? ClientEntryId = null);

/// <summary>
///     The decision the patient made on the device before the photo entry reached the server.
/// </summary>
/// <param name="Kind">AsProposed or Adjusted.</param>
/// <param name="ReferenceFoodId">The food the patient chose instead. Required when Adjusted.</param>
/// <param name="PortionGrams">The portion the patient chose instead. Required when Adjusted.</param>
public record PhotoConfirmation(string Kind, int? ReferenceFoodId, decimal? PortionGrams)
{
    public const string AsProposed = "AsProposed";
    public const string Adjusted = "Adjusted";
}

/// <summary>
///     Subflow 4.2 - Estimate Portion. Issued by the policy that reacts to Meal Logged when the
///     provenance is Photo, never by an endpoint.
/// </summary>
/// <remarks>
///     The proposal comes from the server-side photo analysis or, in the legacy flow, from the device
///     (portion estimation on-device); the server persists the proposal only. What
///     this command does is resolve the food against the Food Catalog and store what the device
///     proposed, which is the whole of the server side of that subflow.
/// </remarks>
public record EstimatePortionCommand(
    int DiaryEntryId,
    int ReferenceFoodId,
    decimal PortionGrams,
    decimal Confidence);

/// <summary>Subflow 4.2 - Confirm Estimate. The patient accepts the proposal as it stands.</summary>
/// <remarks><paramref name="PlanAdherence" /> is required: a confirmation always answers the question.</remarks>
public record ConfirmEstimateCommand(int DiaryEntryId, int PatientId, string? PlanAdherence = null);

/// <summary>Subflow 4.2 - Adjust Estimate. The patient corrects the proposal.</summary>
/// <remarks><paramref name="PlanAdherence" /> is required: a confirmation always answers the question.</remarks>
public record AdjustEstimateCommand(
    int DiaryEntryId,
    int PatientId,
    int ReferenceFoodId,
    decimal PortionGrams,
    string? PlanAdherence = null);

/// <summary>Subflow 4.3 - Log Meal Manually. What the patient typed is a confirmation from the start.</summary>
/// <remarks>
///     <paramref name="PlanAdherence" /> is required: the old off-plan button is now this same log with OffPlan.
///     <paramref name="ClientEntryId" /> makes it idempotent: the same identifier of the same patient returns the
///     entry already stored.
/// </remarks>
public record LogMealManuallyCommand(
    int PatientId,
    DateTimeOffset LocalTimestamp,
    int ReferenceFoodId,
    decimal PortionGrams,
    string? PlanAdherence = null,
    Guid? ClientEntryId = null);

/// <summary>Subflow 4.5 - Record Self Weigh In.</summary>
/// <remarks>
///     The protocol is "did you weigh yourself fasted?" alone. <c>SameTimeOfDay</c> and <c>SameScale</c> stay
///     optional for older clients that still send them; they are stored as declared, or null.
/// </remarks>
public record RecordSelfWeighInCommand(
    int PatientId,
    decimal ValueKg,
    DateTimeOffset LocalTimestamp,
    bool FastedState,
    bool? SameTimeOfDay = null,
    bool? SameScale = null);

/// <summary>
///     Subflow 4.5 - Recalculate Weight Trend. Issued by the policy that reacts to Self Weigh In
///     Recorded, never by an endpoint. Also issued once per synchronisation batch, and once per
///     patient by the one-shot maintenance job (<c>recalculate-weight-trends</c>).
/// </summary>
public record RecalculateWeightTrendCommand(int PatientId);
/// <summary>One entry as the client device queued it while offline.</summary>
/// <remarks>
///     The client identifier is what makes the batch idempotent. The local timestamp is what the
///     server refuses to rewrite.
///     <c>Confirmed</c> false marks a photo entry still waiting for the patient,
///     stored as a proposal; null keeps the behaviour of older clients, which confirm
///     whatever carries a food. <c>PlanAdherence</c> is the answer given with the confirmation; a
///     queue from an older client that never asked it is accepted and stays <c>NotAnswered</c>.
/// </remarks>
public record PendingDiaryEntry(
    Guid ClientEntryId,
    DateTimeOffset LocalTimestamp,
    string Provenance,
    string? PhotoRef,
    int? ReferenceFoodId,
    decimal? PortionGrams,
    decimal? Confidence,
    bool? Confirmed = null,
    string? PlanAdherence = null);

/// <summary>One self weigh-in as the device queued it while offline.</summary>
/// <remarks>
///     The client identifier is what makes the batch idempotent. As with <see cref="RecordSelfWeighInCommand" />,
///     only <c>FastedState</c> is asked.
/// </remarks>
public record PendingSelfWeighIn(
    Guid ClientEntryId,
    decimal ValueKg,
    DateTimeOffset LocalTimestamp,
    bool FastedState,
    bool? SameTimeOfDay = null,
    bool? SameScale = null);

/// <summary>
///     Sync Pending Self Weigh Ins: the readings the device queued offline, pending sync.
/// </summary>
/// <remarks>
///     Business rules: Idempotency By Aggregate Id (Subflow 4.6), applied per patient. A resent reading is
///     resolved in silence. The trend is recalculated once per batch, not once per reading.
/// </remarks>
public record SyncSelfWeighInsCommand(int PatientId, IReadOnlyList<PendingSelfWeighIn> Entries);

/// <summary>
///     Subflow 4.6 - Sync Pending Entries. The batch the device sends when connectivity returns.
/// </summary>
/// <remarks>
///     Business rules: Idempotency By Aggregate Id, Last Write Wins and Declared Local Timestamp
///     Never Rewritten (Subflow 4.6).
/// </remarks>
public record SyncPendingEntriesCommand(int PatientId, IReadOnlyList<PendingDiaryEntry> Entries);

/// <summary>
///     Generate Meal Ideas. Asked by the patient for their own day.
/// </summary>
/// <param name="PatientId">From the token: the patient.</param>
/// <param name="LocalDate">The patient's day, as their device shows it.</param>
/// <param name="ExcludeIdeaIds">"See other ideas": the ideas already seen, so the new ones are different.</param>
public record GenerateMealIdeasCommand(int PatientId, DateOnly? LocalDate, IReadOnlyList<string>? ExcludeIdeaIds = null);

/// <summary>Purge the meal ideas kept for the patient (consent withdrawn or the function turned off).</summary>
public record PurgeMealIdeasCommand(int PatientId);

/// <summary>
///     Log a meal in a group: N <c>Manual</c> entries of one moment sharing a
///     <c>MealGroupId</c>, one per food. "Log this meal" from a meal idea.
/// </summary>
/// <param name="PatientId">From the token: the patient.</param>
/// <param name="LocalTimestamp">When the meal happened, as the device reads it; the same for every entry.</param>
/// <param name="PlanAdherence">InPlan or OffPlan; InPlan when omitted for an idea (ideas respect the plan).</param>
/// <param name="Origin">Where the meal came from, or null.</param>
/// <param name="Items">The foods, already resolved to the catalog, one to ten.</param>
public record LogMealGroupManuallyCommand(
    int PatientId,
    DateTimeOffset LocalTimestamp,
    string? PlanAdherence,
    MealGroupOrigin? Origin,
    IReadOnlyList<MealGroupItem> Items);

/// <summary><c>{ kind: "MealIdea", mealIdeaId }</c>.</summary>
public record MealGroupOrigin(string? Kind, string? MealIdeaId);

/// <summary>One food of the meal and its portion.</summary>
/// <remarks><c>ClientEntryId</c>: the device's identifier of the entry of this item, for idempotency, or null.</remarks>
public record MealGroupItem(int ReferenceFoodId, decimal PortionGrams, Guid? ClientEntryId = null);

/// <summary>
///     Analyze Meal Photo. The patient sends the photo of their dish; the AI
///     recognizes it on the server and the catalog resolves it. Creates no diary entry.
/// </summary>
/// <remarks>The photo is held in memory for this command only; it is never stored nor logged.</remarks>
/// <param name="PatientId">From the token: the patient.</param>
/// <param name="Photo">The bytes of the photo as uploaded.</param>
/// <param name="LocalDate">
///     The patient's day, so the plan suggestion weighs what is left today. Optional: without it (or out of
///     range) only the restrictions are weighed. Never sent to the AI.
/// </param>
public record AnalyzeMealPhotoCommand(int PatientId, byte[]? Photo, DateOnly? LocalDate = null)
{
    public override string ToString()
    {
        return $"AnalyzeMealPhotoCommand {{ PatientId = {PatientId}, Photo = {Photo?.Length ?? 0} bytes, " +
               $"LocalDate = {LocalDate} }}";
    }
}

/// <summary>Purge the photo analyses of a patient (AI consent withdrawn or the function turned off).</summary>
public record PurgeMealPhotoAnalysesCommand(int PatientId);

/// <summary>Purge every photo analysis whose lifetime has passed. Issued by the purge worker.</summary>
public record PurgeExpiredMealPhotoAnalysesCommand;
