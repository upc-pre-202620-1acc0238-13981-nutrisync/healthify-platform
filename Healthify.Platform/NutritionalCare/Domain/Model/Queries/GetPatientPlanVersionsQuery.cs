namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>
///     The plan versions, for the patient: the published versions only, with what each changed.
/// </summary>
public record GetPatientPlanVersionsQuery(int PatientId);
