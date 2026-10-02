namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-2. The <c>Idempotency-Key</c> header of the publication (EV-5.E "Volver a intentarlo"): repeating the
///     request with the same key returns the same result and never publishes a second version.
/// </summary>
public sealed record IdempotencyKey
{
    public const int MaximumLength = 64;

    public IdempotencyKey(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
            throw new ArgumentException($"An idempotency key has 1 to {MaximumLength} characters.", nameof(value));
        // Visible ASCII only: it travels in an HTTP header and is compared byte by byte.
        if (value.Any(c => c is < '!' or > '~'))
            throw new ArgumentException("An idempotency key holds visible ASCII characters only.", nameof(value));
        Value = value;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
