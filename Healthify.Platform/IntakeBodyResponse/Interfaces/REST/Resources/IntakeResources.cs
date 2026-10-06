namespace Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;

// ---------------------------------------------------------------------------------------------
// Request resources
// ---------------------------------------------------------------------------------------------

/// <summary>Payload of Subflow 4.2 - Log Meal By Photo.</summary>
/// <remarks>
///     IN-7: with <c>analysisId</c> (from <c>POST /patients/{id}/meal-photo-analyses</c>), the proposal is the one
///     the AI made on the server and the reference food, portion and confidence below are ignored. Without it, the
///     legacy flow of IN-2: they are what the device already computed.
/// </remarks>
public record LogMealByPhotoResource(
    int PatientId,
    DateTimeOffset LocalTimestamp,
    string? PhotoRef,
    int ReferenceFoodId,
    decimal PortionGrams,
    decimal Confidence,
    PhotoConfirmationResource? Confirmation = null,
    string? PlanAdherence = null,
    Guid? AnalysisId = null,
    Guid? ClientEntryId = null)
{
    /// <summary>The patient logging the meal. Must be the authenticated patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>When the meal happened, as the device reads it. Never rewritten by the server.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;

    /// <summary>Where the photo lives. The server stores the reference, never the image.</summary>
    public string? PhotoRef { get; init; } = PhotoRef;

    /// <summary>The catalog entry the on-device estimator matched. Required.</summary>
    public int ReferenceFoodId { get; init; } = ReferenceFoodId;

    /// <summary>The portion the on-device estimator proposed, in grams. Required.</summary>
    public decimal PortionGrams { get; init; } = PortionGrams;

    /// <summary>How sure the on-device estimator was, from 0 to 1. Always required.</summary>
    public decimal Confidence { get; init; } = Confidence;

    /// <summary>
    ///     IN-2. What the patient decided on the confirmation screen, so the meal is saved already
    ///     confirmed in one request. Omit it to save the entry as «Por confirmar», as before.
    /// </summary>
    public PhotoConfirmationResource? Confirmation { get; init; } = Confirmation;

    /// <summary>
    ///     «¿Esta comida estaba en tu plan?»: InPlan or OffPlan. Required with a confirmation; ignored
    ///     without one, because the answer is given when the entry is confirmed.
    /// </summary>
    public string? PlanAdherence { get; init; } = PlanAdherence;

    /// <summary>
    ///     IN-7. The photo analysis the proposal comes from. It must belong to the patient and not be expired (24
    ///     hours). An analysis is logged once: logging it again returns the same entry.
    /// </summary>
    public Guid? AnalysisId { get; init; } = AnalysisId;

    /// <summary>
    ///     IN-7. The device's identifier of this log. Sending it again returns the entry already stored, without a
    ///     second entry and without evaluating the day again.
    /// </summary>
    public Guid? ClientEntryId { get; init; } = ClientEntryId;
}

/// <summary>IN-2. The patient's decision about the on-device proposal.</summary>
public record PhotoConfirmationResource(string Kind, int? ReferenceFoodId = null, decimal? PortionGrams = null)
{
    /// <summary>AsProposed, or Adjusted with the food and the portion the patient chose instead.</summary>
    public string Kind { get; init; } = Kind;

    /// <summary>The food the patient chose instead. Required when Adjusted.</summary>
    public int? ReferenceFoodId { get; init; } = ReferenceFoodId;

    /// <summary>The portion the patient chose instead, in grams. Required when Adjusted.</summary>
    public decimal? PortionGrams { get; init; } = PortionGrams;
}

/// <summary>Payload of Subflow 4.3 - Log Meal Manually.</summary>
public record LogMealManuallyResource(
    int PatientId,
    DateTimeOffset LocalTimestamp,
    int ReferenceFoodId,
    decimal PortionGrams,
    string? PlanAdherence = null,
    Guid? ClientEntryId = null)
{
    /// <summary>The patient logging the meal. Must be the authenticated patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>When the meal happened, as the device reads it. Never rewritten by the server.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;

    /// <summary>The catalog entry the patient chose. Required.</summary>
    public int ReferenceFoodId { get; init; } = ReferenceFoodId;

    /// <summary>The portion the patient declared, in grams. Required.</summary>
    public decimal PortionGrams { get; init; } = PortionGrams;

    /// <summary>
    ///     «¿Esta comida estaba en tu plan?»: InPlan or OffPlan. Required. OffPlan is marked
    ///     «Fuera del plan» and still counts towards the day.
    /// </summary>
    public string? PlanAdherence { get; init; } = PlanAdherence;

    /// <summary>
    ///     IN-7. The device's identifier of this log. Sending it again returns the entry already stored, without a
    ///     second entry and without evaluating the day again.
    /// </summary>
    public Guid? ClientEntryId { get; init; } = ClientEntryId;
}

