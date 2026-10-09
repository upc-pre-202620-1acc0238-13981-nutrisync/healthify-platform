namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-2 (P2) - Discard a consultation in progress. What it already saved stays as history; its pending
///     diagnosis and its unpublished draft are discarded, and the active diagnosis and version are untouched.
/// </summary>
public record AbandonConsultationCommand(int ConsultationId, int PractitionerId);
