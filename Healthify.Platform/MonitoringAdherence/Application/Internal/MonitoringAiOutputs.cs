using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>IA-2. What the model answers for the weekly summary (schema of <c>weekly-summary@n.md</c>).</summary>
public sealed record WeeklySummaryOutput(string Headline, IReadOnlyList<string> WentWell, IReadOnlyList<string> WatchOut);

/// <summary>IA-4. What the model answers for the suggested questions (schema of <c>suggested-questions@n.md</c>).</summary>
public sealed record SuggestedQuestionsOutput(IReadOnlyList<string> Questions);

/// <summary>IA-5. What the model answers for the monitoring summary.</summary>
public sealed record MonitoringSummaryOutput(string Text);

/// <summary>IA-4. One suggested question, with the identifier the app sends back when the patient adds it.</summary>
/// <param name="Id">Stable within its generation.</param>
/// <param name="Text">The question, in first person, ending in "?".</param>
public sealed record SuggestedQuestion(string Id, string Text);

/// <summary>IA-4. Application DTO: the questions of one generation and the period they were based on.</summary>
/// <param name="Questions">Three to five questions.</param>
/// <param name="AiGenerationId">The generation (sent back as the origin when the patient adds one, MA-4).</param>
/// <param name="BasedOnFrom">First day of the diary read.</param>
/// <param name="BasedOnTo">Last day of the diary read.</param>
/// <param name="GeneratedAt">When it was generated.</param>
/// <param name="Language">X-2. es or en: the language the questions were generated in (the patient's).</param>
public sealed record SuggestedQuestionsView(
    IReadOnlyList<SuggestedQuestion> Questions,
    long AiGenerationId,
    DateOnly BasedOnFrom,
    DateOnly BasedOnTo,
    DateTimeOffset GeneratedAt,
    string? Language = null);

/// <summary>
///     IA-5. Application DTO: the facts of the period (always) and the text the AI wrote from them (when it could).
/// </summary>
/// <param name="Text">The summary; null in the deterministic fallback.</param>
/// <param name="Facts">The facts, computed without AI.</param>
/// <param name="ConsistencyState">
///     Normal, Watch or Alert, only once a ConsistencyEscalation reached the practitioner (Patient Shown First);
///     null otherwise.
/// </param>
/// <param name="AiGenerationId">The generation; null in the fallback.</param>
/// <param name="GeneratedAt">When the text was generated, or the facts computed.</param>
/// <param name="TextUnavailableReason">
///     Why there is no text: AiFeatureDisabled, AiConsentRequired (§12-#5) or NotEnoughData. Null with a text.
/// </param>
public sealed record MonitoringSummaryView(
    string? Text,
    MonitoringPeriodFacts Facts,
    string? ConsistencyState,
    long? AiGenerationId,
    DateTimeOffset GeneratedAt,
    string? TextUnavailableReason);
