using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

/// <summary>
///     A signal received from Monitoring that is waiting for a human decision.
/// </summary>
/// <remarks>
///     This is where the automation ends. A signal notifies and never modifies: there is no automatic
///     path from a review item to Adjust Nutrition Plan. The chain enters through a policy, creates one
///     of these, and dies in the inbox. That a person takes the next step is not a user experience
///     detail, it is what stops an algorithm changing a clinical plan.
///     NC-10 reformulates the rule as "No signal or algorithm modifies the plan without an explicit action
///     of the practitioner": a sustained deviation may carry an AI <see cref="PlanAdjustmentProposal" />,
///     which is a text in the inbox. Attaching it changes this aggregate only; the plan changes when, and
///     only when, the practitioner accepts it (<see cref="ResolveByAssigningPlan" />).
/// </remarks>
public partial class ReviewItem
{
    /// <summary>Required by EF Core.</summary>
    protected ReviewItem()
    {
    }

    public ReviewItem(OpenReviewItemCommand command, int practitionerId)
    {
        if (string.IsNullOrWhiteSpace(command.Evidence))
            throw new ArgumentException("A review item must carry the evidence that raised it.",
                nameof(command));

        PatientId = command.PatientId;
        PractitionerId = practitionerId;
        SignalType = new SignalType(command.SignalType);
        Evidence = command.Evidence.Trim();
        EvidenceData = command.EvidenceData is { } data
            ? new ReviewItemEvidence(data.AveragePercentFromTarget, data.DeviatedDays, data.LoggedDaysConsidered,
                data.Direction, data.AdjustedOn, data.AdjustedPlanVersion, data.AverageEnergyKcalFromTarget,
                data.ConsistencyKgPerWeek, data.ConsistencyState, data.AlertSinceOn, data.WeeksInAlert,
                data.ShownToPatientOn)
            : null;
        State = new ReviewItemState(ReviewItemState.Open);
        ResolvedWithAdjustment = null;
        ResolvedAt = null;
    }

