using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Services;

namespace Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;

/// <summary>
///     One entry of the local food catalog: a name and its nutrients per 100 grams.
/// </summary>
/// <remarks>
///     This is a Generic context and it is deliberately thin. Everything interesting about it happens
///     at its edge: nothing from an external provider reaches this class until it has been translated,
///     and what the provider called the record survives only as an opaque digest.
///     The composite value object is stored as flat columns and rebuilt by the computed property
///     below. An owned type would need its own key mapped onto a typed primary key, which EF Core
///     cannot reconcile, so this follows the pattern the platform already uses.
/// </remarks>
public partial class ReferenceFood
{
    /// <summary>Required by EF Core.</summary>
    protected ReferenceFood()
    {
    }

    /// <summary>Subflow 6.2 - Cache Food Locally. The translated record enters the catalog.</summary>
    public ReferenceFood(LocalName localName, NutrientsPer100g nutrients, SourceHash sourceHash)
    {
        LocalNameText = localName.Value;
        StoreNutrients(nutrients);
        SourceHash = sourceHash;
        IsLocalOverride = false;
        Source = new FoodSource(FoodSource.Imported);
        IsVerified = true;
    }

    /// <summary>Subflow 6.4 - Create Local Override.</summary>
    /// <remarks>
    ///     Business rule: Item Flagged As Local Override (Food Catalog, Subflow 6.4). The flag is set
    ///     here and there is no method that clears it: an item created by a practitioner because the
    ///     external catalog does not carry it stays marked for as long as it exists, so that a later
    ///     import can never quietly overwrite local knowledge with a generic upstream record.
    /// </remarks>
    public ReferenceFood(CreateLocalOverrideCommand command)
    {
        var localName = new LocalName(command.LocalName);

        LocalNameText = localName.Value;
        StoreNutrients(new NutrientsPer100g(command.EnergyKcalPer100g, command.ProteinGPer100g,
            command.CarbGPer100g, command.FatGPer100g));
        SourceHash = SourceHash.ForLocalOverride(localName.Value);
        IsLocalOverride = true;
        Source = new FoodSource(FoodSource.LocalOverride);
        IsVerified = true;
    }

    /// <summary>
    ///     IN-7 - Create AI Estimated Food. A dish that neither the local catalog nor the external providers carry
    ///     enters the catalog with the nutrients the AI estimated for it.
    /// </summary>
    /// <remarks>
    ///     Business rule: AI Estimated Nutrients Coherent (IN-7), asserted here by <see cref="NutrientCoherence" />:
    ///     an estimate that could not describe real food never becomes a food.
    ///     DECISIÓN IN-7: verified automatically (<see cref="IsVerified" /> true, <see cref="VerifiedBy" /> null), as
    ///     the change of design states. The source, the verification and the generation are stored for traceability
    ///     and never shown: for the patient and the practitioner it is a food like any other.
    /// </remarks>
    /// <param name="localName">The dish as the catalog will show it.</param>
    /// <param name="nutrients">The nutrients the AI estimated, per 100 g.</param>
    /// <param name="aiGenerationId">The generation that estimated them.</param>
    /// <param name="tolerance">How far energy and macronutrients may disagree; <see cref="NutrientTolerance.Default" /> when null.</param>
    /// <exception cref="ArgumentException">The nutrients are incoherent, or there is no generation to trace.</exception>
    public static ReferenceFood AiEstimated(LocalName localName, NutrientsPer100g nutrients, long aiGenerationId,
        NutrientTolerance? tolerance = null)
    {
        if (aiGenerationId <= 0)
            throw new ArgumentException("An AI estimated food is traced to its generation.", nameof(aiGenerationId));
        var violations = NutrientCoherence.Violations(nutrients.EnergyKcal, nutrients.ProteinG, nutrients.CarbG,
            nutrients.FatG, tolerance);
        if (violations.Count > 0)
            throw new ArgumentException($"Incoherent AI estimated nutrients: {string.Join("; ", violations)}.",
                nameof(nutrients));

        var food = new ReferenceFood(localName, nutrients, SourceHash.ForAiEstimated(localName.Value))
        {
            Source = new FoodSource(FoodSource.AiEstimated),
            IsVerified = true,
            VerifiedBy = null,
            AiGenerationId = aiGenerationId
        };
        return food;
    }

