namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     How much energy is subtracted from the total expenditure, and how that amount is expressed.
///     The size of the deficit is a clinical decision and arrives in the command.
/// </summary>
public sealed record DeficitStrategy
{
    public const string FixedKcal = "FixedKcal";
    public const string PercentOfTdee = "PercentOfTdee";

    private static readonly HashSet<string> AllowedKinds = new(StringComparer.OrdinalIgnoreCase)
        { FixedKcal, PercentOfTdee };

    public DeficitStrategy(string kind, decimal value)
    {
        if (!AllowedKinds.Contains(kind))
            throw new ArgumentException(
                $"'{kind}' is not a valid deficit strategy. Allowed: {FixedKcal}, {PercentOfTdee}.",
                nameof(kind));

        var normalised = AllowedKinds.First(a => a.Equals(kind, StringComparison.OrdinalIgnoreCase));

        if (normalised == FixedKcal && value is < 0m or > 1500m)
            throw new ArgumentException("A fixed deficit must be between 0 and 1500 kcal.", nameof(value));
        if (normalised == PercentOfTdee && value is < 0m or > 40m)
            throw new ArgumentException("A percentage deficit must be between 0 and 40.", nameof(value));

        Kind = normalised;
        Value = decimal.Round(value, 2);
    }

    public string Kind { get; }
    public decimal Value { get; }

    /// <summary>Business rule: Target Energy Equals TDEE Minus Deficit (Subflow 3.3).</summary>
    public decimal DeficitKcalFor(decimal tdee)
    {
        return Kind == FixedKcal ? Value : decimal.Round(tdee * Value / 100m, 2);
    }
}
