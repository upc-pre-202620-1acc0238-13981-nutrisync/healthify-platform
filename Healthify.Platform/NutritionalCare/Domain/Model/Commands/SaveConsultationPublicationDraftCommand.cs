namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-2 - Keep what the practitioner chose in EV-5 before publishing ("Lo que escribiste no se perdió").
/// </summary>
/// <param name="ConsultationId">The consultation in progress, already at step 4.</param>
/// <param name="PractitionerId">The practitioner leading it, from the token.</param>
/// <param name="Restrictions">NC-6. <c>DietaryRestriction</c> codes.</param>
/// <param name="Guidelines">NC-6. Guideline catalog codes.</param>
/// <param name="CustomGuidelines">NC-6. "Otra indicación": 3 to 140 characters, at most 5.</param>
/// <param name="PatientMessage">NC-9. The message for the patient being written, at most 500 characters.</param>
public record SaveConsultationPublicationDraftCommand(
    int ConsultationId,
    int PractitionerId,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> CustomGuidelines,
    string? PatientMessage = null);
