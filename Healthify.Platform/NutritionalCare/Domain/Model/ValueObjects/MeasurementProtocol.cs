namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     How a clinical measurement was taken. Recording it is what gives the measurement its clinical
///     authority, and it is why a Clinical Measurement outranks a Self Weigh In.
/// </summary>
/// <remarks>Enforces the business rule "Measurement Protocol Recorded" (Subflow 3.1).</remarks>
public sealed record MeasurementProtocol
{
    private const int MaximumLength = 300;

    public MeasurementProtocol(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("The measurement protocol must be recorded.", nameof(value));
        if (value.Length > MaximumLength)
            throw new ArgumentException($"The measurement protocol exceeds the maximum length of {MaximumLength}.",
                nameof(value));
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
