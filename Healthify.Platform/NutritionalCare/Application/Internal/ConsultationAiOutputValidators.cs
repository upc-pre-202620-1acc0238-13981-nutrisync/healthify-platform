using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     IA-6, guard 6. What a diagnosis suggestion must meet once it is schema-valid: a code of the closed list
///     (NC-4), at most one grade away from the category of the body mass index, and a rationale that fits.
/// </summary>
/// <remarks>
///     A rejected suggestion is never shown: the command service answers with the deterministic one (source "Rule").
/// </remarks>
public sealed class DiagnosisSuggestionOutputValidator(DiagnosisCode bmiCategory)
    : IAiOutputValidator<DiagnosisSuggestionOutput>
{
    /// <summary>IA-6: "rationale ≤ 600 chars".</summary>
    public const int MaximumRationaleLength = 600;

    public IReadOnlyList<string> Validate(DiagnosisSuggestionOutput output)
    {
        var violations = new List<string>();

        DiagnosisCode? code = null;
        try
        {
            code = new DiagnosisCode(output.Code);
        }
        catch (ArgumentException)
        {
            violations.Add("$.code: not in the closed diagnosis list.");
        }

        // Business rule: AI Suggestion Within One Grade Of The Index (IA-6).
        if (code is not null && !code.IsWithinOneGradeOf(bmiCategory))
            violations.Add($"$.code: {code.Value} contradicts the BMI category {bmiCategory.Value} by more than one grade.");

        if (string.IsNullOrWhiteSpace(output.Rationale))
            violations.Add("$.rationale: empty.");
        else if (output.Rationale.Trim().Length > MaximumRationaleLength)
            violations.Add($"$.rationale: longer than {MaximumRationaleLength} characters.");

        return violations;
    }
}

/// <summary>
///     IA-7, guard 6. Guideline suggestions are codes of the catalog (NC-6), each once, and nothing else: no custom
///     text can come from the model.
/// </summary>
public sealed class GuidelineSuggestionsOutputValidator : IAiOutputValidator<GuidelineSuggestionsOutput>
{
    public const int MaximumSuggestions = 5;

    public static GuidelineSuggestionsOutputValidator Instance { get; } = new();

    public IReadOnlyList<string> Validate(GuidelineSuggestionsOutput output)
    {
        var violations = new List<string>();
        var suggested = output.Suggested ?? [];

        if (suggested.Count is 0 or > MaximumSuggestions)
            violations.Add($"$.suggested: between 1 and {MaximumSuggestions} codes expected.");

        // Business rule: Closed Guideline Catalog (NC-6), for the AI as for the practitioner.
        foreach (var code in suggested)
            if (!Guideline.Codes.Contains(code, StringComparer.Ordinal))
                violations.Add($"$.suggested: '{code}' is not a code of the guideline catalog.");

        if (suggested.Distinct(StringComparer.Ordinal).Count() != suggested.Count)
            violations.Add("$.suggested: a code appears more than once.");

        return violations;
    }
}
