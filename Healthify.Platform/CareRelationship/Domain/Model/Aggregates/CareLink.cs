using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;

namespace Healthify.Platform.CareRelationship.Domain.Model.Aggregates;

/// <summary>
///     The consented relationship between a patient and a practitioner, and the single source of
///     truth on who may see whom. This is where the asymmetry principle is enforced technically
///     rather than by convention.
/// </summary>
/// <remarks>
///     Consent is stored as six columns (four, plus the two of AI processing since CR-2) and rebuilt
///     as a value object by the <see cref="Consent" /> property. That follows the guidance for optional value objects: a nullable owned type is
///     fragile in EF Core, and consent is genuinely absent between establishing the link and the
///     patient granting it.
/// </remarks>
public partial class CareLink
{
    /// <summary>Required by EF Core.</summary>
    protected CareLink()
    {
    }

    public CareLink(EstablishCareLinkCommand command)
    {
        // Business rule: Patient Cannot Self Link (Care Relationship, Subflow 2.2)
        if (command.PatientId == command.PractitionerId)
            throw new InvalidOperationException("A patient cannot be linked to themselves.");
        if (command.PatientId <= 0 || command.PractitionerId <= 0)
            throw new ArgumentException("A care link requires both participants.", nameof(command));

        PatientId = command.PatientId;
        PractitionerId = command.PractitionerId;
        EstablishedAt = DateTimeOffset.UtcNow;

        // Business rule: Link Starts Inactive Until Consent (Care Relationship, Subflow 2.2)
        // Nothing is readable until the patient says yes.
        ConsentGranted = false;
        ConsentScope = null;
        ConsentGrantedAt = null;
        ConsentWithdrawnAt = null;
        ConsentAiProcessingGranted = false;
        ConsentAiProcessingDecidedAt = null;
    }

    public CareLinkId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient account. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>Cross-context reference to the practitioner account. A plain int, no EF navigation.</summary>
    public int PractitionerId { get; private set; }

    public DateTimeOffset EstablishedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    ///     Which path revoked the link. Null while the link is not revoked.
    /// </summary>
    /// <remarks>
    ///     NOTE: technical field added by CR-1. Rows revoked before it existed were backfilled with
    ///     ConsentWithdrawn, the only revocation path at the time.
    /// </remarks>
    public RevocationReason? RevocationReason { get; private set; }

    public DateTimeOffset? DischargedAt { get; private set; }

    /// <summary>
    ///     The clinical justification recorded at discharge. Null while the link is open.
    /// </summary>
    /// <remarks>
    ///     TODO: field not listed in the aggregate contents of the implementation inventory, which
    ///     names only DischargedAt. Kept because the rule "Clinical Reason Required" is meaningless
    ///     if the reason is validated and then thrown away, and because Care Link History is the
    ///     read model that has to show it. Assumed interpretation: the reason is part of the
    ///     discharge, like its date. Source: event storming Subflow 2.5, technical document 4.4.
    /// </remarks>
    public string? DischargeReason { get; private set; }

    public int? PendingTargetsVersion { get; private set; }
    public int? LastAcknowledgedVersion { get; private set; }

    /// <summary>
    ///     CR-3. When the patient last acknowledged their targets (PAC-4 "Ana las vio el mismo día"). Null when they
    ///     never did, and for acknowledgements given before CR-3, whose moment was not recorded.
    /// </summary>
    public DateTimeOffset? LastAcknowledgedAt { get; private set; }

    // Persisted projection of the Consent value object.
    public bool ConsentGranted { get; private set; }
    public string? ConsentScope { get; private set; }
    public DateTimeOffset? ConsentGrantedAt { get; private set; }
    public DateTimeOffset? ConsentWithdrawnAt { get; private set; }

    /// <summary>CR-2. Persisted projection of <see cref="Consent.AiProcessingGranted" />.</summary>
    public bool ConsentAiProcessingGranted { get; private set; }

    /// <summary>CR-2. Persisted projection of <see cref="Consent.AiProcessingDecidedAt" />.</summary>
    public DateTimeOffset? ConsentAiProcessingDecidedAt { get; private set; }

