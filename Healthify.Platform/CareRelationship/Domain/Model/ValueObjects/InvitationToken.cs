using System.Security.Cryptography;

namespace Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;

/// <summary>
///     The opaque single-use secret carried by the QR code the practitioner shows in the consulting
///     room. It reveals nothing about the invitation, the practitioner or the patient.
/// </summary>
/// <remarks>Enforces the business rule "Single Use Token" together with the uniqueness of its column.</remarks>
public sealed record InvitationToken
{
    private const int ByteLength = 32;
    private const int MinimumLength = 22;
    private const int MaximumLength = 64;

    public InvitationToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Invitation token cannot be empty.", nameof(value));

        var trimmed = value.Trim();

        if (trimmed.Length < MinimumLength || trimmed.Length > MaximumLength)
            throw new ArgumentException(
                $"Invitation token length must be between {MinimumLength} and {MaximumLength}.", nameof(value));
        if (!trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Invitation token contains characters that are not allowed.", nameof(value));

        Value = trimmed;
    }

    public string Value { get; }

    /// <summary>Produces a fresh cryptographically random token, url-safe so it fits in a QR code.</summary>
    public static InvitationToken Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(ByteLength);
        var encoded = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return new InvitationToken(encoded);
    }

    public override string ToString()
    {
        return Value;
    }
}
