using System.Globalization;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Calculators;

/// <summary>
///     NC-3. Reads <c>NutritionalCare:ActivityFactors:&lt;Level&gt;</c> and falls back to the default
///     factor of the level when the key is missing or implausible.
/// </summary>
public class ConfiguredActivityFactorProvider(
    IConfiguration configuration,
    ILogger<ConfiguredActivityFactorProvider> logger) : IActivityFactorProvider
{
    /// <summary>Same range the target proposal accepts for an activity factor (Subflow 3.3).</summary>
    private const decimal MinimumFactor = 1.0m;

    private const decimal MaximumFactor = 2.5m;

    public decimal FactorFor(ActivityLevel level)
    {
        var key = $"NutritionalCare:ActivityFactors:{level.Value}";
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw)) return level.DefaultFactor;

        // Configuration is invariant culture: "1.55", never "1,55".
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var configured))
        {
            logger.LogWarning("Ignoring activity factor configured at {Key}: not a number. Using {Default}.",
                key, level.DefaultFactor);
            return level.DefaultFactor;
        }

        if (configured is < MinimumFactor or > MaximumFactor)
        {
            logger.LogWarning(
                "Ignoring activity factor {Factor} configured at {Key}: outside {Minimum}-{Maximum}. Using {Default}.",
                configured, key, MinimumFactor, MaximumFactor, level.DefaultFactor);
            return level.DefaultFactor;
        }

        return configured;
    }
}
