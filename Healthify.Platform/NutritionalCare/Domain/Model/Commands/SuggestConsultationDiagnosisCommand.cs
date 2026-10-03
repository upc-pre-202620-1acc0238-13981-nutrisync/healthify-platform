namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     IA-6 - Suggest the diagnosis of step 2 (EV-3 "Sugerencia de IA"). A command because it may run an AI generation,
///     which is audited; it never changes the consultation.
/// </summary>
/// <param name="ConsultationId">The consultation.</param>
/// <param name="PractitionerId">From the token: the practitioner leading it.</param>
public record SuggestConsultationDiagnosisCommand(int ConsultationId, int PractitionerId);
