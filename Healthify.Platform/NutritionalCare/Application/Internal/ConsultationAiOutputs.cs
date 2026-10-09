namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>IA-6. What the model answers (schema of <c>diagnosis-suggestion@n.md</c>).</summary>
public sealed record DiagnosisSuggestionOutput(string Code, string Rationale);

/// <summary>IA-7. What the model answers (schema of <c>guideline-suggestions@n.md</c>).</summary>
public sealed record GuidelineSuggestionsOutput(IReadOnlyList<string> Suggested);
