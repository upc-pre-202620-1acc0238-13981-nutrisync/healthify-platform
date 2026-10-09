using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.Shared.Infrastructure.Ai.Configuration;

/// <summary>
///     IA-0. <see cref="IAiSettings" /> over <c>IConfiguration</c>, read on every call. Defaults in code, never only
///     in appsettings: without configuration, AI is off.
/// </summary>
public class ConfiguredAiSettings(IConfiguration configuration, ILogger<ConfiguredAiSettings> logger) : IAiSettings
{
    public const string SupportedProvider = "Gemini";

    /// <summary>
    ///     Gemini 3.5 Flash, stable model code as Google's model list publishes it
    ///     (https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash, checked 2026-10-06).
    /// </summary>
    public const string DefaultModel = "gemini-3.5-flash";

    public const int DefaultTimeoutSeconds = 20;
    public const int DefaultRetentionDays = 180;
    public const int DefaultMaxOutputTokens = 2048;

    // DECISIÓN IA-0: the MD fixes only the meal ideas quota (10 per day per patient). The other patient functions
    // get 20 per patient and the practitioner ones 100 per practitioner, both per rolling 24 hours.
    public const int DefaultMealIdeasDailyLimit = 10;

    /// <summary>IN-7: photos of meals per patient per rolling 24 hours (several meals a day, each maybe retaken).</summary>
    public const int DefaultMealPhotoRecognitionDailyLimit = 30;
    public const int DefaultPatientDailyLimit = 20;
    public const int DefaultPractitionerDailyLimit = 100;

    public bool IsEnabled(AiFeature feature)
    {
        if (!(configuration.GetValue<bool?>("Ai:Enabled") ?? false)) return false;

        var provider = configuration["Ai:Provider"];
        if (!string.IsNullOrWhiteSpace(provider) &&
            !string.Equals(provider, SupportedProvider, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Ai:Provider {Provider} is not supported (only {Supported}); AI stays off",
                provider, SupportedProvider);
            return false;
        }

        return configuration.GetValue<bool?>($"Ai:Features:{feature.Name}:Enabled") ?? true;
    }

    public int DailyLimit(AiFeature feature)
    {
        var configured = configuration.GetValue<int?>($"Ai:Features:{feature.Name}:DailyLimit")
                         // The key the MD uses for meal ideas.
                         ?? configuration.GetValue<int?>($"Ai:Features:{feature.Name}:DailyLimitPerPatient");
        if (configured is >= 0) return configured.Value;

        if (feature == AiFeature.MealIdeas) return DefaultMealIdeasDailyLimit;
        if (feature == AiFeature.MealPhotoRecognition) return DefaultMealPhotoRecognitionDailyLimit;
        return feature.Audience == AiFeatureAudience.Patient ? DefaultPatientDailyLimit : DefaultPractitionerDailyLimit;
    }

    public int MaxOutputTokens(AiFeature feature)
    {
        var configured = configuration.GetValue<int?>($"Ai:Features:{feature.Name}:MaxOutputTokens");
        return configured is > 0 ? configured.Value : DefaultMaxOutputTokens;
    }

    public TimeSpan Timeout
    {
        get
        {
            var seconds = configuration.GetValue<int?>("Ai:TimeoutSeconds");
            return TimeSpan.FromSeconds(seconds is > 0 ? seconds.Value : DefaultTimeoutSeconds);
        }
    }

    public TimeSpan Retention
    {
        get
        {
            var days = configuration.GetValue<int?>("Ai:RetentionDays");
            return TimeSpan.FromDays(days is > 0 ? days.Value : DefaultRetentionDays);
        }
    }
}
