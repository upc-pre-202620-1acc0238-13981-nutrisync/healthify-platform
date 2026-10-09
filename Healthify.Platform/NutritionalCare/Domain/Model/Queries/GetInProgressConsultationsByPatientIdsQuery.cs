namespace Healthify.Platform.NutritionalCare.Domain.Model.Queries;

/// <summary>RM-1 - The consultations in progress of several patients in one read (PAC-1.C from the roster).</summary>
public record GetInProgressConsultationsByPatientIdsQuery(IReadOnlyCollection<int> PatientIds);