    /// <summary>The consent value object, rebuilt from the mapped columns. Not itself a column.</summary>
    public Consent? Consent => ConsentGrantedAt is null
        ? null
        : new Consent(ConsentGranted, ConsentScope!, ConsentGrantedAt.Value, ConsentWithdrawnAt,
            ConsentAiProcessingGranted, ConsentAiProcessingDecidedAt);

    /// <summary>
    ///     CR-2. Whether the AI functions may process this patient's data through this link right now: the link is
    ///     active and the patient said yes to AI. This is the answer behind HasAiProcessingConsent.
    /// </summary>
    public bool HasAiProcessingConsent => IsActive && ConsentAiProcessingGranted;

    /// <summary>
    ///     Business rule: No Access Without Consent (Subflow 2.3). This is the answer the Open Host
    ///     Service gives the other five contexts.
    /// </summary>
    public bool IsActive => Consent is { IsGranted: true } && RevokedAt is null && DischargedAt is null;

    public bool IsDischarged => DischargedAt is not null;
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>
    ///     RM-1. Redeemed and still open, but the patient has not granted consent yet: the roster lists it as
    ///     PendingConsent and nothing about the patient is readable through it. A withdrawn consent is not pending.
    /// </summary>
    public bool IsPendingConsent => ConsentGrantedAt is null && RevokedAt is null && DischargedAt is null;

    /// <summary>Subflow 2.3 - Grant Consent.</summary>
    public void GrantConsent(GrantConsentCommand command)
    {
        // Business rule: Discharged Link Never Reactivated (Care Relationship, Subflow 2.5)
        if (IsDischarged)
            throw new InvalidOperationException("A discharged care link is never reactivated.");
        if (IsRevoked)
            throw new InvalidOperationException("A revoked care link cannot receive consent again.");
        if (Consent is { IsGranted: true })
            throw new InvalidOperationException("Consent has already been granted for this care link.");

        // Business rule: Consent Scope Recorded (Care Relationship, Subflow 2.3)
        // The value object refuses an empty scope, so a consent without a recorded scope
        // cannot exist.
        // CR-2: AI processing is part of the same consent, and off unless the patient turned it on. Leaving the
        // switch off is not recorded as a decision.
        var now = DateTimeOffset.UtcNow;
        var consent = new Consent(true, command.Scope, now, null, command.AiProcessingGranted,
            command.AiProcessingGranted ? now : null);

        Apply(consent);
    }

    /// <summary>CR-2. The patient turns AI processing on (PT2 "Usar funciones con IA").</summary>
    /// <returns>False when it was already on: nothing changes and nothing is published.</returns>
    public bool GrantAiProcessing()
    {
        // Business rule: AI Processing Needs Live Consent (CR-2). Only an active link can be processed.
        if (!IsActive)
            throw new InvalidOperationException("AI processing can only be granted on an active care link.");
        if (ConsentAiProcessingGranted) return false;

        Apply(Consent!.WithAiProcessing(true, DateTimeOffset.UtcNow));
        return true;
    }

    /// <summary>
    ///     CR-2. The patient turns AI processing off (PT21.IA). Always possible, like consent itself, and it changes
    ///     neither the plan nor the records.
    /// </summary>
    /// <returns>False when it was already off: nothing changes and nothing is published.</returns>
    public bool RevokeAiProcessing()
    {
        // Business rule: Consent Always Revocable (Subflow 2.3), applied to AI processing (CR-2).
        if (!ConsentAiProcessingGranted) return false;

        Apply(Consent!.WithAiProcessing(false, DateTimeOffset.UtcNow));
        return true;
    }

    /// <summary>Subflow 2.5 - Withdraw Consent.</summary>
    public void WithdrawConsent()
    {
        // Business rule: Consent Always Revocable (Care Relationship, Subflow 2.3)
        // Business rule: No Justification Required (Care Relationship, Subflow 2.5)
        // The method takes no reason argument on purpose: the patient never explains leaving.
        if (Consent is not { IsGranted: true })
            throw new InvalidOperationException("There is no live consent to withdraw.");

        // CR-2: withdrawing the whole consent also turns AI processing off.
        Apply(Consent.Withdraw(DateTimeOffset.UtcNow));
    }

