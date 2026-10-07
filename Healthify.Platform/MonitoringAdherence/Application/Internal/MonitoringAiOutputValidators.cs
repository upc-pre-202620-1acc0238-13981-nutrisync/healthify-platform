using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>
///     IA-2/IA-4/IA-5, guard 6 of the pipeline: the business rules of every text this context generates, over the
///     rules of <see cref="GeneratedTextRules" /> and the words of <see cref="IAiLanguageLexicon" />.
/// </summary>
public static class GeneratedTextChecks
{
    /// <param name="label">Where the text is, for the violation message (never the text itself).</param>
    /// <param name="text">The text.</param>
    /// <param name="facts">The facts its numbers must come from.</param>
    /// <param name="lexicon">The words it must not contain.</param>
    /// <param name="forPatient">A text the patient reads: no diagnosis, BMI or calculation basis.</param>
    /// <param name="consistencyAllowed">Whether it may mention the consistency index (IA-5 after an escalation).</param>
    /// <param name="violations">Where the broken rules are added.</param>
    public static void Check(string label, string? text, MonitoringPeriodFacts facts, IAiLanguageLexicon lexicon,
        bool forPatient, bool consistencyAllowed, List<string> violations)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            violations.Add($"{label}: empty text");
            return;
        }

        // Business rule: Invitation Tone Never Accusation.
        foreach (var term in GeneratedTextRules.TermsIn(text, lexicon.AccusatoryTerms))
            violations.Add($"{label}: accusatory term '{term}'");

        // Business rule: Diagnosis Never Leaves The Context (§12-#9). The input has no diagnosis; the output may
        // still not invent one.
        if (forPatient)
            foreach (var term in GeneratedTextRules.TermsIn(text, lexicon.DiagnosisTerms))
                violations.Add($"{label}: diagnosis or calculation basis term '{term}'");

        // Business rule: Patient Shown First (IA-5).
        if (!consistencyAllowed)
            foreach (var term in GeneratedTextRules.TermsIn(text, lexicon.ConsistencyTerms))
                violations.Add($"{label}: consistency index term '{term}' before an escalation");

        // Business rule: The AI Does Not Count (IA-2).
        foreach (var violation in GeneratedTextRules.NumbersNotInFacts(text, facts))
            violations.Add($"{label}: {violation}");
    }
}

/// <summary>IA-2. The weekly summary the patient reads.</summary>
public sealed class WeeklySummaryOutputValidator(MonitoringPeriodFacts facts, IAiLanguageLexicon lexicon)
    : IAiOutputValidator<WeeklySummaryOutput>
{
    public IReadOnlyList<string> Validate(WeeklySummaryOutput output)
    {
        var violations = new List<string>();
        var wentWell = output.WentWell ?? [];
        var watchOut = output.WatchOut ?? [];

        if (wentWell.Count is < 1 or > WeeklySummary.MaximumWentWell)
            violations.Add($"wentWell: 1 to {WeeklySummary.MaximumWentWell} items");
        if (watchOut.Count > WeeklySummary.MaximumWatchOut)
            violations.Add($"watchOut: at most {WeeklySummary.MaximumWatchOut} items");
        if ((output.Headline?.Trim().Length ?? 0) > WeeklySummary.HeadlineMaximumLength)
            violations.Add("headline: too long");

        GeneratedTextChecks.Check("headline", output.Headline, facts, lexicon, true, false, violations);
        for (var i = 0; i < wentWell.Count; i++)
        {
            if ((wentWell[i]?.Trim().Length ?? 0) > WeeklySummary.BulletMaximumLength)
                violations.Add($"wentWell[{i}]: too long");
            GeneratedTextChecks.Check($"wentWell[{i}]", wentWell[i], facts, lexicon, true, false, violations);
        }

        for (var i = 0; i < watchOut.Count; i++)
        {
            if ((watchOut[i]?.Trim().Length ?? 0) > WeeklySummary.BulletMaximumLength)
                violations.Add($"watchOut[{i}]: too long");
            GeneratedTextChecks.Check($"watchOut[{i}]", watchOut[i], facts, lexicon, true, false, violations);
        }

        return violations;
    }
}

/// <summary>
///     IA-4. The questions the patient may bring to the visit: 3 to 5, 10 to 120 characters, a question each (ends
///     in "?"), no two alike.
/// </summary>
public sealed class SuggestedQuestionsOutputValidator(MonitoringPeriodFacts facts, IAiLanguageLexicon lexicon)
    : IAiOutputValidator<SuggestedQuestionsOutput>
{
    public const int MinimumQuestions = 3;
    public const int MaximumQuestions = 5;
    public const int MinimumLength = 10;
    public const int MaximumLength = 120;

    public IReadOnlyList<string> Validate(SuggestedQuestionsOutput output)
    {
        var violations = new List<string>();
        var questions = output.Questions ?? [];

        if (questions.Count is < MinimumQuestions or > MaximumQuestions)
            violations.Add($"questions: {MinimumQuestions} to {MaximumQuestions} items");
        if (questions.Select(q => GeneratedTextRules.Normalize(q?.Trim() ?? "")).Distinct().Count() != questions.Count)
            violations.Add("questions: repeated question");

        for (var i = 0; i < questions.Count; i++)
        {
            var text = questions[i]?.Trim() ?? "";
            if (text.Length is < MinimumLength or > MaximumLength)
                violations.Add($"questions[{i}]: {MinimumLength} to {MaximumLength} characters");
            if (!text.EndsWith('?'))
                violations.Add($"questions[{i}]: not a question (must end in '?')");
            GeneratedTextChecks.Check($"questions[{i}]", text, facts, lexicon, true, false, violations);
        }

        return violations;
    }
}

/// <summary>
///     IA-5. The monitoring summary the practitioner reads: professional and direct, never accusatory, and silent
///     about the consistency index until it reached the practitioner as an escalation.
/// </summary>
public sealed class MonitoringSummaryOutputValidator(
    MonitoringPeriodFacts facts,
    IAiLanguageLexicon lexicon,
    bool consistencyAllowed) : IAiOutputValidator<MonitoringSummaryOutput>
{
    public const int MaximumLength = 600;

    public IReadOnlyList<string> Validate(MonitoringSummaryOutput output)
    {
        var violations = new List<string>();
        if ((output.Text?.Trim().Length ?? 0) > MaximumLength) violations.Add($"text: over {MaximumLength} characters");
        GeneratedTextChecks.Check("text", output.Text, facts, lexicon, false, consistencyAllowed, violations);
        return violations;
    }
}
