namespace Healthify.Platform.CareRelationship.Interfaces.Acl;

/// <summary>Flat DTO exposed to other bounded contexts - primitives only.</summary>
/// <remarks>
///     RM-2 appends <c>EstablishedAt</c>, for "vinculada desde 12 mar. 2026"; CR-3 appends <c>LastAcknowledgedAt</c>.
/// </remarks>
public record CareLinkStatusItem(
    int CareLinkId,
    int PatientId,
    int PractitionerId,
    bool IsActive,
    bool HasConsent,
    int? LastAcknowledgedVersion,
    DateTimeOffset? EstablishedAt = null,
    DateTimeOffset? LastAcknowledgedAt = null);

/// <summary>
///     RM-1. One row of a practitioner's roster, primitives only. Only Active and PendingConsent links are
///     listed; revoked, discharged and withdrawn ones are history and stay out of the roster.
/// </summary>
/// <param name="CareLinkId">Identifier of the link.</param>
/// <param name="PatientId">The patient at the other end.</param>
/// <param name="LinkStatus">Active or PendingConsent.</param>
/// <param name="LinkedSince">When the link was established ("Vinculada desde 12 mar. 2026").</param>
public record RosterCareLinkItem(int CareLinkId, int PatientId, string LinkStatus, DateTimeOffset LinkedSince);

/// <summary>
///     Public ACL contract of the Care Relationship bounded context, and the Open Host Service the
///     other five contexts consume. It publishes essentially one question, "is this link active",
///     and everyone else is a Conformist to the answer. This is where the asymmetry principle is
///     enforced technically.
/// </summary>
/// <remarks>
///     An Open Host Service is a query pattern, not a publication one: asking whether a link is
///     active is not reacting to a past fact, which is why Consent Granted triggers no external
///     policy. Every parameter and return value is a primitive or a primitives-only DTO declared
///     here, and every method degrades gracefully rather than throwing.
/// </remarks>
public interface ICareRelationshipContextFacade
{
    /// <summary>
    ///     Whether this patient and this practitioner are linked with live consent right now.
    ///     Returns false when they are not, and also when the lookup fails: no answer means no access.
    /// </summary>
    Task<bool> IsCareLinkActive(int patientId, int practitionerId, CancellationToken ct = default);

    /// <summary>The link that currently grants access for a patient, or null.</summary>
    Task<CareLinkStatusItem?> GetActiveCareLinkByPatientId(int patientId, CancellationToken ct = default);

    /// <summary>
    ///     RM-1. The Active and PendingConsent links of a practitioner, oldest first. Empty when there are none
    ///     or the lookup fails.
    /// </summary>
    Task<IReadOnlyList<RosterCareLinkItem>> GetRosterCareLinks(int practitionerId, CancellationToken ct = default);

    /// <summary>
    ///     CR-2. Whether the patient allows the AI functions to process their data now: an active link whose consent
    ///     includes AI processing. Asked on every generation (IA-0, guard 2). False when the lookup fails.
    /// </summary>
    Task<bool> HasAiProcessingConsent(int patientId, CancellationToken ct = default);
}