    public ReferenceFoodId Id { get; private set; } = null!;

    /// <summary>
    ///     Persisted projection of the <see cref="LocalName" /> value object. Kept as a plain string
    ///     because search matches on it, and EF Core cannot translate a member access on a converted
    ///     type into SQL.
    /// </summary>
    public string LocalNameText { get; private set; } = null!;

    // Persisted projection of the NutrientsPer100g value object.
    public decimal EnergyKcalPer100g { get; private set; }
    public decimal ProteinGPer100g { get; private set; }
    public decimal CarbGPer100g { get; private set; }
    public decimal FatGPer100g { get; private set; }

    /// <summary>
    ///     Business rule: Source Hash Stored For Upstream Changes (Subflow 6.1). Stored, compared,
    ///     and never exposed in a resource.
    /// </summary>
    public SourceHash SourceHash { get; private set; } = null!;

    /// <summary>Business rule: Item Flagged As Local Override (Subflow 6.4).</summary>
    public bool IsLocalOverride { get; private set; }

    /// <summary>IN-7. Where the food came from. Stored for traceability, never exposed.</summary>
    public FoodSource Source { get; private set; } = new(FoodSource.Imported);

    /// <summary>IN-7. Whether the nutrients are verified. True for every food; never exposed.</summary>
    public bool IsVerified { get; private set; } = true;

    /// <summary>IN-7. Who verified the food, when a person did; null when it was verified automatically. Never exposed.</summary>
    public int? VerifiedBy { get; private set; }

    /// <summary>IN-7. The <c>ai_generations</c> row that estimated an <c>AiEstimated</c> food; null otherwise.</summary>
    public long? AiGenerationId { get; private set; }

    /// <summary>
    ///     IN-7. A food an import never overwrites: a local override (Subflow 6.4) or an AI-estimated food, which has
    ///     no upstream record to follow.
    /// </summary>
    public bool IsProtectedFromImport => IsLocalOverride || Source.IsAiEstimated;

    /// <summary>Rebuilt from the stored columns.</summary>
    public LocalName LocalName => new(LocalNameText);

    /// <summary>Rebuilt from the stored columns.</summary>
    public NutrientsPer100g NutrientsPer100g =>
        new(EnergyKcalPer100g, ProteinGPer100g, CarbGPer100g, FatGPer100g);

    /// <summary>
    ///     Subflow 6.1 - the upstream record changed, so the cached copy follows it.
    /// </summary>
    /// <remarks>
    ///     Business rule: Item Flagged As Local Override (Subflow 6.4) read from the other side. A
    ///     local override exists precisely because the external catalog was wrong or silent about
    ///     this food, so an import never touches one.
    /// </remarks>
    public void RefreshFromUpstream(LocalName localName, NutrientsPer100g nutrients)
    {
        if (IsLocalOverride)
            throw new InvalidOperationException("A local override is never overwritten by an import.");
        // IN-7: nor an AI-estimated food, which has no upstream record to follow.
        if (Source.IsAiEstimated)
            throw new InvalidOperationException("An AI estimated food is never overwritten by an import.");

        LocalNameText = localName.Value;
        StoreNutrients(nutrients);
    }

    private void StoreNutrients(NutrientsPer100g nutrients)
    {
        EnergyKcalPer100g = nutrients.EnergyKcal;
        ProteinGPer100g = nutrients.ProteinG;
        CarbGPer100g = nutrients.CarbG;
        FatGPer100g = nutrients.FatG;
    }
}
