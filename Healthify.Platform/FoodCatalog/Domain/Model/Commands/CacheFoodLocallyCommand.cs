namespace Healthify.Platform.FoodCatalog.Domain.Model.Commands;

/// <summary>
///     Subflow 6.2 - Cache Food Locally. Issued by the policy that reacts to Reference Food
///     Translated, never by an endpoint: nothing enters the catalog that has not been translated
///     first.
/// </summary>
/// <remarks>
///     It carries the translated payload as primitives, exactly as the event delivers it. There is no
///     external identifier among them, by construction.
/// </remarks>
public record CacheFoodLocallyCommand(
    string LocalName,
    decimal EnergyKcalPer100g,
    decimal ProteinGPer100g,
    decimal CarbGPer100g,
    decimal FatGPer100g,
    string SourceHash);
