namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-2 (minimal, created with NC-3) - Start a guided consultation. Needs a baseline and an active
///     care link; only one consultation in progress per patient.
/// </summary>
public record StartConsultationCommand(int PatientId, int PractitionerId, int? ScheduledFollowUpId = null);
