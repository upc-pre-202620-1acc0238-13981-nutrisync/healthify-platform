using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     IA-8. The tone of a message for the patient drafted by the AI: whole words or phrases of a list, compared
///     without case or accents ("fallaste" matches "Fallaste"; "mal" does not match "normal"). Static domain service.
/// </summary>
public static class PatientMessageRules
{
    /// <summary>The terms of <paramref name="terms" /> that appear in <paramref name="text" /> as whole words.</summary>
    public static IReadOnlyList<string> TermsIn(string? text, IEnumerable<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var normalized = Normalize(text);
        return terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Where(t => Regex.IsMatch(normalized,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(Normalize(t))}(?![\p{{L}}\p{{N}}])",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            .Distinct()
            .ToList();
    }

    /// <summary>Lower case, without accents.</summary>
    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
