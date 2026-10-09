namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     Which weight the calculation runs on. Actual, ideal or adjusted are clinically different
///     answers, and choosing between them is the practitioner call, not the aggregate.
/// </summary>
public sealed record ReferenceWeight
{
    public const string Actual = "Actual";
    public const string Ideal = "Ideal";
    public const string Adjusted = "Adjusted";

    private static readonly HashSet<string> AllowedKinds = new(StringComparer.OrdinalIgnoreCase)
        { Actual, Ideal, Adjusted };

    public ReferenceWeight(string kind, decimal valueKg)
    {
        if (!AllowedKinds.Contains(kind))
            throw new ArgumentException(
                $"'{kind}' is not a valid reference weight kind. Allowed: {Actual}, {Ideal}, {Adjusted}.",
                nameof(kind));
        if (valueKg is < 20m or > 400m)
            throw new ArgumentException("The reference weight must be between 20 and 400 kg.", nameof(valueKg));

        Kind = AllowedKinds.First(a => a.Equals(kind, StringComparison.OrdinalIgnoreCase));
        ValueKg = decimal.Round(valueKg, 2);
    }

    public string Kind { get; }
    public decimal ValueKg { get; }
}
