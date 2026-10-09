using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.FoodCatalog.Domain.Model.Events;

/// <summary>
///     Subflow 6.1. One upstream record crossed the anti-corruption layer and became platform
///     vocabulary. Stays inside Food Catalog: the policy of Subflow 6.2 is what listens to it.
/// </summary>
/// <remarks>
///     The payload is what survived the translation. There is no external identifier in it, only the
///     opaque digest that lets a later import recognise the same upstream record.
/// </remarks>
public record ReferenceFoodTranslated(
    string LocalName,
    decimal EnergyKcalPer100g,
    decimal ProteinGPer100g,
    decimal CarbGPer100g,
    decimal FatGPer100g,
    string SourceHash) : DomainEventBase;
