namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

/// <summary>
///     IA-3. What is left of today's targets: the published contract (<c>ActiveTargetsCache</c>) minus what the
///     confirmed entries of the day add up to. Computed by this server, never by the AI.
/// </summary>
/// <remarks>
///     A nutrient already covered is zero, never negative: the ideas are about what still fits, and a negative
///     remainder would read as a debt. Nothing here qualifies the day.
/// </remarks>
public sealed record RemainingTargets
{
    /// <summary>
    ///     Business rule: Ideas Need Room For A Meal (IA-3). At or below this energy no idea is generated and the
    ///     patient reads a kind empty state («Ya cubriste tu energía de hoy»).
    /// </summary>
    public const decimal MinimumForAMealKcal = 150m;

    /// <summary>IA-3. Width of the energy buckets the cache is keyed by.</summary>
    public const decimal EnergyBucketKcal = 50m;

    public RemainingTargets(decimal energyKcal, decimal proteinG, decimal carbG, decimal fatG)
    {
        EnergyKcal = decimal.Round(Math.Max(0m, energyKcal), 0);
        ProteinG = decimal.Round(Math.Max(0m, proteinG), 1);
        CarbG = decimal.Round(Math.Max(0m, carbG), 1);
        FatG = decimal.Round(Math.Max(0m, fatG), 1);
    }

    public decimal EnergyKcal { get; }
    public decimal ProteinG { get; }
    public decimal CarbG { get; }
    public decimal FatG { get; }

    /// <summary>Business rule: Ideas Need Room For A Meal (IA-3).</summary>
    public bool LeavesRoomForAMeal => EnergyKcal > MinimumForAMealKcal;

    /// <summary>IA-3. The 50 kcal bucket of the remaining energy: 590 and 570 share one, 540 does not.</summary>
    public int EnergyBucket => (int)Math.Floor(EnergyKcal / EnergyBucketKcal);

    /// <summary>The targets minus what was eaten.</summary>
    public static RemainingTargets Of(decimal targetEnergyKcal, decimal targetProteinG, decimal targetCarbG,
        decimal targetFatG, decimal eatenEnergyKcal, decimal eatenProteinG, decimal eatenCarbG, decimal eatenFatG)
    {
        return new RemainingTargets(targetEnergyKcal - eatenEnergyKcal, targetProteinG - eatenProteinG,
            targetCarbG - eatenCarbG, targetFatG - eatenFatG);
    }
}

/// <summary>IA-3. One ingredient of a meal idea, in grams, resolved to the catalog when the catalog has it (FC-2).</summary>
/// <param name="Name">The name the idea gives it ("Pechuga de pollo").</param>
/// <param name="Grams">The quantity.</param>
/// <param name="ReferenceFoodId">
///     The catalog entry it resolved to, or null: a minor ingredient (a condiment) the catalog does not have, which
///     the app leaves out of the log or sends to PT9 (IN-6). An idea whose unresolved ingredients bring more than 15 %
///     of its energy never reaches the patient.
/// </param>
/// <param name="CatalogName">The local name of that entry, or null.</param>
public sealed record MealIdeaIngredient(string Name, decimal Grams, int? ReferenceFoodId, string? CatalogName)
{
    public bool IsResolved => ReferenceFoodId is not null;
}

/// <summary>
///     IA-3. An idea of a meal that fits what is left today and respects the plan. A suggestion the patient may log
///     (IN-6), never a prescription: the ideas do not replace the practitioner's guidelines.
/// </summary>
/// <param name="MealIdeaId">Stable within its generation; sent back as the origin when the idea is logged.</param>
/// <param name="Name">"Pollo al horno con camote y ensalada".</param>
/// <param name="EnergyKcal">Energy of the idea: from the catalog when it disagrees with the model by more than 15 %.</param>
/// <param name="ProteinG">Protein of the idea, same source as the energy.</param>
/// <param name="CarbG">Carbohydrate of the idea, same source as the energy.</param>
/// <param name="FatG">Fat of the idea, same source as the energy.</param>
/// <param name="Ingredients">The ingredients in grams.</param>
/// <param name="Why">«Por qué esta idea», in an inviting tone.</param>
/// <param name="NutrientsFromCatalog">True when the figures are the catalog's rather than the model's.</param>
public sealed record MealIdea(
    string MealIdeaId,
    string Name,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<MealIdeaIngredient> Ingredients,
    string Why,
    bool NutrientsFromCatalog);
