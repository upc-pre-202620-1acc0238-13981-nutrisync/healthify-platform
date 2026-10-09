namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     IA-7. The guideline chips EV-5 pre-selects for the diagnosis of step 2: catalog codes only, never free text.
///     From the AI when it is available, valid and consented to; otherwise from the fixed table of NC-6
///     (<c>NutritionalCare:DefaultGuidelinesByDiagnosis</c>).
/// </summary>
/// <param name="Suggested">Codes of the guideline catalog, each once.</param>
/// <param name="Source">"Ai" or "Rule".</param>
/// <param name="AiGenerationId">The generation; null for the fixed table.</param>
public record GuidelineSuggestions(IReadOnlyList<string> Suggested, string Source, long? AiGenerationId);
