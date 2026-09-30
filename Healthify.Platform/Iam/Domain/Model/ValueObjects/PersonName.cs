using System.Text.RegularExpressions;

namespace Healthify.Platform.Iam.Domain.Model.ValueObjects;

/// <summary>
///     IAM-1. The given names and family names of the person behind an account, as they wrote them at
///     registration ("María José" / "Flores Quispe").
/// </summary>
/// <remarks>
///     Human text, not an identifier: it is trimmed and its inner spaces collapsed, both parts are
///     required, and digits or angle brackets are rejected. It is what the other contexts show beside a
///     patient ("Ana Flores", "Hola, María"), never something they match on.
/// </remarks>
public sealed partial record PersonName
{
    public const int MaxLength = 80;

    public PersonName(string? givenNames, string? familyNames)
    {
        GivenNames = Normalize(givenNames, nameof(givenNames));
        FamilyNames = Normalize(familyNames, nameof(familyNames));
    }

    /// <summary>"María José".</summary>
    public string GivenNames { get; }

    /// <summary>"Flores Quispe".</summary>
    public string FamilyNames { get; }

    /// <summary>"María José Flores Quispe".</summary>
    public string FullName => $"{GivenNames} {FamilyNames}";

    /// <summary>"María", for "Hola, María".</summary>
    public string FirstGivenName => GivenNames.Split(' ')[0];

    /// <summary>The letter of the avatar.</summary>
    public char Initial => char.ToUpperInvariant(GivenNames[0]);

    public override string ToString()
    {
        return FullName;
    }

    private static string Normalize(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Both given names and family names are required.", parameterName);

        var collapsed = InnerWhitespace().Replace(value.Trim(), " ");

        if (collapsed.Length > MaxLength)
            throw new ArgumentException($"A name cannot exceed {MaxLength} characters.", parameterName);
        if (collapsed.Any(c => char.IsDigit(c) || c is '<' or '>'))
            throw new ArgumentException("A name cannot contain digits or angle brackets.", parameterName);

        return collapsed;
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex InnerWhitespace();
}
