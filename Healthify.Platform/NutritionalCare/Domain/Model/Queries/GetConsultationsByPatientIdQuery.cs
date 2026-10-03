using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>NC-2 - The consultations of a patient (PT25 "Anteriores", PAC-3), optionally of one state.</summary>
public record GetConsultationsByPatientIdQuery(int PatientId, ConsultationState? State = null);
