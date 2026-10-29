namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Step 4 of the guided consultation ("Publish and close consultation"): publish the prescribed draft
///     of step 3, replacing the version in force, and close the consultation.
/// </summary>
/// <param name="ConsultationId">The consultation in progress.</param>
/// <param name="PractitionerId">The practitioner leading it, from the token.</param>
/// <param name="Restrictions"><c>DietaryRestriction</c> codes only.</param>
/// <param name="Guidelines">Catalog codes; any other text becomes a custom guideline.</param>
/// <param name="CustomGuidelines">The "other guideline" texts: 3 to 140 characters, at most 5 per version.</param>
/// <param name="IdempotencyKey">
///     The <c>Idempotency-Key</c> header: repeating a completed publication with the same key returns the
///     same result and publishes nothing again.
/// </param>
/// <param name="PatientMessage">Optional message for the patient with this version, at most 500 characters.</param>
public record PublishFromConsultationCommand(
    int ConsultationId,
    int PractitionerId,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> CustomGuidelines,
    string? IdempotencyKey = null,
    string? PatientMessage = null);
