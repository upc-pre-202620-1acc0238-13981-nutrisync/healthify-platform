namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     Which way a deviation goes.
/// </summary>
/// <remarks>
///     Two values, and neither of them is a verdict. Above and Below describe a direction on a
///     number line, which is all this context is entitled to say.
/// </remarks>
public sealed record DeviationDirection
{
    public const string Above = "Above";
    public const string Below = "Below";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { Above, Below };

    public DeviationDirection(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid deviation direction. Allowed: {Above}, {Below}.",
                nameof(value));

        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsAbove => Value == Above;
    public bool IsBelow => Value == Below;

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     How large a deviation is, expressed both as a fraction of the target and in kilocalories.
/// </summary>
/// <remarks>
///     Both numbers come from the same computation and are kept together because they answer
///     different questions: the fraction is comparable across patients and plans, and the absolute
///     figure is the one a practitioner can act on. Neither is a grade.
/// </remarks>
public sealed record DeviationMagnitude
{
    public DeviationMagnitude(decimal relativeValue, decimal absoluteEnergyKcal)
    {
        if (relativeValue < 0m)
            throw new ArgumentException("A deviation magnitude is an absolute size and cannot be negative.",
                nameof(relativeValue));
        if (absoluteEnergyKcal < 0m)
            throw new ArgumentException("A deviation magnitude is an absolute size and cannot be negative.",
                nameof(absoluteEnergyKcal));

        RelativeValue = decimal.Round(relativeValue, 4);
        AbsoluteEnergyKcal = decimal.Round(absoluteEnergyKcal, 2);
    }

    /// <summary>Mean distance from the target across the logged days, as a fraction of the target.</summary>
    public decimal RelativeValue { get; }

    /// <summary>The same distance in kilocalories per day.</summary>
    public decimal AbsoluteEnergyKcal { get; }

    public override string ToString()
    {
        return $"{RelativeValue:P2} ({AbsoluteEnergyKcal:0.##} kcal/day)";
    }
}