/// <summary>
///     Payload of Subflow 4.4 - Log Off Plan Meal.
/// </summary>
/// <remarks>
///     Business rules: No Detail Requested, No Deviation Computed, No Visual Penalty and Streak
///     Rewards Logging Not Deficit (Subflow 4.4). Two fields, and that is the whole design. There is
///     no food, no portion, no reason and no note, because asking for any of them is what makes
///     people stop declaring. The goal is to make saying it cheaper than omitting it.
///     Deprecated by IN-1: use <c>planAdherence: "OffPlan"</c> on manual-logs or photo-logs.
/// </remarks>
public record LogOffPlanMealResource(int PatientId, DateTimeOffset LocalTimestamp)
{
    /// <summary>The patient logging the meal. Must be the authenticated patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>When it happened, as the device reads it. Never rewritten by the server.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;
}

/// <summary>Payload of Subflow 4.2 - Confirm Estimate (IN-1).</summary>
public record EstimateConfirmationResource(string? PlanAdherence = null)
{
    /// <summary>«¿Esta comida estaba en tu plan?»: InPlan or OffPlan. Required.</summary>
    public string? PlanAdherence { get; init; } = PlanAdherence;
}

/// <summary>Payload of Subflow 4.2 - Adjust Estimate.</summary>
public record AdjustEstimateResource(int ReferenceFoodId, decimal PortionGrams, string? PlanAdherence = null)
{
    /// <summary>The catalog entry the patient chose instead. Required.</summary>
    public int ReferenceFoodId { get; init; } = ReferenceFoodId;

    /// <summary>The portion the patient chose instead, in grams. Required.</summary>
    public decimal PortionGrams { get; init; } = PortionGrams;

    /// <summary>«¿Esta comida estaba en tu plan?»: InPlan or OffPlan. Required.</summary>
    public string? PlanAdherence { get; init; } = PlanAdherence;
}

/// <summary>Payload of Subflow 4.5 - Record Self Weigh In.</summary>
/// <remarks>IN-3: only <c>FastedState</c> is asked. The other two conditions are optional for older clients.</remarks>
public record RecordSelfWeighInResource(
    int PatientId,
    decimal ValueKg,
    DateTimeOffset LocalTimestamp,
    bool FastedState,
    bool? SameTimeOfDay = null,
    bool? SameScale = null)
{
    /// <summary>The patient recording the reading. Must be the authenticated patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The reading in kilograms.</summary>
    public decimal ValueKg { get; init; } = ValueKg;

    /// <summary>When it was taken, as the device reads it. Never rewritten by the server.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;

    /// <summary>«¿Te pesaste en ayunas?». The protocol condition (IN-3).</summary>
    public bool FastedState { get; init; } = FastedState;

    /// <summary>Optional. No longer asked (IN-3); stored as declared when an older client sends it.</summary>
    public bool? SameTimeOfDay { get; init; } = SameTimeOfDay;

    /// <summary>Optional. No longer asked (IN-3); stored as declared when an older client sends it.</summary>
    public bool? SameScale { get; init; } = SameScale;
}

/// <summary>One entry as the device queued it while offline.</summary>
public record PendingDiaryEntryResource(
    Guid ClientEntryId,
    DateTimeOffset LocalTimestamp,
    string Provenance,
    string? PhotoRef,
    int? ReferenceFoodId,
    decimal? PortionGrams,
    decimal? Confidence,
    bool? Confirmed = null,
    string? PlanAdherence = null)
{
    /// <summary>The identifier the device generated. Resending it is recognised, never duplicated.</summary>
    public Guid ClientEntryId { get; init; } = ClientEntryId;

    /// <summary>When it happened, as the device read it. The server refuses to rewrite this.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;

    /// <summary>Photo or Manual. Required. OffPlan is still accepted from older clients (legacy, IN-1).</summary>
    public string Provenance { get; init; } = Provenance;

    /// <summary>Where the photo lives, when there is one.</summary>
    public string? PhotoRef { get; init; } = PhotoRef;

    /// <summary>The catalog entry, when the entry carries one.</summary>
    public int? ReferenceFoodId { get; init; } = ReferenceFoodId;

    /// <summary>The portion in grams, when the entry carries one.</summary>
    public decimal? PortionGrams { get; init; } = PortionGrams;

    /// <summary>The on-device confidence, when the entry came from a photo.</summary>
    public decimal? Confidence { get; init; } = Confidence;

    /// <summary>
    ///     False for a photo entry the patient had not confirmed yet («Por confirmar»): it is stored as
    ///     a proposal. Omitted by older clients, which confirm whatever carries a food.
    /// </summary>
    public bool? Confirmed { get; init; } = Confirmed;

    /// <summary>
    ///     «¿Esta comida estaba en tu plan?»: InPlan or OffPlan, when the entry is confirmed. Omitted by
    ///     older clients; their entries are accepted and stay NotAnswered.
    /// </summary>
    public string? PlanAdherence { get; init; } = PlanAdherence;
}

