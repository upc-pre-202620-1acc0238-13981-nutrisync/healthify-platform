using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Calculators;

/// <summary>
///     NC-6. Reads <c>NutritionalCare:DefaultGuidelinesByDiagnosis:&lt;DiagnosisCode&gt;</c> (a list of guideline
///     codes) and falls back to the table in code when the key is missing or holds no valid code. A code that
///     is not in the catalog is ignored with a warning: the output only ever contains catalog codes.
/// </summary>
/// <remarks>
///     NOTE: the table below is a starting point for the MVP, to be confirmed with the nutritionist. The
///     overweight row is the example of IA-7.
/// </remarks>
public class ConfiguredDefaultGuidelinesProvider(
    IConfiguration configuration,
    ILogger<ConfiguredDefaultGuidelinesProvider> logger) : IDefaultGuidelinesProvider
{
    private const string Section = "NutritionalCare:DefaultGuidelinesByDiagnosis";

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Defaults =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [DiagnosisCode.Underweight] =
                [Guideline.EatEvery3To4Hours, Guideline.ProteinAtBreakfast, Guideline.Drink2LWater],
            [DiagnosisCode.NormalWeight] =
                [Guideline.PrioritizeVegetables, Guideline.Drink2LWater, Guideline.AvoidSugaryDrinks],
            [DiagnosisCode.OverweightGradeI] =
                [Guideline.PrioritizeVegetables, Guideline.AvoidSugaryDrinks, Guideline.ReduceSalt],
            [DiagnosisCode.ObesityGradeI] =
            [
                Guideline.PrioritizeVegetables, Guideline.AvoidSugaryDrinks, Guideline.ReduceSalt,
                Guideline.ProteinAtBreakfast
            ],
            [DiagnosisCode.ObesityGradeII] =
            [
                Guideline.PrioritizeVegetables, Guideline.AvoidSugaryDrinks, Guideline.ReduceSalt,
                Guideline.ProteinAndVegetablesAtDinner
            ],
            [DiagnosisCode.ObesityGradeIII] =
            [
                Guideline.PrioritizeVegetables, Guideline.AvoidSugaryDrinks, Guideline.ReduceSalt,
                Guideline.ProteinAndVegetablesAtDinner
            ]
        };

    public IReadOnlyList<string> For(DiagnosisCode diagnosis)
    {
        var configured = configuration.GetSection($"{Section}:{diagnosis.Value}").GetChildren()
            .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (configured.Count == 0) return Defaults[diagnosis.Value];

        var valid = new List<string>();
        foreach (var code in configured)
            try
            {
                valid.Add(Guideline.FromCode(code!).Code!);
            }
            catch (ArgumentException)
            {
                logger.LogWarning("Ignoring guideline {Code} configured at {Section}:{Diagnosis}: not in the catalog.",
                    code, Section, diagnosis.Value);
            }

        return valid.Count == 0 ? Defaults[diagnosis.Value] : valid.Distinct().ToList();
    }
}
