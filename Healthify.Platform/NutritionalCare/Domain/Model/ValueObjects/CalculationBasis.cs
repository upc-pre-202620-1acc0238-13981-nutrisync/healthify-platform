namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     Everything the target calculation ran on, kept with the plan for ever. This is what makes a
///     target auditable: the equation is published, the parameters were chosen by a human, and the
///     number can be recomputed by hand. There is no black box.
/// </summary>
/// <remarks>
///     Business rule: Calculation Basis Always Recorded (Subflow 3.5). It never leaves this bounded
///     context: the published contract carries targets, guidelines and restrictions only.
///     The reference weight and the deficit strategy are held as flat components and rebuilt as value
///     objects by the computed properties below. Nesting them as owned types cannot be materialised
///     by EF Core, which refuses to bind a nested owned reference to a constructor parameter, so the
///     storage is flat while the domain reads exactly as the model defines it.
/// </remarks>
public sealed record CalculationBasis
{
    /// <summary>The constructor EF Core binds to: every parameter maps to a stored column.</summary>
    public CalculationBasis(
        Equation equation,
        string referenceWeightKind,
        decimal referenceWeightKg,
        decimal activityFactor,
        string deficitKind,
        decimal deficitValue,
        decimal computedBmr,
        decimal computedTdee)
    {
        if (activityFactor is < 1.0m or > 2.5m)
            throw new ArgumentException("The activity factor must be between 1.0 and 2.5.",
                nameof(activityFactor));
        if (computedBmr <= 0m)
            throw new ArgumentException("The computed BMR must be positive.", nameof(computedBmr));
        if (computedTdee <= 0m)
            throw new ArgumentException("The computed TDEE must be positive.", nameof(computedTdee));

        // Constructing the two components validates them, so an invalid basis cannot be built even
        // through this flat entry point.
        _ = new ReferenceWeight(referenceWeightKind, referenceWeightKg);
        _ = new DeficitStrategy(deficitKind, deficitValue);

        Equation = equation;
        ReferenceWeightKind = referenceWeightKind;
        ReferenceWeightKg = decimal.Round(referenceWeightKg, 2);
        ActivityFactor = decimal.Round(activityFactor, 2);
        DeficitKind = deficitKind;
        DeficitValue = decimal.Round(deficitValue, 2);
        ComputedBmr = decimal.Round(computedBmr, 2);
        ComputedTdee = decimal.Round(computedTdee, 2);
    }

    public Equation Equation { get; }

    public string ReferenceWeightKind { get; }
    public decimal ReferenceWeightKg { get; }

    /// <summary>
    ///     The largest single source of error in the whole calculation, and it comes out of a
    ///     conversation rather than a sensor. That is exactly why a human sets it.
    /// </summary>
    public decimal ActivityFactor { get; }

    public string DeficitKind { get; }
    public decimal DeficitValue { get; }

    public decimal ComputedBmr { get; }
    public decimal ComputedTdee { get; }

    /// <summary>Rebuilt from the stored components. Not a column.</summary>
    public ReferenceWeight ReferenceWeight => new(ReferenceWeightKind, ReferenceWeightKg);

    /// <summary>Rebuilt from the stored components. Not a column.</summary>
    public DeficitStrategy DeficitStrategy => new(DeficitKind, DeficitValue);

    /// <summary>The way the domain builds a basis: from the value objects, not from loose scalars.</summary>
    public static CalculationBasis From(
        Equation equation,
        ReferenceWeight referenceWeight,
        decimal activityFactor,
        DeficitStrategy deficitStrategy,
        decimal computedBmr,
        decimal computedTdee)
    {
        return new CalculationBasis(equation, referenceWeight.Kind, referenceWeight.ValueKg,
            activityFactor, deficitStrategy.Kind, deficitStrategy.Value, computedBmr, computedTdee);
    }
}
