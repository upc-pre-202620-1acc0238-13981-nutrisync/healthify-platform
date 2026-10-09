using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>
///     Subflow 2.5. Integration event 2 of 13: crosses into Monitoring and Adherence, whose policy
///     closes the evaluation window (Subflow 5.11).
/// </summary>
/// <remarks>
///     Reason (CR-1): the revocation reason as a plain string, ConsentWithdrawn or SwitchedPractitioner, so
///     consumers in other contexts never depend on the value object.
/// </remarks>
public record CareLinkRevoked(
    int CareLinkId,
    int PatientId,
    int PractitionerId,
    DateTimeOffset RevokedAt,
    string Reason = RevocationReason.ConsentWithdrawnValue)
    : DomainEventBase;
