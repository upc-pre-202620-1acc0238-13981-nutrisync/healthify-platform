using System.Globalization;

namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     The reasoning behind a nutritional diagnosis.
/// </summary>
/// <remarks>Enforces the business rule "Clinical Rationale Required" (Subflow 3.2).</remarks>
public sealed record ClinicalRationale
{
    private const int MaximumLength = 2000;

    public ClinicalRationale(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A clinical rationale is required.", nameof(value));
        if (value.Length > MaximumLength)
            throw new ArgumentException($"The clinical rationale exceeds the maximum length of {MaximumLength}.",
                nameof(value));
        Value = value.Trim();
    }

    public string Value { get; }

    /// <summary>
    ///     NC-4. Deterministic rationale written from the measurement, used when the practitioner picks
    ///     the code without writing one ("IMC 26.3 kg/m²; cintura 88 cm"). It keeps the spirit of
    ///     Clinical Rationale Required without asking for text. Clinical data is not translated.
    /// </summary>
    public static ClinicalRationale FromMeasurement(decimal bmiKgM2, decimal? waistCm, decimal? bodyFatPercentage)
    {
        var parts = new List<string> { $"IMC {bmiKgM2.ToString("0.0", CultureInfo.InvariantCulture)} kg/m²" };
        if (waistCm is not null)
            parts.Add($"cintura {waistCm.Value.ToString("0.##", CultureInfo.InvariantCulture)} cm");
        if (bodyFatPercentage is not null)
            parts.Add($"grasa corporal {bodyFatPercentage.Value.ToString("0.##", CultureInfo.InvariantCulture)} %");
        return new ClinicalRationale(string.Join("; ", parts));
    }

    public override string ToString()
    {
        return Value;
    }
}
