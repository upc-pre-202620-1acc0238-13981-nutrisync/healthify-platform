using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>Subflow 2.4. Stays inside Care Relationship.</summary>
public record TargetsPendingAcknowledgement(int CareLinkId, int PatientId, int PlanVersion) : DomainEventBase;
