using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>
///     Subflow 2.2. Integration event 1 of 13: crosses into Monitoring and Adherence, whose policy
///     opens the evaluation window (Subflow 5.1).
/// </summary>
public record CareLinkEstablished(int CareLinkId, int PatientId, int PractitionerId) : DomainEventBase;
