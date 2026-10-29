namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>The consultations in progress of several patients in one read (for the roster).</summary>
public record GetInProgressConsultationsByPatientIdsQuery(IReadOnlyCollection<int> PatientIds);
