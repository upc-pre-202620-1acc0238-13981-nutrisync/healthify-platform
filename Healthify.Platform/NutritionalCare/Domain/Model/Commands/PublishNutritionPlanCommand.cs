namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Subflow 3.5 - Publish Nutrition Plan. Menus and food equivalences live as free text inside
///     the guidelines: modelling them would be a different project.
/// </summary>
/// <param name="PlanId">The prescribed draft version.</param>
/// <param name="PractitionerId">The prescribing practitioner, from the token.</param>
/// <param name="Guidelines">
///     Catalog codes. Any other text becomes a custom guideline.
/// </param>
/// <param name="Restrictions">NC-6. <c>DietaryRestriction</c> codes only.</param>
/// <param name="CustomGuidelines">NC-6. "Otra indicación": 3 to 140 characters, at most 5 per version.</param>
/// <param name="PatientMessage">NC-9. Optional message for the patient with this version, at most 500 characters.</param>
/// <remarks>Deprecated by NC-6: free text in <paramref name="Guidelines" />; new clients send codes and custom texts apart.</remarks>
public record PublishNutritionPlanCommand(
    int PlanId,
    int PractitionerId,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<string>? CustomGuidelines = null,
    string? PatientMessage = null);
