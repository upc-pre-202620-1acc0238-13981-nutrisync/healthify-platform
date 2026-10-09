using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;

/// <summary>
///     One thing the patient says they ate.
/// </summary>
/// <remarks>
///     Business rule: Entry Never Deleted (Subflow 4.2). There is no method on this class that
///     removes an entry and no endpoint that reaches one, because the diary is a record of what was
///     said, not a tidy summary of what should have been. An entry the patient regrets is still an
///     entry, and a diary that can be pruned cannot be interpreted.
///     Business rule: Proposal Kept Alongside Confirmation (Subflow 4.2). The proposed and the
///     confirmed estimate live side by side on the same row. Confirming does not overwrite the
///     proposal and adjusting does not delete it, so what a model guessed and what a person said
///     remain separately readable for as long as the entry exists.
///     This class carries no field that qualifies the entry. There is no compliance flag, no
///     deviation, no streak and no penalty, and there is no method that could set one. Comparing what
///     was prescribed against what was eaten belongs to Monitoring and Adherence; this context
///     records, and recording well means recording without an opinion.
///     The composite value objects are stored as flat columns and rebuilt by the computed properties
///     below, following the pattern the platform already uses.
/// </remarks>
public partial class DiaryEntry
{
    /// <summary>Required by EF Core.</summary>
    protected DiaryEntry()
    {
    }

    /// <summary>Subflows 4.2 and 4.3 - an entry comes into being with its provenance and its moment.</summary>
    /// <remarks>
    ///     IN-1. Off the plan is no longer a provenance: it is the patient's answer, recorded with the
    ///     confirmation. A new entry therefore cannot be created with <c>Provenance.OffPlan</c>; the
    ///     legacy paths that still need one go through <see cref="LegacyOffPlan" />.
    /// </remarks>
    public DiaryEntry(
        int patientId,
        LocalTimestamp localTimestamp,
        Provenance provenance,
        SyncState syncState,
        string? photoRef = null,
        Guid? clientEntryId = null)
        : this(patientId, localTimestamp, RefuseLegacyOffPlan(provenance), syncState, photoRef,
            clientEntryId, new PlanAdherence(PlanAdherence.NotAnswered))
    {
    }

    private DiaryEntry(
        int patientId,
        LocalTimestamp localTimestamp,
        Provenance provenance,
        SyncState syncState,
        string? photoRef,
        Guid? clientEntryId,
        PlanAdherence planAdherence)
    {
        PatientId = patientId;
        PlanAdherence = planAdherence;

        // Business rules: Local Timestamp Required and Provenance Required. Both are constructor
        // parameters of a type that refuses to exist empty, so an entry without them cannot be built.
        LocalTimestamp = localTimestamp.Value.DateTime;
        LocalUtcOffsetMinutes = (int)localTimestamp.Value.Offset.TotalMinutes;
        Provenance = provenance;

        SyncState = syncState;
        PhotoRef = string.IsNullOrWhiteSpace(photoRef) ? null : photoRef.Trim();
        ClientEntryId = clientEntryId;
    }

    /// <summary>
    ///     Subflow 4.4 - Log Off Plan Meal, kept only for the deprecated one-tap endpoint and for
    ///     offline queues sent by older clients.
    /// </summary>
    /// <remarks>
    ///     Deprecated by IN-1. The entry carries no food and no portion, exactly as before, and its
    ///     answer is recorded as off the plan so that it reads the same as the rows backfilled by the
    ///     migration.
    /// </remarks>
    public static DiaryEntry LegacyOffPlan(
        int patientId,
        LocalTimestamp localTimestamp,
        SyncState syncState,
        Guid? clientEntryId = null)
    {
        return new DiaryEntry(patientId, localTimestamp, new Provenance(Provenance.OffPlan), syncState, null,
            clientEntryId, new PlanAdherence(PlanAdherence.OffPlan));
    }

