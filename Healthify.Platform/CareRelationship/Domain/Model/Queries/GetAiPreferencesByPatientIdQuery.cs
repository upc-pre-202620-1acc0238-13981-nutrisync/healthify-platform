namespace Healthify.Platform.CareRelationship.Domain.Model.Queries;

/// <summary>IA-1. The AI preferences of a patient, read together with their consent to AI processing.</summary>
public record GetAiPreferencesByPatientIdQuery(int PatientId);
