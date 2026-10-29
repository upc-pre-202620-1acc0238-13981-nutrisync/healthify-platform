namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>The consultation in progress of a patient, if any.</summary>
public record GetInProgressConsultationByPatientIdQuery(int PatientId);
