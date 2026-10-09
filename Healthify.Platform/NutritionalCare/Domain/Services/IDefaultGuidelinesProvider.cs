using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     NC-6. The fixed table DiagnosisCode → guideline codes that pre-selects the chips of EV-5
///     ("Sugeridas según el diagnóstico"). It is the deterministic fallback of IA-7: cheap, predictable and
///     enough for the MVP. Defaults in code, overridable in <c>NutritionalCare:DefaultGuidelinesByDiagnosis</c>.
/// </summary>
public interface IDefaultGuidelinesProvider
{
    /// <summary>Catalog codes of <see cref="Guideline" /> only; never free text.</summary>
    IReadOnlyList<string> For(DiagnosisCode diagnosis);
}
