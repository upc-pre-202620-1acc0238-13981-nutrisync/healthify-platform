namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Subflow 3.6 - Adjust Nutrition Plan, between visits and without an appointment. Creates the
///     next version; the previous one is superseded and never deleted.
/// </summary>
/// <param name="PlanId">The published version being adjusted.</param>
/// <param name="PractitionerId">The prescribing practitioner, from the token.</param>
/// <param name="EnergyKcal">New energy target.</param>
/// <param name="ProteinG">New protein target.</param>
/// <param name="CarbG">New carbohydrate target.</param>
/// <param name="FatG">New fat target.</param>
/// <param name="Guidelines">Catalog codes. Any other text becomes a custom guideline.</param>
/// <param name="Restrictions">
///     NC-6. <c>DietaryRestriction</c> codes only. Legacy restrictions of the current version that are not
///     mapped to a code here are left out of the new version and recorded on it.
/// </param>
/// <param name="ChangeReason">Why this version exists.</param>
/// <param name="CustomGuidelines">NC-6. "Otra indicación": 3 to 140 characters, at most 5 per version.</param>
/// <param name="PatientMessage">NC-9. Optional message for the patient with this version, at most 500 characters.</param>
/// <remarks>Deprecated by NC-6: free text in <paramref name="Guidelines" />; new clients send codes and custom texts apart.</remarks>
public record AdjustNutritionPlanCommand(
    int PlanId,
    int PractitionerId,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    string ChangeReason,
    IReadOnlyList<string>? CustomGuidelines = null,
    string? PatientMessage = null);
