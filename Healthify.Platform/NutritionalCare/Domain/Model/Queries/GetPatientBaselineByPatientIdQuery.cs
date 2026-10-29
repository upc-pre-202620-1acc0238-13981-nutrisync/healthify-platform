namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>The baseline of a patient. Its absence is what the patient summary shows as empty.</summary>
public record GetPatientBaselineByPatientIdQuery(int PatientId);
