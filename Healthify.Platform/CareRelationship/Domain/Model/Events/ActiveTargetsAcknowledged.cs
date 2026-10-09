using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>
///     Subflow 2.4. Stays inside Care Relationship: the plan does not change because the patient
///     read it. The practitioner sees it through the Targets Read Status read model.
/// </summary>
public record ActiveTargetsAcknowledged(int CareLinkId, int PatientId, int PlanVersion) : DomainEventBase;
