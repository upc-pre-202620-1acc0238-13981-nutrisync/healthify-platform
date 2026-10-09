namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>
///     NC-8 - PT4.1 "Versiones del plan", for the patient: the published versions only, with what each changed.
/// </summary>
public record GetPatientPlanVersionsQuery(int PatientId);
