namespace Healthify.Platform.MonitoringAdherence.Domain.Services;

/// <summary>
///     IA-2/IA-4/IA-5. The words a generated text of this context must not contain, in Spanish and English. The
///     list lives in a file of this context (<c>Infrastructure/Ai/Lexicon/generated-text-lexicon.json</c>) so it can
///     grow without touching the rules that use it.
/// </summary>
public interface IAiLanguageLexicon
{
    /// <summary>
    ///     Business rule: Invitation Tone Never Accusation. "fallaste", "te pasaste", "mal", "failed"… For every text
    ///     of this context, the patient's and the practitioner's.
    /// </summary>
    IReadOnlyCollection<string> AccusatoryTerms { get; }

    /// <summary>
    ///     Business rule: Diagnosis Never Leaves The Context (§12-#9). Diagnosis, BMI and calculation basis words, for
    ///     the texts the patient reads (IA-2, IA-4).
    /// </summary>
    IReadOnlyCollection<string> DiagnosisTerms { get; }

    /// <summary>
    ///     Business rule: Patient Shown First (IA-5). Words of the consistency index, for a practitioner text whose
    ///     input did not include it.
    /// </summary>
    IReadOnlyCollection<string> ConsistencyTerms { get; }
}