    private void Apply(Consent consent)
    {
        ConsentGranted = consent.IsGranted;
        ConsentScope = consent.Scope;
        ConsentGrantedAt = consent.GrantedAt;
        ConsentWithdrawnAt = consent.WithdrawnAt;
        ConsentAiProcessingGranted = consent.AiProcessingGranted;
        ConsentAiProcessingDecidedAt = consent.AiProcessingDecidedAt;
    }

    /// <summary>
    ///     Subflow 2.5 - Revoke Care Link. Reached from the consent withdrawal policy, and from a
    ///     redemption that explicitly replaces the active link (CR-1).
    /// </summary>
    public void Revoke(RevocationReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        // Business rule: Revoked Link Kept With Revocation Date (Care Relationship, Subflow 2.5)
        // The row is never deleted: the history of the relationship survives its end.
        if (IsRevoked) throw new InvalidOperationException("This care link has already been revoked.");

        RevokedAt = DateTimeOffset.UtcNow;
        RevocationReason = reason;
        EndAiProcessing(RevokedAt.Value);
    }

    /// <summary>Subflow 2.5 - Discharge Patient.</summary>
    public void Discharge(ClinicalReason reason)
    {
        // Business rule: Discharged Link Never Reactivated (Care Relationship, Subflow 2.5)
        if (IsDischarged) throw new InvalidOperationException("This care link has already been discharged.");

        // Business rule: Clinical Reason Required (Care Relationship, Subflow 2.5)
        // The value object refuses an empty reason, so the argument itself carries the rule.
        DischargeReason = reason.Value;
        DischargedAt = DateTimeOffset.UtcNow;
        EndAiProcessing(DischargedAt.Value);
    }

    /// <summary>
    ///     Business rule: AI Processing Ends With The Link (CR-2). A revoked (any reason, a switch of practitioner
    ///     included) or discharged link never keeps the consent to AI processing: it goes off in the same change,
    ///     at the same moment, so the content generated through it can be purged. A new link starts without it.
    /// </summary>
    private void EndAiProcessing(DateTimeOffset at)
    {
        if (!ConsentAiProcessingGranted) return;
        Apply(Consent!.WithAiProcessing(false, at));
    }

    /// <summary>Subflow 2.4 - Mark Targets Pending Acknowledgement.</summary>
    public void MarkTargetsPending(int planVersion)
    {
        if (planVersion <= 0)
            throw new ArgumentException("A plan version must be a positive integer.", nameof(planVersion));

        // Business rule: One Pending Version At A Time (Care Relationship, Subflow 2.4)
        // A newer publication replaces the pending one rather than queueing behind it: the patient
        // acknowledges what is current, not a backlog.
        if (PendingTargetsVersion is not null && PendingTargetsVersion >= planVersion)
            throw new InvalidOperationException(
                "A pending targets version at least as recent as this one already exists.");

        PendingTargetsVersion = planVersion;
    }

    /// <summary>Subflow 2.4 - Acknowledge Active Targets.</summary>
    public void AcknowledgeActiveTargets(int planVersion)
    {
        if (PendingTargetsVersion is null)
            throw new InvalidOperationException("There is no pending targets version to acknowledge.");

        // Business rule: Acknowledged Version Not Newer Than Active (Care Relationship, Subflow 2.4)
        // A patient cannot acknowledge a version that was never published to them.
        if (planVersion > PendingTargetsVersion)
            throw new ArgumentException(
                "The acknowledged version is newer than the active one.", nameof(planVersion));

        // Business rule: Acknowledgement Does Not Change The Plan (Care Relationship, Subflow 2.4)
        // Only these two fields move. Nothing here reaches Nutritional Care, and no targets are
        // altered: acknowledging is an act of the relationship, not of the clinical act.
        LastAcknowledgedVersion = planVersion;
        LastAcknowledgedAt = DateTimeOffset.UtcNow;
        if (planVersion == PendingTargetsVersion) PendingTargetsVersion = null;
    }
}
