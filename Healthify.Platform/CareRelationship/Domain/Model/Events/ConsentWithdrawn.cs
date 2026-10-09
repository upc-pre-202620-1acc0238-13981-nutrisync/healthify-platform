using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>
///     Subflow 2.5. Consumed by this context own policy, which revokes the care link.
/// </summary>
public record ConsentWithdrawn(int CareLinkId, int PatientId, DateTimeOffset WithdrawnAt) : DomainEventBase;
