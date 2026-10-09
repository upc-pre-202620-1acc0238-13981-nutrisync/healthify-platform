using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Services;

/// <summary>
///     IA-2/IA-4/IA-5. The rules every generated text of this context meets before anyone reads it. Static domain
///     service: no state, no I/O; the lists of words come from <see cref="IAiLanguageLexicon" />.
/// </summary>
/// <remarks>
///     Business rule: The AI Does Not Count (IA-2). Every number in a text must be one of the facts of its period;
///     "N de M días" must name the days of the period as M and one of its day counts as N. Small numbers written as
///     words ("cinco de siete días") are checked the same way.
///     Business rule: Invitation Tone Never Accusation. Whole words or phrases of the lexicon, compared without case
///     or accents ("fallaste" matches "Fallaste"; "mal" does not match "normal").
/// </remarks>
public static partial class GeneratedTextRules
{
    /// <summary>Spelled numbers a sentence about days may use. "un/una/one" are articles too, so they are left out.</summary>
    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.Ordinal)
    {
        ["cero"] = 0, ["zero"] = 0, ["dos"] = 2, ["two"] = 2, ["tres"] = 3, ["three"] = 3, ["cuatro"] = 4,
        ["four"] = 4, ["cinco"] = 5, ["five"] = 5, ["seis"] = 6, ["six"] = 6, ["siete"] = 7, ["seven"] = 7,
        ["ocho"] = 8, ["eight"] = 8, ["nueve"] = 9, ["nine"] = 9, ["diez"] = 10, ["ten"] = 10
    };

    /// <summary>The terms of <paramref name="terms" /> that appear in <paramref name="text" /> as whole words.</summary>
    public static IReadOnlyList<string> TermsIn(string? text, IEnumerable<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var normalized = Normalize(text);
        return terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Where(t => Regex.IsMatch(normalized, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(Normalize(t))}(?![\p{{L}}\p{{N}}])",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            .Distinct()
            .ToList();
    }

    /// <summary>
    ///     The numbers of <paramref name="text" /> that are not facts of the period, and the "N de M días" sentences
    ///     whose N or M does not match. Empty when every number is a fact.
    /// </summary>
    public static IReadOnlyList<string> NumbersNotInFacts(string? text, MonitoringPeriodFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (string.IsNullOrWhiteSpace(text)) return [];

        var violations = new List<string>();
        var allowed = facts.AllowedNumbers();
        var normalized = Normalize(text);

        foreach (Match match in DigitsPattern().Matches(normalized))
        {
            var number = decimal.Parse(match.Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            if (!allowed.Any(a => Math.Abs(a - number) < 0.05m))
                violations.Add($"the number {match.Value} is not a fact of the period");
        }

        foreach (Match match in WordPattern().Matches(normalized))
            if (NumberWords.TryGetValue(match.Value, out var number) && !allowed.Contains(number))
                violations.Add($"the number '{match.Value}' is not a fact of the period");

        var dayCounts = facts.DayCounts();
        foreach (Match match in RatioPattern().Matches(normalized))
        {
            if (!TryNumber(match.Groups["n"].Value, out var n) || !TryNumber(match.Groups["m"].Value, out var m))
                continue;
            if (m != facts.TotalDays || !dayCounts.Contains(n))
                violations.Add($"'{match.Value}' does not match the {facts.TotalDays} days of the period");
        }

        return violations;
    }

    /// <summary>Lower case, without accents: how texts and terms are compared.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool TryNumber(string token, out int number)
    {
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out number)) return true;
        return NumberWords.TryGetValue(token, out number);
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])\d+(?:[.,]\d+)?(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsPattern();

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<n>\d+|\p{L}+)\s+(?:de|of|out\s+of)\s+(?<m>\d+|\p{L}+)(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant)]
    private static partial Regex RatioPattern();
}
