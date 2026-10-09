using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Services;

/// <summary>Energy and macronutrients of a meal idea or of a part of it.</summary>
public sealed record MealNutrients(decimal EnergyKcal, decimal ProteinG, decimal CarbG, decimal FatG)
{
    public static MealNutrients Zero { get; } = new(0m, 0m, 0m, 0m);

    public MealNutrients Plus(MealNutrients other)
    {
        return new MealNutrients(EnergyKcal + other.EnergyKcal, ProteinG + other.ProteinG, CarbG + other.CarbG,
            FatG + other.FatG);
    }

    /// <summary>The nutrients of <paramref name="grams" /> of a food given per 100 g.</summary>
    public static MealNutrients OfPortion(decimal grams, decimal energyKcalPer100g, decimal proteinGPer100g,
        decimal carbGPer100g, decimal fatGPer100g)
    {
        var factor = grams / 100m;
        return new MealNutrients(energyKcalPer100g * factor, proteinGPer100g * factor, carbGPer100g * factor,
            fatGPer100g * factor);
    }

    public MealNutrients Rounded()
    {
        return new MealNutrients(decimal.Round(EnergyKcal, 0), decimal.Round(ProteinG, 1), decimal.Round(CarbG, 1),
            decimal.Round(FatG, 1));
    }
}

/// <summary>
///     IA-3. The hard validation of a meal idea, the same before and after the catalog recalculates it: what makes
///     an idea one the patient may see.
/// </summary>
public static class MealIdeaRules
{
    /// <summary>IA-3: with fewer valid ideas than this, the generation is retried once and then rejected.</summary>
    public const int MinimumIdeas = 2;

    /// <summary>PT14.4 shows three ideas.</summary>
    public const int MaximumIdeasShown = 3;

    /// <summary>IA-3: above this relative difference with the model, the catalog figures are used.</summary>
    public const decimal CatalogToleranceRatio = 0.15m;

    /// <summary>
    ///     Why the idea cannot be shown; empty when it can.
    /// </summary>
    /// <param name="name">The name of the idea.</param>
    /// <param name="energyKcal">Its energy (the model's, or the catalog's once recalculated).</param>
    /// <param name="ingredientTexts">Each ingredient as named by the idea and, when resolved, by the catalog.</param>
    /// <param name="why">«Por qué esta idea».</param>
    /// <param name="remaining">What is left today.</param>
    /// <param name="restrictions">The restriction codes of the plan.</param>
    /// <param name="lexicon">The ingredient map and the forbidden words.</param>
    public static IReadOnlyList<string> Screen(string name, decimal energyKcal, IEnumerable<string> ingredientTexts,
        string why, RemainingTargets remaining, IReadOnlyCollection<string> restrictions, IRestrictionLexicon lexicon)
    {
        var reasons = new List<string>();

        // Business rule: Idea Fits What Is Left (IA-3). energyKcal ≤ remainingKcal.
        if (energyKcal <= 0m || energyKcal > remaining.EnergyKcal)
            reasons.Add($"'{name}': {energyKcal:0} kcal does not fit the {remaining.EnergyKcal:0} kcal left.");

        // Business rule: Idea Respects The Restrictions (IA-3, NC-6). The name counts too: "Ceviche de langostinos".
        foreach (var text in ingredientTexts.Prepend(name))
        foreach (var restriction in lexicon.ViolatedRestrictions(text, restrictions))
            reasons.Add($"'{name}': '{text}' breaks {restriction}.");

        // Business rules: Invitation Tone Never Accusation and Diagnosis Never Leaves The Context.
        foreach (var text in new[] { name, why })
        foreach (var term in lexicon.ForbiddenTermsIn(text))
            reasons.Add($"'{name}': a text for the patient carries '{term}'.");

        return reasons;
    }

    /// <summary>
    ///     Business rule: Unresolved Ingredients Stay Minor (IA-3). The ingredients the catalog cannot resolve (FC-2)
    ///     may bring at most this share of the energy of the idea; above it, «cabe en lo que te queda» cannot be
    ///     verified and the idea is discarded.
    /// </summary>
    public const decimal UnresolvedEnergyShareLimit = 0.15m;

    /// <summary>
    ///     IA-3. The share of the idea's energy brought by its unresolved ingredients: what the model declares for the
    ///     whole idea minus what the catalog accounts for the resolved ones, over the declared energy. Zero when the
    ///     catalog already accounts for all of it; one when the model declares no energy.
    /// </summary>
    /// <remarks>
    ///     DECISIÓN IA-3: the model gives no energy per ingredient, so the unresolved ingredients of an idea are
    ///     measured together, by the part of the declared energy the catalog cannot explain. A pinch of salt and a
    ///     clove of garlic are a few kcal; 150 g of an unknown grain is not.
    /// </remarks>
    public static decimal UnresolvedEnergyShare(decimal declaredEnergyKcal, decimal resolvedCatalogEnergyKcal)
    {
        if (declaredEnergyKcal <= 0m) return 1m;
        return Math.Max(0m, declaredEnergyKcal - resolvedCatalogEnergyKcal) / declaredEnergyKcal;
    }

    /// <summary>Business rule: Unresolved Ingredients Stay Minor (IA-3).</summary>
    public static bool UnresolvedIngredientsAreMinor(decimal declaredEnergyKcal, decimal resolvedCatalogEnergyKcal)
    {
        return UnresolvedEnergyShare(declaredEnergyKcal, resolvedCatalogEnergyKcal) <= UnresolvedEnergyShareLimit;
    }

    /// <summary>
    ///     IA-3. The figures shown for an idea. When every ingredient is in the catalog, the catalog sum replaces the
    ///     model's figures if their energies differ by more than 15 %. When some minor ingredient is not (see
    ///     <see cref="UnresolvedIngredientsAreMinor" />, checked before), the macros stay the model's and the energy is
    ///     the larger of the two, so «cabe en lo que te queda» is never checked against less than what the catalog
    ///     already accounts for.
    /// </summary>
    /// <returns>The figures, rounded, and whether they are the catalog's.</returns>
    public static (MealNutrients Nutrients, bool FromCatalog) Reconcile(MealNutrients declared, MealNutrients catalog,
        bool everyIngredientResolved)
    {
        if (everyIngredientResolved)
        {
            var differs = declared.EnergyKcal <= 0m ||
                          Math.Abs(catalog.EnergyKcal - declared.EnergyKcal) >
                          CatalogToleranceRatio * declared.EnergyKcal;
            return differs ? (catalog.Rounded(), true) : (declared.Rounded(), false);
        }

        // DECISIÓN IA-3: with a minor unresolved ingredient there is no full catalog figure to compare with.
        return (new MealNutrients(Math.Max(declared.EnergyKcal, catalog.EnergyKcal), declared.ProteinG,
            declared.CarbG, declared.FatG).Rounded(), false);
    }
}
