namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     A medical history item from the closed list the baseline offers.
/// </summary>
/// <remarks>
///     There is no "None" and no "Other": an empty list means the patient has none of these. A free
///     text history from before the baseline existed is kept on the assessments that recorded it.
/// </remarks>
public sealed record MedicalCondition
{
    public const string Type2Diabetes = "Type2Diabetes";
    public const string Hypertension = "Hypertension";
    public const string CeliacDisease = "CeliacDisease";
    public const string Hypothyroidism = "Hypothyroidism";
    public const string ChronicKidneyDisease = "ChronicKidneyDisease";
    public const string Gout = "Gout";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Type2Diabetes, Hypertension, CeliacDisease, Hypothyroidism, ChronicKidneyDisease, Gout
    };

    public MedicalCondition(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Allowed.Contains(value.Trim()))
            throw new ArgumentException(
                $"'{value}' is not a known medical condition. Allowed: {string.Join(", ", Allowed)}.",
                nameof(value));
        Value = Allowed.First(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    /// <summary>Validates a whole selection. Duplicates collapse, order of first appearance is kept.</summary>
    public static IReadOnlyList<MedicalCondition> ListFrom(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Select(v => new MedicalCondition(v))
            .DistinctBy(c => c.Value)
            .ToList();
    }

    public override string ToString()
    {
        return Value;
    }
}
