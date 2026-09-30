using System.Text.RegularExpressions;

namespace Healthify.Platform.Iam.Domain.Model.ValueObjects;

/// <summary>Account email address. Normalised to lowercase so that uniqueness is case insensitive.</summary>
public sealed partial record Email
{
    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email cannot be empty.", nameof(value));
        if (value.Length > 255)
            throw new ArgumentException("Email exceeds maximum length of 255.", nameof(value));
        if (!Pattern().IsMatch(value))
            throw new ArgumentException($"'{value}' is not a valid email address.", nameof(value));
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled)]
    private static partial Regex Pattern();
}