    public DiaryEntryId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient account. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>
    ///     The wall clock the patient was reading when they logged, stored exactly as it arrived.
    /// </summary>
    /// <remarks>
    ///     Business rule: Declared Local Timestamp Never Rewritten (Subflow 4.6). Three decisions are
    ///     compressed into this property and the one below it.
    ///     It is stored as the local wall clock rather than as an instant because the database maps
    ///     <see cref="DateTimeOffset" /> onto a plain datetime and normalises it: an entry logged at
    ///     nine in the evening in Lima would come back as two in the morning of the following day,
    ///     and the diary would file it under a day the patient never lived.
    ///     It keeps this exact name because the shared UTC interceptor excludes properties called
    ///     <c>LocalTimestamp</c>, and it is a plain type rather than a converted one because the
    ///     diary is read by date range, which a converted type would not translate into SQL.
    /// </remarks>
    public DateTime LocalTimestamp { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. The offset the device declared,
    ///     stored beside the wall clock so that the moment can be rebuilt exactly as it was given.
    /// </summary>
    public int LocalUtcOffsetMinutes { get; private set; }

    /// <summary>The moment the patient declared, rebuilt whole. Never rewritten by this server.</summary>
    public DateTimeOffset DeclaredLocalTimestamp =>
        new(LocalTimestamp, TimeSpan.FromMinutes(LocalUtcOffsetMinutes));

    public Provenance Provenance { get; private set; } = null!;

    /// <summary>
    ///     IN-1. Whether the patient said the meal was in their plan. Set with the confirmation and
    ///     never rewritten afterwards; <c>NotAnswered</c> while the entry waits to be confirmed.
    /// </summary>
    public PlanAdherence PlanAdherence { get; private set; } = null!;

    /// <summary>
    ///     Where the photo lives, when there is one.
    /// </summary>
    /// <remarks>
    ///     TODO: hotspot (event storming 4, hotspot 2) - is the photo kept or discarded after the
    ///     estimate? With no professional validation of estimates, the main reason to keep it is
    ///     gone. Assumed interpretation: the field is a reference the client owns, this server never
    ///     stores image bytes, and nothing here deletes it. Deciding a retention period is a product
    ///     decision that has not been taken. Source: event storming v3, section 4, hotspot 2.
    /// </remarks>
    public string? PhotoRef { get; private set; }

    // Persisted projection of the ProposedEstimate value object, absent until the estimator runs.
    public int? ProposedReferenceFoodId { get; private set; }
    public decimal? ProposedPortionGrams { get; private set; }
    public decimal? ProposedConfidence { get; private set; }
    public DateTimeOffset? ProposedEstimatedAt { get; private set; }

    // Persisted projection of the ConfirmedEstimate value object, absent until the patient speaks.
    public int? ConfirmedReferenceFoodId { get; private set; }
    public decimal? ConfirmedPortionGrams { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    public SyncState SyncState { get; private set; } = null!;

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. The identifier the client device
    ///     generated while offline. It is what makes Subflow 4.6 idempotent: a resent entry is
    ///     recognised rather than duplicated. Null for entries created directly against the server.
    /// </summary>
    public Guid? ClientEntryId { get; private set; }

    /// <summary>
    ///     IN-6. The meal several entries of one moment make together (an idea of IA-3 has several ingredients and
    ///     an entry holds one food), so the diary can show them as one meal. Null for an entry logged alone.
    /// </summary>
    public Guid? MealGroupId { get; private set; }

    /// <summary>IN-6. Where a grouped entry came from (<c>MealIdea</c>), or null.</summary>
    public EntryOrigin? Origin { get; private set; }

    /// <summary>
    ///     IN-7. NOTE: technical field. The photo analysis the proposal of this entry came from (the AI recognized
    ///     the dish on the server). Unique: one analysis is logged once. The analysis itself is purged after 24 hours;
    ///     this identifier stays as a trace and points to nothing that holds patient data.
    /// </summary>
    public Guid? MealPhotoAnalysisId { get; private set; }

    /// <summary>IN-7. NOTE: technical field. The <c>ai_generations</c> row that produced the proposal, or null.</summary>
    public long? ProposedAiGenerationId { get; private set; }

    /// <summary>Rebuilt from the stored columns, or null while the estimator has not run.</summary>
    public ProposedEstimate? ProposedEstimate => ProposedReferenceFoodId is null
        ? null
        : new ProposedEstimate(ProposedReferenceFoodId.Value, ProposedPortionGrams!.Value,
            new Confidence(ProposedConfidence!.Value), ProposedEstimatedAt!.Value);

    /// <summary>Rebuilt from the stored columns, or null while the patient has not confirmed.</summary>
    public ConfirmedEstimate? ConfirmedEstimate => ConfirmedReferenceFoodId is null
        ? null
        : new ConfirmedEstimate(ConfirmedReferenceFoodId.Value, ConfirmedPortionGrams!.Value,
            ConfirmedAt!.Value);

    public bool HasProposedEstimate => ProposedReferenceFoodId is not null;
    public bool HasConfirmedEstimate => ConfirmedReferenceFoodId is not null;

    /// <summary>The calendar day the patient was living when they logged, not the server day.</summary>
    public DateOnly LocalDate => DateOnly.FromDateTime(LocalTimestamp);

    /// <summary>Subflow 4.2 - Estimate Portion. Called by the policy, with what the device computed.</summary>
    public void ProposeEstimate(ProposedEstimate estimate)
    {
        // Business rule: Estimate Stored As Proposal Only (Subflow 4.2). A proposal never arrives
        // after the patient has spoken: at that point it would be a model correcting a person.
        if (HasConfirmedEstimate)
            throw new InvalidOperationException(
                "This entry has already been confirmed by the patient.");

        ProposedReferenceFoodId = estimate.ReferenceFoodId;
        ProposedPortionGrams = estimate.PortionGrams;
        ProposedConfidence = estimate.Confidence.Value;
        ProposedEstimatedAt = estimate.EstimatedAt;
    }

    /// <summary>Subflow 4.2 - Confirm Estimate. The patient accepts the proposal as it stands.</summary>
    /// <param name="adherence">IN-1. The patient's answer to «¿Esta comida estaba en tu plan?». Required.</param>
    public void ConfirmProposedEstimate(PlanAdherence adherence)
    {
        RequireAnswer(adherence);
        if (!HasProposedEstimate)
            throw new InvalidOperationException("There is no proposal to confirm on this entry.");
        if (HasConfirmedEstimate)
            throw new InvalidOperationException("This entry has already been confirmed.");

        // Business rule: Proposal Kept Alongside Confirmation (Subflow 4.2). The proposal columns
        // are read here and left exactly as they are.
        StoreConfirmation(ProposedReferenceFoodId!.Value, ProposedPortionGrams!.Value);
        PlanAdherence = adherence;
    }

    /// <summary>Subflow 4.2 - Adjust Estimate. The patient corrects the proposal.</summary>
    /// <param name="referenceFoodId">The food the patient chose instead.</param>
    /// <param name="portionGrams">The portion the patient chose instead.</param>
    /// <param name="adherence">IN-1. The patient's answer to «¿Esta comida estaba en tu plan?». Required.</param>
    public void AdjustProposedEstimate(int referenceFoodId, decimal portionGrams, PlanAdherence adherence)
    {
        RequireAnswer(adherence);
        if (!HasProposedEstimate)
            throw new InvalidOperationException("There is no proposal to adjust on this entry.");
        if (HasConfirmedEstimate)
            throw new InvalidOperationException("This entry has already been confirmed.");

        // Business rule: Proposal Kept Alongside Confirmation (Subflow 4.2). The correction is
        // written beside the proposal, never over it, so the size of the correction stays visible.
        StoreConfirmation(referenceFoodId, portionGrams);
        PlanAdherence = adherence;
    }

    /// <summary>Subflow 4.3 - Log Meal Manually. What the patient typed is a confirmation from the start.</summary>
    /// <param name="estimate">What the patient declared.</param>
    /// <param name="adherence">IN-1. The patient's answer to «¿Esta comida estaba en tu plan?». Required.</param>
    public void ConfirmDirectly(ConfirmedEstimate estimate, PlanAdherence adherence)
    {
        RequireAnswer(adherence);
        if (HasConfirmedEstimate)
            throw new InvalidOperationException("This entry has already been confirmed.");

        StoreConfirmation(estimate.ReferenceFoodId, estimate.PortionGrams);
        PlanAdherence = adherence;
    }

    /// <summary>
    ///     Subflow 4.6 - an entry queued offline by a client older than IN-1, which never asked the
    ///     question. It is confirmed as the device declared it and its answer stays <c>NotAnswered</c>:
    ///     refusing it would discard something the patient really recorded.
    /// </summary>
    public void ConfirmFromLegacySync(ConfirmedEstimate estimate)
    {
        if (HasConfirmedEstimate)
            throw new InvalidOperationException("This entry has already been confirmed.");

        StoreConfirmation(estimate.ReferenceFoodId, estimate.PortionGrams);
    }

    /// <summary>
    ///     IN-6. Places a new entry in the meal it was logged with. Set once, before the entry is stored; a grouping
    ///     is part of what the patient said and, like the moment, it is never rewritten.
    /// </summary>
    public void JoinMealGroup(Guid mealGroupId, EntryOrigin? origin)
    {
        if (mealGroupId == Guid.Empty)
            throw new ArgumentException("A meal group needs an identifier.", nameof(mealGroupId));
        if (MealGroupId is not null)
            throw new InvalidOperationException("This entry already belongs to a meal group.");

        MealGroupId = mealGroupId;
        Origin = origin;
    }

    /// <summary>
    ///     IN-7. Records that the proposal of this new entry is the one the AI made from a photo analysis. Set once,
    ///     before the entry is stored, and never rewritten: like the proposal itself, it is what happened.
    /// </summary>
    public void RecordPhotoAnalysis(Guid analysisId, long aiGenerationId)
    {
        if (analysisId == Guid.Empty)
            throw new ArgumentException("A photo analysis needs an identifier.", nameof(analysisId));
        if (aiGenerationId <= 0)
            throw new ArgumentException("A photo analysis is traced to its AI generation.", nameof(aiGenerationId));
        if (!Provenance.IsPhoto)
            throw new InvalidOperationException("Only a photo entry comes from a photo analysis.");
        if (MealPhotoAnalysisId is not null)
            throw new InvalidOperationException("This entry already comes from a photo analysis.");

        MealPhotoAnalysisId = analysisId;
        ProposedAiGenerationId = aiGenerationId;
    }

    /// <summary>Subflow 4.6 - the entry reached the server and is reconciled.</summary>
    public void MarkSynchronized()
    {
        SyncState = new SyncState(SyncState.Synced);
    }

    /// <summary>Subflow 4.6 - the incoming copy disagreed with the stored one.</summary>
    public void MarkConflicted()
    {
        SyncState = new SyncState(SyncState.Conflicted);
    }

    /// <summary>
    ///     Subflow 4.6 - Last Write Wins, applied to the only part of an entry that can legitimately
    ///     change after the fact.
    /// </summary>
    /// <remarks>
    ///     Business rule: Declared Local Timestamp Never Rewritten (Subflow 4.6). Note what this
    ///     method cannot touch. Last-write-wins resolves the estimate, never the moment: the patient
    ///     said when it happened once, and a later copy of the same entry does not get to move it.
    /// </remarks>
    /// <param name="referenceFoodId">The food the latest copy carries.</param>
    /// <param name="portionGrams">The portion the latest copy carries.</param>
    /// <param name="confirmedAt">When the server reconciled it.</param>
    /// <param name="adherence">
    ///     IN-1. The answer the latest copy carries, if any. It is recorded only while the entry has
    ///     none; an answer already given is never rewritten by a later copy.
    /// </param>
    /// <returns>True when the stored entry actually changed.</returns>
    public bool ResolveWithLatest(int referenceFoodId, decimal portionGrams, DateTimeOffset confirmedAt,
        PlanAdherence? adherence = null)
    {
        // DECISIÓN §12-#3: «¿Estaba en tu plan?» cannot be corrected once answered in this version.
        // A later copy may only fill an answer that is still missing.
        var answerChanged = false;
        if (adherence is { IsAnswered: true } && !PlanAdherence.IsAnswered)
        {
            PlanAdherence = adherence;
            answerChanged = true;
        }

        if (ConfirmedReferenceFoodId == referenceFoodId && ConfirmedPortionGrams == portionGrams)
            return answerChanged;

        ConfirmedReferenceFoodId = referenceFoodId;
        ConfirmedPortionGrams = decimal.Round(portionGrams, 2);
        ConfirmedAt = confirmedAt;
        return true;
    }

    private static Provenance RefuseLegacyOffPlan(Provenance provenance)
    {
        if (provenance.IsOffPlan)
            throw new ArgumentException(
                "Off the plan is an answer recorded with the confirmation, not a provenance.",
                nameof(provenance));
        return provenance;
    }

    private static void RequireAnswer(PlanAdherence adherence)
    {
        ArgumentNullException.ThrowIfNull(adherence);

        // Business rule: Plan Adherence Required On Confirmation (IN-1). Equivalent to the UI
        // validation «Responde Sí o No»: a confirmed entry always says whether it was in the plan.
        if (!adherence.IsAnswered)
            throw new ArgumentException("Confirming an entry requires answering whether it was in the plan.",
                nameof(adherence));
    }

    private void StoreConfirmation(int referenceFoodId, decimal portionGrams)
    {
        var confirmed = new ConfirmedEstimate(referenceFoodId, portionGrams, DateTimeOffset.UtcNow);

        ConfirmedReferenceFoodId = confirmed.ReferenceFoodId;
        ConfirmedPortionGrams = confirmed.PortionGrams;
        ConfirmedAt = confirmed.ConfirmedAt;
    }
}
