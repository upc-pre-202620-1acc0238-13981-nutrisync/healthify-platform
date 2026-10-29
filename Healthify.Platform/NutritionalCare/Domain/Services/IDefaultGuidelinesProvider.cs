using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     The fixed table DiagnosisCode → guideline codes that pre-selects the chips of step 4
///     (suggested by diagnosis). It is the deterministic fallback of the AI suggestion: cheap, predictable and
///     enough for the MVP. Defaults in code, overridable in <c>NutritionalCare:DefaultGuidelinesByDiagnosis</c>.
/// </summary>
public interface IDefaultGuidelinesProvider
{
    /// <summary>Catalog codes of <see cref="Guideline" /> only; never free text.</summary>
    IReadOnlyList<string> For(DiagnosisCode diagnosis);
}
