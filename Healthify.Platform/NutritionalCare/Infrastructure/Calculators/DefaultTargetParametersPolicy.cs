using System.Globalization;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Calculators;

/// <summary>
///     NC-5. Reads <c>NutritionalCare:DefaultTargetParameters</c> and falls back, value by value, to the
///     defaults in code when a key is missing or invalid: Mifflin-St Jeor, actual weight, a fixed deficit of
///     500 kcal, 1.6 g of protein per kg and 30 % of the energy from fat.
/// </summary>
/// <remarks>
///     NOTE: the MD writes the deficit kind as "Kcal"; the value object calls it <c>FixedKcal</c>, which is
///     what is configured. The reference weight of the defaults is always the measured weight: an ideal or
///     adjusted weight is a number the practitioner gives through "Cambiar parámetros".
/// </remarks>
public class DefaultTargetParametersPolicy(
    IConfiguration configuration,
    IActivityFactorProvider activityFactorProvider,
    ILogger<DefaultTargetParametersPolicy> logger) : IDefaultTargetParametersPolicy
{
    private const string Section = "NutritionalCare:DefaultTargetParameters";

    public const string DefaultEquation = Equation.MifflinStJeor;
    public const string DefaultDeficitKind = DeficitStrategy.FixedKcal;
    public const decimal DefaultDeficitValue = 500m;
    public const decimal DefaultProteinGramsPerKg = 1.6m;
    public const decimal DefaultFatPercentOfEnergy = 30m;

    public ProposeTargetsCommand For(int patientId, int practitionerId, DiagnosisCode? diagnosis,
        ActivityLevel activityLevel, decimal measuredWeightKg)
    {
        var equation = ReadEquation();
        var referenceKind = ReadReferenceWeightKind();
        var (deficitKind, deficitValue) = ReadDeficit();
        var protein = ReadDecimal("ProteinGramsPerKg", DefaultProteinGramsPerKg, v => v is > 0m and <= 4m);
        var fat = ReadDecimal("FatPercentOfEnergy", DefaultFatPercentOfEnergy, v => v is >= 10m and <= 60m);

        // DECISIÓN §12-#2: for Underweight and NormalWeight the default deficit is 0 kcal. No weight loss is
        // proposed to someone with low or normal weight; to be confirmed with the nutritionist.
        if (diagnosis is { IsAtOrBelowNormalWeight: true }) deficitValue = 0m;

        return new ProposeTargetsCommand(patientId, practitionerId, equation, referenceKind, measuredWeightKg,
            activityFactorProvider.FactorFor(activityLevel), deficitKind, deficitValue, protein, fat);
    }

    private string ReadEquation()
    {
        var raw = configuration[$"{Section}:Equation"];
        if (string.IsNullOrWhiteSpace(raw)) return DefaultEquation;
        try
        {
            return new Equation(raw).Value;
        }
        catch (ArgumentException)
        {
            Warn("Equation", raw, DefaultEquation);
            return DefaultEquation;
        }
    }

    private string ReadReferenceWeightKind()
    {
        var raw = configuration[$"{Section}:ReferenceWeightKind"];
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Equals(ReferenceWeight.Actual, StringComparison.OrdinalIgnoreCase))
            return ReferenceWeight.Actual;

        Warn("ReferenceWeightKind", raw, ReferenceWeight.Actual);
        return ReferenceWeight.Actual;
    }

    private (string Kind, decimal Value) ReadDeficit()
    {
        var rawKind = configuration[$"{Section}:DeficitKind"];
        var kind = string.IsNullOrWhiteSpace(rawKind) ? DefaultDeficitKind : rawKind.Trim();
        var value = ReadDecimal("DeficitValue", DefaultDeficitValue, _ => true);
        try
        {
            var strategy = new DeficitStrategy(kind, value);
            return (strategy.Kind, strategy.Value);
        }
        catch (ArgumentException)
        {
            Warn("DeficitKind/DeficitValue", $"{kind} {value}", $"{DefaultDeficitKind} {DefaultDeficitValue}");
            return (DefaultDeficitKind, DefaultDeficitValue);
        }
    }

    private decimal ReadDecimal(string key, decimal fallback, Func<decimal, bool> plausible)
    {
        var raw = configuration[$"{Section}:{key}"];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        // Configuration is invariant culture: "1.6", never "1,6".
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && plausible(value))
            return value;

        Warn(key, raw, fallback.ToString(CultureInfo.InvariantCulture));
        return fallback;
    }

    private void Warn(string key, string configured, string fallback)
    {
        logger.LogWarning("Ignoring {Section}:{Key} = {Configured}. Using {Default}.", Section, key, configured,
            fallback);
    }
}
