namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>The baselines of several patients in one read (roster "Nueva").</summary>
public record GetPatientBaselinesByPatientIdsQuery(IReadOnlyCollection<int> PatientIds);
