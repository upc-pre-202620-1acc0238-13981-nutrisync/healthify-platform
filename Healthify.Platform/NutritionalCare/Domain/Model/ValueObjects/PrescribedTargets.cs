namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     What the practitioner actually signed, and whether it matches what was proposed. Every target
///     in the platform is traceable to a person through this value object.
/// </summary>
public sealed record PrescribedTargets
{
    public PrescribedTargets(
        decimal energyKcal,
        decimal proteinG,
        decimal carbG,
        decimal fatG,
        PrescriptionOutcome outcome,
        OverrideReason? overrideReason = null)
    {
        if (energyKcal <= 0m) throw new ArgumentException("Target energy must be positive.", nameof(energyKcal));
        if (proteinG < 0m) throw new ArgumentException("Protein cannot be negative.", nameof(proteinG));
        if (carbG < 0m) throw new ArgumentException("Carbohydrate cannot be negative.", nameof(carbG));
        if (fatG < 0m) throw new ArgumentException("Fat cannot be negative.", nameof(fatG));

        // Business rule: Override Requires Reason (Nutritional Care, Subflow 3.4)
        if (outcome.IsOverridden && overrideReason is null)
            throw new ArgumentException("Overriding the proposed targets requires a reason.",
                nameof(overrideReason));

        EnergyKcal = decimal.Round(energyKcal, 2);
        ProteinG = decimal.Round(proteinG, 2);
        CarbG = decimal.Round(carbG, 2);
        FatG = decimal.Round(fatG, 2);
        Outcome = outcome;
        OverrideReason = outcome.IsOverridden ? overrideReason : null;
    }

    public decimal EnergyKcal { get; }
    public decimal ProteinG { get; }
    public decimal CarbG { get; }
    public decimal FatG { get; }
    public PrescriptionOutcome Outcome { get; }
    public OverrideReason? OverrideReason { get; }
}
