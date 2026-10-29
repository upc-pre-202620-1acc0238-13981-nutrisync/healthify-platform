namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Keep what the practitioner chose in step 4 before publishing, so nothing written is lost.
/// </summary>
/// <param name="ConsultationId">The consultation in progress, already at step 4.</param>
/// <param name="PractitionerId">The practitioner leading it, from the token.</param>
/// <param name="Restrictions"><c>DietaryRestriction</c> codes.</param>
/// <param name="Guidelines">Guideline catalog codes.</param>
/// <param name="CustomGuidelines">The "other guideline" texts: 3 to 140 characters, at most 5.</param>
/// <param name="PatientMessage">The message for the patient being written, at most 500 characters.</param>
public record SaveConsultationPublicationDraftCommand(
    int ConsultationId,
    int PractitionerId,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> CustomGuidelines,
    string? PatientMessage = null);
