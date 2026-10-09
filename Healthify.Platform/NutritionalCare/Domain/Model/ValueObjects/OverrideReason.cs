namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     Why the practitioner replaced the calculated numbers.
/// </summary>
/// <remarks>Enforces the business rule "Override Requires Reason" (Subflow 3.4).</remarks>
public sealed record OverrideReason
{
    private const int MaximumLength = 500;

    public OverrideReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("An override reason is required.", nameof(value));
        if (value.Length > MaximumLength)
            throw new ArgumentException($"The override reason exceeds the maximum length of {MaximumLength}.",
                nameof(value));
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
