namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     Subflow 2.4 - Mark Targets Pending Acknowledgement. Issued by the policy that reacts to
///     Active Targets Updated, published by Nutritional Care. It has no REST endpoint.
/// </summary>
public record MarkTargetsPendingAcknowledgementCommand(int PatientId, int PlanVersion);
