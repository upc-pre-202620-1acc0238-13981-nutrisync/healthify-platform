namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Subflow 3.2 - Issue Diagnosis. Clinical phase 2.</summary>
/// <param name="PatientId">The patient the diagnosis is about.</param>
/// <param name="PractitionerId">The issuing practitioner, from the token.</param>
/// <param name="AssessmentId">The closed assessment the diagnosis reads.</param>
/// <param name="Statement">Free text diagnosis. With a <paramref name="Code" />, it may be empty and the code is kept.</param>
/// <param name="Rationale">
///     Required without a <paramref name="Code" />. With one, it may be empty and a deterministic rationale
///     is written from the measurement (NC-4).
/// </param>
/// <param name="Code">NC-4. A <c>DiagnosisCode</c> of the closed list, or null for the original free text diagnosis.</param>
/// <param name="Source">NC-4. A <c>DiagnosisSource</c>; defaults to PractitionerSelected when a code is given.</param>
/// <param name="AiGenerationId">NC-4. Required when the source is AiSuggestionAccepted (IA-0 traceability).</param>
public record IssueDiagnosisCommand(
    int PatientId,
    int PractitionerId,
    int AssessmentId,
    string? Statement,
    string? Rationale,
    string? Code = null,
    string? Source = null,
    long? AiGenerationId = null);
