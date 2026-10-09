namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     One point of the clinical weight series held by an evaluation window.
/// </summary>
/// <remarks>
///     Business rules: Clinical Measurement Outranks Self Weigh In and Two Series Never Merged
///     (Subflow 5.3). <see cref="Source" /> has exactly one legal value, and that is the enforcement:
///     nothing can be appended to this series that did not come from a measurement taken by a
///     practitioner under a recorded protocol. The patient's own readings live in the other context
///     as a smoothed trend, are read from there through its published contract, and are never mixed
///     into this list. Two series that measure the same quantity with different authority stay two
///     series, because averaging them would quietly give a bathroom scale clinical weight.
/// </remarks>
/// <param name="Date">The day the measurement was taken.</param>
/// <param name="ValueKg">The reading in kilograms.</param>
/// <param name="Source">Always <see cref="ClinicalMeasurement" />.</param>
public sealed record AnthropometryPoint(DateOnly Date, decimal ValueKg, string Source)
{
    /// <summary>The only source this series accepts.</summary>
    public const string ClinicalMeasurement = "ClinicalMeasurement";

    /// <summary>The day the measurement was taken.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>The reading in kilograms.</summary>
    public decimal ValueKg { get; init; } = ValueKg is > 0m and <= 400m
        ? decimal.Round(ValueKg, 2)
        : throw new ArgumentException("An anthropometry point must carry a plausible weight.",
            nameof(ValueKg));

    /// <summary>Always <see cref="ClinicalMeasurement" />.</summary>
    public string Source { get; init; } =
        string.Equals(Source, ClinicalMeasurement, StringComparison.OrdinalIgnoreCase)
            ? ClinicalMeasurement
            : throw new ArgumentException(
                "The clinical anthropometry series only accepts clinical measurements. The patient's own " +
                "readings are a separate series and the two are never merged.", nameof(Source));

    /// <summary>Builds a point from a clinical measurement, which is the only way to build one.</summary>
    public static AnthropometryPoint FromClinicalMeasurement(DateOnly date, decimal valueKg)
    {
        return new AnthropometryPoint(date, valueKg, ClinicalMeasurement);
    }
}
