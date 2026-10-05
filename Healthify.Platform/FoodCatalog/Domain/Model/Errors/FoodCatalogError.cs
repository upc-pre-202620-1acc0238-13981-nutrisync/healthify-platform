namespace Healthify.Platform.FoodCatalog.Domain.Model.Errors;

/// <summary>Every failure this bounded context can report. One value per business rule it enforces.</summary>
public enum FoodCatalogError
{
    /// <summary>Rule: the import could not reach any external provider (Subflow 6.1).</summary>
    ExternalCatalogUnavailable,

    /// <summary>Rule: Taxonomy Translation Mandatory (Subflow 6.1).</summary>
    TaxonomyTranslationFailed,

    /// <summary>Rule: No External Id Enters The Domain (Subflow 6.1).</summary>
    ExternalIdNotAllowed,

    /// <summary>Rule: Source Hash Stored For Upstream Changes (Subflow 6.1).</summary>
    SourceHashRequired,

    ReferenceFoodNotFound,

    /// <summary>Rule: Practitioner Only (Subflow 6.4).</summary>
    PractitionerOnly,

    /// <summary>Rule: Local Name And Nutrients Required (Subflow 6.4).</summary>
    LocalNameAndNutrientsRequired,

    /// <summary>Rule: one local override per local name (Subflow 6.4).</summary>
    DuplicatedLocalOverride,

    /// <summary>
    ///     Rule: AI Estimated Nutrients Coherent (IN-7). kcal 0–900 per 100 g, each macronutrient 0–100 g and together
    ///     at most 100 g, and the energy within 15 % of 4P + 4C + 9G.
    /// </summary>
    InconsistentNutrients,

    /// <summary>IN-7. An AI-estimated food is traced to the generation that estimated it.</summary>
    AiGenerationRequired,

    UnexpectedError
}
