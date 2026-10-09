namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>
///     A care link as the platform exposes it. Read models: Care Link Status when active, Care Link
///     History once it has ended, Practitioner Patient List in a collection.
/// </summary>
public record CareLinkResource(
    int CareLinkId,
    int PatientId,
    int PractitionerId,
    bool IsActive,
    bool HasConsent,
    string? ConsentScope,
    DateTimeOffset EstablishedAt,
    DateTimeOffset? ConsentGrantedAt,
    DateTimeOffset? ConsentWithdrawnAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? DischargedAt,
    string? DischargeReason,
    int? PendingTargetsVersion,
    int? LastAcknowledgedVersion,
    bool AiProcessingGranted = false,
    DateTimeOffset? AiProcessingDecidedAt = null)
{
    /// <summary>Identifier of the care link.</summary>
    public int CareLinkId { get; init; } = CareLinkId;

    /// <summary>Identifier of the linked patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Identifier of the linked practitioner.</summary>
    public int PractitionerId { get; init; } = PractitionerId;

    /// <summary>
    ///     Whether the link grants access right now. False until the patient grants consent, and
    ///     false again once it is withdrawn, revoked or the patient is discharged.
    /// </summary>
    public bool IsActive { get; init; } = IsActive;

    /// <summary>Whether consent is currently granted.</summary>
    public bool HasConsent { get; init; } = HasConsent;

    /// <summary>What the patient agreed to share. Null while no consent has ever been granted.</summary>
    public string? ConsentScope { get; init; } = ConsentScope;

    /// <summary>When the link was established by redeeming an invitation.</summary>
    public DateTimeOffset EstablishedAt { get; init; } = EstablishedAt;

    /// <summary>When consent was granted, or null.</summary>
    public DateTimeOffset? ConsentGrantedAt { get; init; } = ConsentGrantedAt;

    /// <summary>When consent was withdrawn, or null.</summary>
    public DateTimeOffset? ConsentWithdrawnAt { get; init; } = ConsentWithdrawnAt;

    /// <summary>When the link was revoked, or null. The row is kept, never deleted.</summary>
    public DateTimeOffset? RevokedAt { get; init; } = RevokedAt;

    /// <summary>When the treatment was discharged, or null. A discharged link is never reactivated.</summary>
    public DateTimeOffset? DischargedAt { get; init; } = DischargedAt;

    /// <summary>The clinical reason recorded at discharge, or null.</summary>
    public string? DischargeReason { get; init; } = DischargeReason;

    /// <summary>Version of the published targets awaiting acknowledgement, or null.</summary>
    public int? PendingTargetsVersion { get; init; } = PendingTargetsVersion;

    /// <summary>Most recent version the patient acknowledged, or null.</summary>
    public int? LastAcknowledgedVersion { get; init; } = LastAcknowledgedVersion;

    /// <summary>
    ///     CR-2. Whether the patient allows the AI functions to process their data. False when consent was
    ///     withdrawn: withdrawing it also turns AI off.
    /// </summary>
    public bool AiProcessingGranted { get; init; } = AiProcessingGranted;

    /// <summary>CR-2. When the patient last turned AI processing on or off; null while they never decided.</summary>
    public DateTimeOffset? AiProcessingDecidedAt { get; init; } = AiProcessingDecidedAt;
}
