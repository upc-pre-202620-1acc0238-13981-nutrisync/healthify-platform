using System.Globalization;
using System.Text;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;

namespace Healthify.Platform.FoodCatalog.Domain.Services;

/// <summary>
///     FC-2. Decides which entry of the local catalog a free name ("Pechuga de pollo", "camote") stands for. Pure
///     and deterministic: the same name and candidates always give the same answer, so an idea resolved twice
///     resolves to the same food.
/// </summary>
/// <remarks>
///     Business rule: Ingredient Resolved Only When Every Word Matches (FC-2). A candidate matches when each
///     significant word of the name (connectors such as "de", "con" or "y" do not count) is the start of a word of
///     the candidate, ignoring case, accents, punctuation and a final plural "s": "pechuga de pollo" matches
///     "Pechuga de pollo sin piel cocida", "camotes" matches "Camote amarillo sancochado", and "pollo frito"
///     matches nothing that is not fried chicken. A name that matches nothing stays unresolved: a guess here would
///     put wrong nutrients on a meal.
///     Among the candidates that match, the closest wins: an identical name first, then the one with fewer words
///     the name did not ask for, then a local override (a practitioner added it for this population), then the
///     shorter name, then the oldest entry.
/// </remarks>
public static class FoodNameMatcher
{
    private static readonly HashSet<string> Connectors =
    [
        "de", "del", "la", "el", "las", "los", "con", "y", "en", "al", "a", "sin", "of", "the", "with", "and",
        "in", "un", "una"
    ];

    /// <summary>Characters of the search term: enough to narrow the catalog, short enough to survive a plural.</summary>
    public const int SearchTermLength = 5;

    /// <summary>
    ///     What narrows the catalog search: the start of the longest significant word of the name, so "frijoles"
    ///     still finds "Frijol canario cocido". <see cref="BestMatch" /> decides among what comes back.
    /// </summary>
    /// <returns>Null when the name has no significant word.</returns>
    public static string? SearchTermOf(string name)
    {
        var word = SignificantWords(name).OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal)
            .FirstOrDefault();
        return word is null || word.Length <= SearchTermLength ? word : word[..SearchTermLength];
    }

    /// <summary>The closest candidate whose words cover every significant word of <paramref name="name" />, or null.</summary>
    public static ReferenceFood? BestMatch(string name, IEnumerable<ReferenceFood> candidates)
    {
        var wanted = SignificantWords(name);
        if (wanted.Count == 0) return null;
        var normalizedName = Normalize(name);

        return candidates
            .Select(c => new { Food = c, Words = Words(c.LocalNameText), Normalized = Normalize(c.LocalNameText) })
            .Where(c => wanted.All(w => c.Words.Any(cw => WordMatches(w, cw))))
            .OrderByDescending(c => c.Normalized == normalizedName)
            .ThenBy(c => c.Words.Count(cw => !wanted.Any(w => WordMatches(w, cw))))
            .ThenByDescending(c => c.Food.IsLocalOverride)
            .ThenBy(c => c.Food.LocalNameText.Length)
            .ThenBy(c => c.Food.Id.Value)
            .Select(c => c.Food)
            .FirstOrDefault();
    }

    /// <summary>IN-7. A clear similar match shares at least this many significant words with the name.</summary>
    public const int MinimumSharedWords = 2;

    /// <summary>IN-7. … and the longer of the two has at most this many significant words more.</summary>
    public const int MaximumExtraWords = 1;

    /// <summary>
    ///     IN-7. What narrows the catalog when looking for a dish similar to <paramref name="name" />: the start of
    ///     each of its longest significant words (a similar entry may lack any one of them).
    /// </summary>
    public static IReadOnlyList<string> SearchTermsOf(string name, int max)
    {
        return SignificantWords(name).OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal)
            .Take(Math.Max(1, max))
            .Select(w => w.Length <= SearchTermLength ? w : w[..SearchTermLength])
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     IN-7. The catalog entry that is clearly the same dish as <paramref name="name" />, or null.
    /// </summary>
    /// <remarks>
    ///     Business rule: Similar Dish Reused, Not Duplicated (IN-7). Same words as FC-2 (significant words, no accents,
    ///     no final plural), but in both directions: «Lomo saltado de res» is clearly «Lomo saltado», and «Lomo saltado»
    ///     clearly «Lomo saltado de res». It is clear only when every significant word of the shorter one is in the
    ///     longer, the shorter has at least <see cref="MinimumSharedWords" /> of them (one word, such as «Pollo», is
    ///     not enough to say «Pollo a la brasa»), the longer adds at most <see cref="MaximumExtraWords" />, and no other
    ///     entry is as close: a tie between two different foods is not clear, and then nothing is reused.
    /// </remarks>
    public static ReferenceFood? ClearSimilarMatch(string name, IEnumerable<ReferenceFood> candidates)
    {
        var wanted = SignificantWords(name);
        if (wanted.Count == 0) return null;
        var normalizedName = Normalize(name);

        var ranked = candidates
            .GroupBy(c => c.Id.Value).Select(g => g.First())
            .Select(c => new { Food = c, Words = SignificantWords(c.LocalNameText), Normalized = Normalize(c.LocalNameText) })
            .Where(c => c.Words.Count > 0)
            .Where(c => Covers(wanted, c.Words) || Covers(c.Words, wanted))
            .Select(c => new
            {
                c.Food,
                Identical = c.Normalized == normalizedName,
                Shared = Math.Min(wanted.Count, c.Words.Count),
                Extra = Math.Abs(wanted.Count - c.Words.Count)
            })
            .Where(c => c.Identical || (c.Shared >= MinimumSharedWords && c.Extra <= MaximumExtraWords))
            .OrderByDescending(c => c.Identical)
            .ThenBy(c => c.Extra)
            .ThenByDescending(c => c.Food.IsLocalOverride)
            .ThenBy(c => c.Food.Id.Value)
            .ToList();

        if (ranked.Count == 0) return null;
        var best = ranked[0];
        if (!best.Identical && ranked.Count > 1 && !ranked[1].Identical && ranked[1].Extra == best.Extra)
            return null; // two entries equally close: not clear
        return best.Food;
    }

    /// <summary>Every word of <paramref name="shorter" /> matches a word of <paramref name="longer" />.</summary>
    private static bool Covers(IReadOnlyList<string> shorter, IReadOnlyList<string> longer)
    {
        return shorter.All(w => longer.Any(l => WordMatches(w, l)));
    }

    /// <summary>Lower case, without accents or punctuation, single spaces.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<string> SignificantWords(string? text)
    {
        return Words(text).Where(w => !Connectors.Contains(w)).Distinct().ToList();
    }

    private static List<string> Words(string? text)
    {
        return Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Singular).ToList();
    }

    /// <summary>A word of the name matches a word of the candidate that starts with it, or that it extends.</summary>
    private static bool WordMatches(string wanted, string candidate)
    {
        return candidate.StartsWith(wanted, StringComparison.Ordinal) ||
               (candidate.Length >= 4 && wanted.StartsWith(candidate, StringComparison.Ordinal));
    }

    private static string Singular(string word)
    {
        return word.Length > 3 && word.EndsWith('s') ? word[..^1] : word;
    }
}