/// <summary>IN-4. One self weigh-in as the device queued it offline.</summary>
public record PendingSelfWeighInResource(
    Guid ClientEntryId,
    decimal ValueKg,
    DateTimeOffset LocalTimestamp,
    bool FastedState,
    bool? SameTimeOfDay = null,
    bool? SameScale = null)
{
    /// <summary>The identifier the device generated offline. Resending it is recognised, never duplicated.</summary>
    public Guid ClientEntryId { get; init; } = ClientEntryId;

    /// <summary>The reading in kilograms.</summary>
    public decimal ValueKg { get; init; } = ValueKg;

    /// <summary>When it was taken, as the device read it. Never rewritten by the server.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;

    /// <summary>«¿Te pesaste en ayunas?». The protocol condition (IN-3).</summary>
    public bool FastedState { get; init; } = FastedState;

    /// <summary>Optional. No longer asked (IN-3).</summary>
    public bool? SameTimeOfDay { get; init; } = SameTimeOfDay;

    /// <summary>Optional. No longer asked (IN-3).</summary>
    public bool? SameScale { get; init; } = SameScale;
}

/// <summary>IN-4. Payload of Sync Pending Self Weigh Ins: <c>{ entries: [...] }</c>.</summary>
public record SyncSelfWeighInsResource(IReadOnlyList<PendingSelfWeighInResource>? Entries, int? PatientId = null)
{
    /// <summary>The queued readings. Each one is reconciled on its own; the batch never fails whole.</summary>
    public IReadOnlyList<PendingSelfWeighInResource>? Entries { get; init; } = Entries;

    /// <summary>Optional. When sent, it must be the authenticated patient.</summary>
    public int? PatientId { get; init; } = PatientId;
}

/// <summary>Payload of Subflow 4.6 - Sync Pending Entries.</summary>
public record SyncPendingEntriesResource(int PatientId, IReadOnlyList<PendingDiaryEntryResource> Entries)
{
    /// <summary>The patient whose queue this is. Must be the authenticated patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>The queued entries. Each one is reconciled on its own; the batch never fails whole.</summary>
    public IReadOnlyList<PendingDiaryEntryResource> Entries { get; init; } = Entries;
}

/// <summary>IA-3. Payload of «Ver ideas» and «Ver otras ideas» (PT14.4).</summary>
public record GenerateMealIdeasResource(DateOnly? LocalDate, IReadOnlyList<string>? ExcludeIdeaIds = null)
{
    /// <summary>Today on the patient's device (yyyy-MM-dd). Required.</summary>
    public DateOnly? LocalDate { get; init; } = LocalDate;

    /// <summary>«Ver otras ideas»: the mealIdeaId of the ideas already seen, so the new ones are different.</summary>
    public IReadOnlyList<string>? ExcludeIdeaIds { get; init; } = ExcludeIdeaIds;
}

/// <summary>
///     IN-6. Payload of «Registrar esta comida» (PT14.5): one meal of several foods, logged as one entry per food
///     sharing a meal group.
/// </summary>
public record LogMealGroupResource(
    DateTimeOffset LocalTimestamp,
    IReadOnlyList<MealGroupItemResource>? Items,
    string? PlanAdherence = null,
    MealGroupOriginResource? Origin = null,
    int? PatientId = null)
{
    /// <summary>When the meal happened, as the device reads it. The same for every entry; never rewritten.</summary>
    public DateTimeOffset LocalTimestamp { get; init; } = LocalTimestamp;

    /// <summary>The foods, already resolved to the catalog (<c>referenceFoodId</c> of the idea), one to ten.</summary>
    public IReadOnlyList<MealGroupItemResource>? Items { get; init; } = Items;

    /// <summary>InPlan or OffPlan. Optional for an idea (InPlan); required otherwise.</summary>
    public string? PlanAdherence { get; init; } = PlanAdherence;

    /// <summary><c>{ kind: "MealIdea", mealIdeaId }</c> when the meal comes from an idea of IA-3.</summary>
    public MealGroupOriginResource? Origin { get; init; } = Origin;

    /// <summary>Optional; when present it must be the authenticated patient.</summary>
    public int? PatientId { get; init; } = PatientId;
}

/// <summary>IN-6. One food of the meal.</summary>
public record MealGroupItemResource(int ReferenceFoodId, decimal PortionGrams, Guid? ClientEntryId = null)
{
    /// <summary>The catalog entry. Required.</summary>
    public int ReferenceFoodId { get; init; } = ReferenceFoodId;

    /// <summary>The portion, in grams. Required, positive.</summary>
    public decimal PortionGrams { get; init; } = PortionGrams;

    /// <summary>
    ///     IN-7. The device's identifier of the entry of this item. When every item carries one, resending the meal
    ///     returns the entries already stored, without new ones and without evaluating the day again.
    /// </summary>
    public Guid? ClientEntryId { get; init; } = ClientEntryId;
}

/// <summary>IN-6. Where the meal came from.</summary>
public record MealGroupOriginResource(string? Kind, string? MealIdeaId = null)
{
    /// <summary>MealIdea.</summary>
    public string? Kind { get; init; } = Kind;

    /// <summary>The <c>mealIdeaId</c> of the idea. Accepted for traceability of the request; not stored.</summary>
    public string? MealIdeaId { get; init; } = MealIdeaId;
}
