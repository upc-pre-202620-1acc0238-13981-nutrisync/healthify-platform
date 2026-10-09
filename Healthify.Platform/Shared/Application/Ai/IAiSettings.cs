namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>
///     IA-0. The configuration the pipeline obeys, read on every generation so the kill switch acts without a
///     restart. Every value has its default in code: AI is off unless <c>Ai:Enabled</c> says otherwise.
/// </summary>
public interface IAiSettings
{
    /// <summary>Guard 1: <c>Ai:Enabled</c> (default false) and <c>Ai:Features:&lt;Feature&gt;:Enabled</c> (default true).</summary>
    bool IsEnabled(AiFeature feature);

    /// <summary>Guard 4: generations per rolling 24 hours (<c>Ai:Features:&lt;Feature&gt;:DailyLimit</c>).</summary>
    int DailyLimit(AiFeature feature);

    /// <summary><c>Ai:Features:&lt;Feature&gt;:MaxOutputTokens</c>, default 2048.</summary>
    int MaxOutputTokens(AiFeature feature);

    /// <summary>Guard 5: bound of each provider attempt (<c>Ai:TimeoutSeconds</c>, default 20).</summary>
    TimeSpan Timeout { get; }

    /// <summary>§12-#14: how long a generation is kept (<c>Ai:RetentionDays</c>, default 180).</summary>
    TimeSpan Retention { get; }
}
