namespace Healthify.Platform.Iam.Domain.Model.ValueObjects;

/// <summary>
///     IAM-3. The language the account wants the interface in: <c>es</c> or <c>en</c> (PT21.I, PR20.I).
/// </summary>
/// <remarks>
///     «Los datos clínicos no se traducen; solo la interfaz». This chooses the language of messages, errors and
///     AI texts, never of what a practitioner wrote. A regional tag (<c>es-PE</c>, <c>en-US</c>) is accepted and
///     kept as its language, because the resource files are per language.
/// </remarks>
public sealed record PreferredLanguage
{
    public const string Spanish = "es";
    public const string English = "en";

    /// <summary>IAM-3: <c>es</c> for every account until it says otherwise.</summary>
    public static readonly PreferredLanguage Default = new(Spanish);

    public PreferredLanguage(string? value)
    {
        var language = value?.Trim().Split('-', '_')[0].ToLowerInvariant();
        if (language is not (Spanish or English))
            throw new ArgumentException($"'{value}' is not a supported language. Allowed: {Spanish}, {English}.",
                nameof(value));
        Value = language;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