    public ReviewItemId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient the signal is about.</summary>
    public int PatientId { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the aggregate contents in the inventory. Resolved from
    ///     the care link when the item is opened, because the inbox is read per practitioner and the
    ///     ACL contract of this context exposes an open item count per practitioner.
    /// </summary>
    public int PractitionerId { get; private set; }

    public SignalType SignalType { get; private set; } = null!;

    /// <summary>What Monitoring observed. Evidence, not a verdict.</summary>
    public string Evidence { get; private set; } = null!;

    /// <summary>
    ///     NC-11. The same evidence as numbers, for the client to word in its language. Null for items opened before
    ///     NC-11 and for signals that carry no numbers (consistency escalation).
    /// </summary>
    public ReviewItemEvidence? EvidenceData { get; private set; }

    public ReviewItemState State { get; private set; } = null!;

    /// <summary>Business rule: Resolution States Whether The Plan Was Adjusted (Subflow 3.7).</summary>
    public bool? ResolvedWithAdjustment { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>
    ///     The note of the resolution. For <see cref="ResolutionNoteCode.Custom" /> it is the practitioner's text; for a
    ///     system code it is the Spanish fallback of clients that do not know the code (X-2); null when nothing was
    ///     written.
    /// </summary>
    public string? ResolutionNote { get; private set; }

    /// <summary>
    ///     X-2. <see cref="ResolutionNoteCode" />: Custom, PlanAssignedAsIs, PlanAssignedWithEdits or ProposalDiscarded.
    ///     Null while the item is open, and when it was resolved without a note and without a proposal to discard.
    /// </summary>
    public string? ResolutionNoteCode { get; private set; }

    /// <summary>X-2. The parameters of <see cref="ResolutionNoteCode" />, or null.</summary>
    public ResolutionNoteData? ResolutionNoteData { get; private set; }

    /// <summary>NC-10. The AI plan proposal attached to a sustained deviation, or null.</summary>
    public PlanAdjustmentProposal? Proposal { get; private set; }

    /// <summary>
    ///     NC-10. "Revisar de nuevo en N días": when the scheduled recheck is due (<c>ResolvedAt + RecheckAfterDays</c>),
    ///     set when a practitioner assigns a proposed plan. Null otherwise.
    /// </summary>
    public DateTimeOffset? RecheckDueAt { get; private set; }

    /// <summary>
    ///     NC-10. When the recheck item was opened, or skipped because nobody cares for the patient any more. NOTE:
    ///     technical field, it keeps the recheck policy idempotent.
    /// </summary>
    public DateTimeOffset? RecheckIssuedAt { get; private set; }

    public bool IsOpen => State.IsOpen;

    /// <summary>NC-11/NC-10. Whether PR14.IA opens instead of PR14.</summary>
    public bool HasPlanProposal => Proposal is not null;

    /// <summary>NC-10. A recheck is waiting for its date.</summary>
    public bool IsRecheckPending => RecheckDueAt is not null && RecheckIssuedAt is null;

    /// <summary>Subflow 3.7 - Resolve Review Item.</summary>
    /// <remarks>
    ///     NC-10: also "Resolver sin asignar". A proposal still waiting is dismissed: the plan stays as it is, and the
    ///     proposal remains as the record of what was not assigned.
    /// </remarks>
    public void Resolve(bool resolvedWithAdjustment, string? note)
    {
        if (!IsOpen) throw new InvalidOperationException("This review item has already been resolved.");

        // Business rule: Resolution States Whether The Plan Was Adjusted (Nutritional Care, 3.7)
        // Closing an item without saying whether the plan moved would lose the only record of what
        // the signal actually caused.
        ResolvedWithAdjustment = resolvedWithAdjustment;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        State = new ReviewItemState(ReviewItemState.Resolved);
        ResolvedAt = DateTimeOffset.UtcNow;
        var discarding = Proposal is { IsProposed: true };
        if (discarding) Proposal!.Dismiss();

        // X-2. The practitioner's note is theirs (Custom); without one, a discarded proposal is said by a code.
        // DECISIÓN X-2: ProposalDiscarded only when there is no note, so the practitioner's text is never replaced;
        // the dismissal stays readable on the proposal (status Dismissed) either way.
        ResolutionNoteCode = ResolutionNote is not null ? ValueObjects.ResolutionNoteCode.Custom
            : discarding ? ValueObjects.ResolutionNoteCode.ProposalDiscarded
            : null;
        ResolutionNoteData = null;
    }

    /// <summary>
    ///     NC-10 - The AI proposal for a sustained deviation, generated in the background after the item opened.
    ///     Changes this aggregate only: no plan is written, no version is created.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Not a sustained deviation (DECISIÓN §12-#11), already resolved, or a proposal is already attached.
    /// </exception>
    public PlanAdjustmentProposal AttachProposal(long aiGenerationId, string title, decimal energyKcal,
        decimal proteinG, decimal carbG, decimal fatG, IEnumerable<string> addedGuidelines,
        IEnumerable<string> removedGuidelines, string patientMessage, int recheckAfterDays, string rationale,
        DateTimeOffset generatedAt, string? practitionerLanguage = null, string? patientLanguage = null)
    {
        // Business rule: Proposal Only For A Sustained Deviation (NC-10). The other signals keep PR14.
        if (!SignalType.IsSustainedDeviation)
            throw new InvalidOperationException("Only a sustained deviation carries a plan proposal.");
        if (!IsOpen) throw new InvalidOperationException("A resolved review item takes no plan proposal.");
        if (Proposal is not null)
            throw new InvalidOperationException("This review item already has a plan proposal.");

        Proposal = new PlanAdjustmentProposal(aiGenerationId, title, energyKcal, proteinG, carbG, fatG,
            addedGuidelines, removedGuidelines, patientMessage, recheckAfterDays, rationale, generatedAt,
            practitionerLanguage, patientLanguage);
        return Proposal;
    }

    /// <summary>
    ///     IA-8 - The patient's AI processing ended (consent withdrawn, link ended or discharge): a proposal no
    ///     practitioner accepted is removed with what the AI wrote in it. An accepted one stays, because it produced a
    ///     version of the plan and is part of the clinical record. The item itself is untouched and keeps working as
    ///     PR14 without AI.
    /// </summary>
    /// <returns>Whether a proposal was removed.</returns>
    public bool PurgeUnacceptedProposal()
    {
        // Business rule: Accepted Proposal Is Clinical Record (IA-8).
        if (Proposal is null || Proposal.IsAccepted) return false;

        Proposal = null;
        return true;
    }

    /// <summary>
    ///     NC-10 - PR14.IA "Resolver" (as is) or PR14.IA-A "Asignar plan ajustado" (with edits): the practitioner
    ///     assigned version <paramref name="planVersion" /> from the proposal. The item is resolved with an
    ///     adjustment, with the automatic note, and the recheck is scheduled.
    /// </summary>
    /// <remarks>
    ///     Called by the acceptance command only, in the same transaction that creates the version and supersedes the
    ///     previous one: the item never says a plan was assigned that does not exist, nor the reverse.
    /// </remarks>
    public void ResolveByAssigningPlan(int planVersion, bool acceptedAsIs)
    {
        if (!IsOpen) throw new InvalidOperationException("This review item has already been resolved.");
        if (Proposal is null) throw new InvalidOperationException("This review item has no plan proposal.");
        if (planVersion <= 0) throw new ArgumentException("A plan version is positive.", nameof(planVersion));

        Proposal.Accept(planVersion, acceptedAsIs);

        // Business rule: Resolution States Whether The Plan Was Adjusted (3.7): it was, by this act.
        ResolvedWithAdjustment = true;
        // X-2: a code with the version, for the client to word in the reader's language; the Spanish sentence stays
        // as the fallback of clients that do not know the code.
        ResolutionNote = acceptedAsIs
            ? $"Plan v{planVersion} asignado (propuesta IA aceptada tal cual)"
            : $"Plan v{planVersion} asignado (propuesta IA aceptada con ediciones)";
        ResolutionNoteCode = acceptedAsIs
            ? ValueObjects.ResolutionNoteCode.PlanAssignedAsIs
            : ValueObjects.ResolutionNoteCode.PlanAssignedWithEdits;
        ResolutionNoteData = new ResolutionNoteData(planVersion);
        State = new ReviewItemState(ReviewItemState.Resolved);
        ResolvedAt = DateTimeOffset.UtcNow;
        RecheckDueAt = ResolvedAt.Value.AddDays(Proposal.RecheckAfterDays);
    }

    /// <summary>NC-10 - The scheduled recheck was opened, or is not needed any more. Once per item.</summary>
    public void MarkRecheckIssued(DateTimeOffset issuedAt)
    {
        if (!IsRecheckPending) throw new InvalidOperationException("No recheck is pending on this review item.");
        RecheckIssuedAt = issuedAt;
    }
}
