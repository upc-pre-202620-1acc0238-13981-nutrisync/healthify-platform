namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>RM-1 - The active version of several patients in one read (roster "sin plan").</summary>
public record GetActivePlansByPatientIdsQuery(IReadOnlyCollection<int> PatientIds);
