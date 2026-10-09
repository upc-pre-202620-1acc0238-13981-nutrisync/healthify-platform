namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>Read model: Plan Version History. Superseded versions are kept and listed.</summary>
public record GetPlansByPatientIdQuery(int PatientId);
