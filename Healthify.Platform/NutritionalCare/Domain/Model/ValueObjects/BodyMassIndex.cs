namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-3. Body mass index of a clinical measurement, in kg/m² with one decimal, and its WHO
///     category.
/// </summary>
/// <remarks>
///     The category is taken from the rounded value, so the number shown and its category always agree
///     (24.96 shows as 25.0 and is OverweightGradeI). The codes are the ones NC-4 uses for the
///     diagnosis. DECISIÓN §12-#9: neither the index nor its category ever leaves this bounded context;
///     no facade publishes them, because for the patient the category is a diagnosis.
/// </remarks>
public sealed record BodyMassIndex
{
    public const string Underweight = "Underweight";
    public const string NormalWeight = "NormalWeight";
    public const string OverweightGradeI = "OverweightGradeI";
    public const string ObesityGradeI = "ObesityGradeI";
    public const string ObesityGradeII = "ObesityGradeII";
    public const string ObesityGradeIII = "ObesityGradeIII";

    public BodyMassIndex(decimal weightKg, decimal heightCm)
    {
        if (weightKg <= 0m) throw new ArgumentException("The weight must be positive.", nameof(weightKg));
        if (heightCm <= 0m) throw new ArgumentException("The height must be positive.", nameof(heightCm));

        var heightM = heightCm / 100m;
        Value = decimal.Round(weightKg / (heightM * heightM), 1, MidpointRounding.AwayFromZero);
        Category = CategoryFor(Value);
    }

    /// <summary>kg/m², one decimal.</summary>
    public decimal Value { get; }

    /// <summary>WHO category of <see cref="Value" />.</summary>
    public string Category { get; }

    /// <summary>WHO cut-offs: &lt;18.5, 18.5-24.9, 25-29.9, 30-34.9, 35-39.9, ≥40.</summary>
    public static string CategoryFor(decimal bmi)
    {
        return bmi switch
        {
            < 18.5m => Underweight,
            < 25m => NormalWeight,
            < 30m => OverweightGradeI,
            < 35m => ObesityGradeI,
            < 40m => ObesityGradeII,
            _ => ObesityGradeIII
        };
    }
}
