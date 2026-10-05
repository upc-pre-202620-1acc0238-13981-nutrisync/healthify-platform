namespace Healthify.Platform.FoodCatalog.Interfaces.REST.Resources;

// ---------------------------------------------------------------------------------------------
// Request resources
// ---------------------------------------------------------------------------------------------

/// <summary>Payload of Subflow 6.4 - Create Local Override.</summary>
public record CreateLocalOverrideResource(
    string LocalName,
    decimal EnergyKcalPer100g,
    decimal ProteinGPer100g,
    decimal CarbGPer100g,
    decimal FatGPer100g)
{
    /// <summary>The name this food carries in the platform. Required.</summary>
    public string LocalName { get; init; } = LocalName;

    /// <summary>Energy per 100 grams, in kilocalories. Required.</summary>
    public decimal EnergyKcalPer100g { get; init; } = EnergyKcalPer100g;

    /// <summary>Protein per 100 grams, in grams. Required.</summary>
    public decimal ProteinGPer100g { get; init; } = ProteinGPer100g;

    /// <summary>Carbohydrate per 100 grams, in grams. Required.</summary>
    public decimal CarbGPer100g { get; init; } = CarbGPer100g;

    /// <summary>Fat per 100 grams, in grams. Required.</summary>
    public decimal FatGPer100g { get; init; } = FatGPer100g;
}

/// <summary>Payload of Subflow 6.1 - Import Catalog Snapshot, requested on demand.</summary>
public record ImportCatalogSnapshotResource(string Term, int Max)
{
    /// <summary>What to ask the external providers for. Required.</summary>
    public string Term { get; init; } = Term;

    /// <summary>Upper bound on the records taken from each provider. Defaults to 25 when not positive.</summary>
    public int Max { get; init; } = Max;
}

// ---------------------------------------------------------------------------------------------
// Response resources
// ---------------------------------------------------------------------------------------------

/// <summary>
///     One entry of the local catalog. Read models Food Results List and Local Food Catalog.
/// </summary>
/// <remarks>
///     There is no source hash on this resource and there never will be. The fingerprint of the
///     upstream record exists so that an import can detect a change; exposing it would turn it back
///     into the external identifier it was designed to replace.
/// </remarks>
public record ReferenceFoodResource(
    int ReferenceFoodId,
    string LocalName,
    decimal EnergyKcalPer100g,
    decimal ProteinGPer100g,
    decimal CarbGPer100g,
    decimal FatGPer100g,
    bool IsLocalOverride)
{
    /// <summary>Identifier of the catalog entry.</summary>
    public int ReferenceFoodId { get; init; } = ReferenceFoodId;

    /// <summary>The name this food carries in the platform, never the provider label.</summary>
    public string LocalName { get; init; } = LocalName;

    /// <summary>Energy per 100 grams, in kilocalories.</summary>
    public decimal EnergyKcalPer100g { get; init; } = EnergyKcalPer100g;

    /// <summary>Protein per 100 grams, in grams.</summary>
    public decimal ProteinGPer100g { get; init; } = ProteinGPer100g;

    /// <summary>Carbohydrate per 100 grams, in grams.</summary>
    public decimal CarbGPer100g { get; init; } = CarbGPer100g;

    /// <summary>Fat per 100 grams, in grams.</summary>
    public decimal FatGPer100g { get; init; } = FatGPer100g;

    /// <summary>True when a practitioner added this entry because the external catalog lacked it.</summary>
    public bool IsLocalOverride { get; init; } = IsLocalOverride;
}

/// <summary>What one run of Import Catalog Snapshot translated and announced.</summary>
public record CatalogImportSummaryResource(
    string Term,
    int ProvidersConsulted,
    int TranslatedCount,
    int FailedCount)
{
    /// <summary>The term the providers were asked for.</summary>
    public string Term { get; init; } = Term;

    /// <summary>How many external providers were consulted.</summary>
    public int ProvidersConsulted { get; init; } = ProvidersConsulted;

    /// <summary>Records that crossed the anti-corruption layer and were announced for caching.</summary>
    public int TranslatedCount { get; init; } = TranslatedCount;

    /// <summary>Records dropped because they could not be expressed in this vocabulary.</summary>
    public int FailedCount { get; init; } = FailedCount;
}
