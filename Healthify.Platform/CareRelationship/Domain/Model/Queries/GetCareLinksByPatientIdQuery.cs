namespace Healthify.Platform.CareRelationship.Domain.Model.Queries;

/// <summary>Read model: Care Link History of a patient (data export). Every link, oldest first.</summary>
public record GetCareLinksByPatientIdQuery(int PatientId);
