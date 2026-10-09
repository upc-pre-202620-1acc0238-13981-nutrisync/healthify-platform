namespace Healthify.Platform.FoodCatalog.Domain.Model.Commands;

/// <summary>
///     Subflow 6.4 - Create Local Override. The practitioner adds a food the external catalog does
///     not carry, which for Peruvian prepared dishes is most of them.
/// </summary>
public record CreateLocalOverrideCommand(
    int PractitionerId,
    string LocalName,
    decimal EnergyKcalPer100g,
    decimal ProteinGPer100g,
    decimal CarbGPer100g,
    decimal FatGPer100g);
