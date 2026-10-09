namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>
///     Subflow 2.4 - Acknowledge Active Targets. An act of the relationship, not of the clinical
///     act: acknowledging does not change the plan.
/// </summary>
public record AcknowledgeActiveTargetsCommand(int CareLinkId, int PatientId, int PlanVersion);
