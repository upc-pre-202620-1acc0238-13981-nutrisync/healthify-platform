namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>NC-2 - PAC-1.C "Consulta en curso": the consultation in progress of a patient, if any.</summary>
public record GetInProgressConsultationByPatientIdQuery(int PatientId);
