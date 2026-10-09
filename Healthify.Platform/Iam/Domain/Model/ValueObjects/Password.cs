namespace Healthify.Platform.Iam.Domain.Model.ValueObjects;

/// <summary>
///     A plaintext password that has satisfied the strength policy. It exists only long enough to be
///     hashed: nothing in the platform persists or logs an instance of this type.
/// </summary>
/// <remarks>Enforces the business rule "Strong Password Required" (Iam, Subflow 1.1).</remarks>
public sealed record Password
{
    private const int MinimumLength = 8;
    private const int MaximumLength = 128;

    public Password(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Password cannot be empty.", nameof(value));
        if (value.Length < MinimumLength)
            throw new ArgumentException($"Password must be at least {MinimumLength} characters long.", nameof(value));
        if (value.Length > MaximumLength)
            throw new ArgumentException($"Password exceeds maximum length of {MaximumLength}.", nameof(value));
        if (!value.Any(char.IsUpper))
            throw new ArgumentException("Password must contain at least one uppercase letter.", nameof(value));
        if (!value.Any(char.IsLower))
            throw new ArgumentException("Password must contain at least one lowercase letter.", nameof(value));
        if (!value.Any(char.IsDigit))
            throw new ArgumentException("Password must contain at least one digit.", nameof(value));
        if (value.All(char.IsLetterOrDigit))
            throw new ArgumentException("Password must contain at least one special character.", nameof(value));
        Value = value;
    }

    public string Value { get; }
}
