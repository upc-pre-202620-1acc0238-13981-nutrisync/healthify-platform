using System.Globalization;
using System.Text;
using System.Text.Json;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Lexicon;

/// <summary>
///     IA-3. <see cref="IRestrictionLexicon" /> over <c>RestrictionLexicon.json</c>, a file of this context embedded in
///     the assembly (see the csproj). Spanish and English terms live together: a text is checked against both.
/// </summary>
/// <remarks>
///     A term matches whole words of the text, ignoring case, accents, punctuation and a final plural "s"
///     ("Langostinos al ajo" breaks ShellfishFree; "mal" does not match "malta"). A category's exceptions are removed
///     from the text before its terms are looked for, so "leche de almendras" is not dairy.
/// </remarks>
public sealed class EmbeddedRestrictionLexicon : IRestrictionLexicon
{
    public const string ResourceName = "IntakeBodyResponse/RestrictionLexicon.json";

    private readonly Dictionary<string, Category> _categories;
    private readonly IReadOnlyList<string> _forbidden;
    private readonly Dictionary<string, IReadOnlyList<string>> _restrictions;

    private EmbeddedRestrictionLexicon(Dictionary<string, Category> categories,
        Dictionary<string, IReadOnlyList<string>> restrictions, IReadOnlyList<string> forbidden)
    {
        _categories = categories;
        _restrictions = restrictions;
        _forbidden = forbidden;
    }

    /// <summary>The lexicon embedded in this assembly, loaded once.</summary>
    public static EmbeddedRestrictionLexicon Instance { get; } = Load();

    /// <summary>The restriction codes the lexicon knows.</summary>
    public IReadOnlyCollection<string> KnownRestrictions => _restrictions.Keys;

    public IReadOnlyList<string> ViolatedRestrictions(string text, IReadOnlyCollection<string> restrictionCodes)
    {
        if (string.IsNullOrWhiteSpace(text) || restrictionCodes.Count == 0) return [];
        var padded = Padded(text);

        var violated = new List<string>();
        foreach (var code in restrictionCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_restrictions.TryGetValue(code, out var categories)) continue;
            if (categories.Any(c => _categories.TryGetValue(c, out var category) && category.Matches(padded)))
                violated.Add(code);
        }

        return violated;
    }

    public IReadOnlyList<string> ForbiddenTermsIn(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var padded = Padded(text);
        return _forbidden.Where(term => padded.Contains(term, StringComparison.Ordinal))
            .Select(term => term.Trim()).ToList();
    }

    /// <summary>Lower case, no accents, no punctuation, no final plural "s", padded with spaces: " pollo al horno ".</summary>
    internal static string Padded(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 3 && w.EndsWith('s') ? w[..^1] : w);
        return $" {string.Join(' ', words)} ";
    }

    /// <exception cref="InvalidOperationException">The file is missing, or a restriction names an unknown category.</exception>
    private static EmbeddedRestrictionLexicon Load()
    {
        using var stream = typeof(EmbeddedRestrictionLexicon).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var categories = root.GetProperty("categories").EnumerateObject().ToDictionary(c => c.Name,
            c => new Category(Terms(c.Value.GetProperty("terms")), Terms(c.Value.GetProperty("exceptions"))),
            StringComparer.Ordinal);

        var restrictions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var restriction in root.GetProperty("restrictions").EnumerateObject())
        {
            var names = restriction.Value.EnumerateArray().Select(c => c.GetString()!).ToList();
            var unknown = names.FirstOrDefault(n => !categories.ContainsKey(n));
            if (unknown is not null)
                throw new InvalidOperationException(
                    $"Restriction {restriction.Name} names the unknown category '{unknown}'.");
            restrictions[restriction.Name] = names;
        }

        return new EmbeddedRestrictionLexicon(categories, restrictions,
            Terms(root.GetProperty("forbiddenInPatientText")));
    }

    private static IReadOnlyList<string> Terms(JsonElement array)
    {
        return array.EnumerateArray().Select(t => t.GetString())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => Padded(t!))
            .Where(t => t.Trim().Length > 0)
            .Distinct(StringComparer.Ordinal)
            // Longest first, so "concha de abanico" is removed whole before "concha".
            .OrderByDescending(t => t.Length)
            .ToList();
    }

    private sealed record Category(IReadOnlyList<string> Terms, IReadOnlyList<string> Exceptions)
    {
        public bool Matches(string padded)
        {
            var text = Exceptions.Aggregate(padded, (current, exception) =>
                current.Replace(exception, " ", StringComparison.Ordinal));
            return Terms.Any(term => text.Contains(term, StringComparison.Ordinal));
        }
    }
}
