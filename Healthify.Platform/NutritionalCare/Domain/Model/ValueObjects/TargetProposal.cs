namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     What the arithmetic produced, before the practitioner signed anything. A proposal is not a
///     prescription.
/// </summary>
public sealed record TargetProposal
{
    public TargetProposal(decimal energyKcal, decimal proteinG, decimal carbG, decimal fatG)
    {
        if (energyKcal <= 0m) throw new ArgumentException("Target energy must be positive.", nameof(energyKcal));
        if (proteinG < 0m) throw new ArgumentException("Protein cannot be negative.", nameof(proteinG));
        if (carbG < 0m) throw new ArgumentException("Carbohydrate cannot be negative.", nameof(carbG));
        if (fatG < 0m) throw new ArgumentException("Fat cannot be negative.", nameof(fatG));

        EnergyKcal = decimal.Round(energyKcal, 2);
        ProteinG = decimal.Round(proteinG, 2);
        CarbG = decimal.Round(carbG, 2);
        FatG = decimal.Round(fatG, 2);
    }

    public decimal EnergyKcal { get; }
    public decimal ProteinG { get; }
    public decimal CarbG { get; }
    public decimal FatG { get; }
}
