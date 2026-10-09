namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     NC-4/IA-6. The suggestion card of EV-3: the AI suggestion when it is available, accepted by its validator and
///     consented to; otherwise the deterministic one, the WHO category of the body mass index, with
///     <see cref="Source" /> "Rule" and no generation. EV-3 is never left empty.
/// </summary>
/// <remarks>Professional information: never reaches a patient endpoint or the published contract.</remarks>
/// <param name="Code">A <c>DiagnosisCode</c>.</param>
/// <param name="Rationale">The rationale the AI proposed, or the deterministic one ("IMC 26.3 kg/m²; cintura 88 cm").</param>
/// <param name="Source">"Ai" or "Rule".</param>
/// <param name="AiGenerationId">The generation of an AI suggestion; null for the deterministic one.</param>
public record DiagnosisSuggestion(string Code, string Rationale, string Source, long? AiGenerationId)
{
    public const string RuleSource = "Rule";

    /// <summary>IA-6. Sent back as <c>AiSuggestionAccepted</c> with its generation when the practitioner uses it.</summary>
    public const string AiSource = "Ai";
}
