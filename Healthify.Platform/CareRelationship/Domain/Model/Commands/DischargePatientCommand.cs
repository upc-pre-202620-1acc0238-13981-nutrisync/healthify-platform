namespace Healthify.Platform.CareRelationship.Domain.Model.Commands;

/// <summary>Subflow 2.5 - Discharge Patient. Only the linked practitioner may discharge.</summary>
public record DischargePatientCommand(int CareLinkId, int PractitionerId, string ClinicalReason);
