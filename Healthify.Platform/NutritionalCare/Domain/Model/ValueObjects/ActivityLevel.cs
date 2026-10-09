namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-3. Current physical activity, chosen from a closed list in EV-2. It replaces the free text
///     physical activity of the original assessment.
/// </summary>
/// <remarks>
///     Each level carries a default activity factor. The factors actually used can be overridden in
///     configuration (<c>NutritionalCare:ActivityFactors</c>), see <c>IActivityFactorProvider</c>.
/// </remarks>
public sealed record ActivityLevel
{
    public const string Sedentary = "Sedentary";
    public const string Light = "Light";
    public const string Moderate = "Moderate";
    public const string Intense = "Intense";

    private static readonly Dictionary<string, decimal> DefaultFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        [Sedentary] = 1.2m,
        [Light] = 1.375m,
        [Moderate] = 1.55m,
        [Intense] = 1.725m
    };

    public ActivityLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !DefaultFactors.ContainsKey(value.Trim()))
            throw new ArgumentException(
                $"'{value}' is not a valid activity level. Allowed: {string.Join(", ", All)}.", nameof(value));
        Value = DefaultFactors.Keys.First(k => k.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every level, from the least to the most active.</summary>
    public static IReadOnlyList<string> All { get; } = [Sedentary, Light, Moderate, Intense];

    public string Value { get; }

    /// <summary>The factor used when configuration does not override it.</summary>
    public decimal DefaultFactor => DefaultFactors[Value];

    public override string ToString()
    {
        return Value;
    }
}
