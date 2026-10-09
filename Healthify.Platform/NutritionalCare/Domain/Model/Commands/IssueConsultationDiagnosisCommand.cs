namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-4 - Step 2 of the consultation (EV-3 "Continuar a metas"). Issues the diagnosis on the
///     assessment of step 1 and supersedes the active one.
/// </summary>
/// <param name="ConsultationId">The consultation in progress.</param>
/// <param name="PractitionerId">The practitioner leading it, from the token.</param>
/// <param name="Code">A <c>DiagnosisCode</c> of the closed list.</param>
/// <param name="Source">AiSuggestionAccepted ("Usar sugerencia") or PractitionerSelected ("Elegir otro").</param>
/// <param name="AiGenerationId">Required with AiSuggestionAccepted.</param>
/// <param name="Rationale">
///     Required with AiSuggestionAccepted (the rationale the AI proposed). Optional with
///     PractitionerSelected: a deterministic one is written from the measurement.
/// </param>
public record IssueConsultationDiagnosisCommand(
    int ConsultationId,
    int PractitionerId,
    string Code,
    string Source,
    long? AiGenerationId,
    string? Rationale);
