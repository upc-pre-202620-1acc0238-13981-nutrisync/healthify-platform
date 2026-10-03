namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     IA-8. The words a message drafted by the AI for the patient must not contain (es and en, compared as whole
///     words without case or accents). The list lives in <c>patient-message-lexicon.json</c>, embedded.
/// </summary>
public interface IPatientMessageLexicon
{
    /// <summary>Business rule: Invitation Tone Never Accusation. "fallaste", "te pasaste", "failed"…</summary>
    IReadOnlyCollection<string> AccusatoryTerms { get; }

    /// <summary>
    ///     Business rule: Diagnosis Never Leaves The Context (§12-#9). Diagnosis, BMI and calculation basis words: the
    ///     patient never reads them.
    /// </summary>
    IReadOnlyCollection<string> DiagnosisTerms { get; }
}
