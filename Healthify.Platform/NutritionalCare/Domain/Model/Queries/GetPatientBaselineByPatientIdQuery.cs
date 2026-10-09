namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>NC-1 - The baseline of a patient. Its absence is what PAC-0 shows as empty.</summary>
public record GetPatientBaselineByPatientIdQuery(int PatientId);
