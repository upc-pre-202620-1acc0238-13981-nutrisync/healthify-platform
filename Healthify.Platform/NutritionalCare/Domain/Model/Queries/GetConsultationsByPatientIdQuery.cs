using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>The consultations of a patient (the past consultations list), optionally of one state.</summary>
public record GetConsultationsByPatientIdQuery(int PatientId, ConsultationState? State = null);
