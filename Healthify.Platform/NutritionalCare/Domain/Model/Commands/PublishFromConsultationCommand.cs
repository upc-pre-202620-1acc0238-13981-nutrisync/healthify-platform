namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-7 - Step 4 of the guided consultation (EV-5 "Publicar y cerrar consulta"): publish the prescribed draft
///     of step 3, replacing the version in force, and close the consultation.
/// </summary>
/// <param name="ConsultationId">The consultation in progress.</param>
/// <param name="PractitionerId">The practitioner leading it, from the token.</param>
/// <param name="Restrictions">NC-6. <c>DietaryRestriction</c> codes only.</param>
/// <param name="Guidelines">NC-6. Catalog codes; any other text becomes a custom guideline.</param>
/// <param name="CustomGuidelines">NC-6. "Otra indicación": 3 to 140 characters, at most 5 per version.</param>
/// <param name="IdempotencyKey">
///     NC-2. The <c>Idempotency-Key</c> header: repeating a completed publication with the same key returns the
///     same result and publishes nothing again.
/// </param>
/// <param name="PatientMessage">NC-9. Optional message for the patient with this version, at most 500 characters.</param>
public record PublishFromConsultationCommand(
    int ConsultationId,
    int PractitionerId,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> CustomGuidelines,
    string? IdempotencyKey = null,
    string? PatientMessage = null);
