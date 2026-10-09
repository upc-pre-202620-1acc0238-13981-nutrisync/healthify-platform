namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>
///     Read model: Targets Read Status. Tells the practitioner whether the patient has seen the
///     targets that were published to them. It says nothing about whether they are following them.
/// </summary>
public record TargetsReadStatusResource(
    int CareLinkId,
    int PatientId,
    int? PendingTargetsVersion,
    int? LastAcknowledgedVersion,
    bool HasPendingAcknowledgement,
    DateTimeOffset? LastAcknowledgedAt = null)
{
    /// <summary>Identifier of the care link.</summary>
    public int CareLinkId { get; init; } = CareLinkId;

    /// <summary>Identifier of the linked patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Version awaiting acknowledgement, or null when there is nothing pending.</summary>
    public int? PendingTargetsVersion { get; init; } = PendingTargetsVersion;

    /// <summary>Most recent version the patient acknowledged, or null.</summary>
    public int? LastAcknowledgedVersion { get; init; } = LastAcknowledgedVersion;

    /// <summary>Whether a published version is still waiting to be acknowledged.</summary>
    public bool HasPendingAcknowledgement { get; init; } = HasPendingAcknowledgement;

    /// <summary>
    ///     CR-3. When the patient acknowledged <see cref="LastAcknowledgedVersion" /> ("Ana las vio el mismo día"),
    ///     or null when unknown (never acknowledged, or acknowledged before the moment was recorded).
    /// </summary>
    public DateTimeOffset? LastAcknowledgedAt { get; init; } = LastAcknowledgedAt;
}
