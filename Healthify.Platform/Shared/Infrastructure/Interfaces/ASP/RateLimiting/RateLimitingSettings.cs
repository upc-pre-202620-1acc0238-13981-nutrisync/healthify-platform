using Microsoft.Extensions.Configuration;

namespace Healthify.Platform.Shared.Infrastructure.Interfaces.ASP.RateLimiting;

/// <summary>Limit of one rate limiting policy: <see cref="PermitLimit" /> requests per <see cref="Window" />.</summary>
/// <param name="PermitLimit">Requests allowed in a window (at least 1).</param>
/// <param name="Window">Length of the window (at least one second).</param>
/// <param name="SegmentsPerWindow">Segments of a sliding window; 1 for a fixed window.</param>
public sealed record RateLimitPolicySettings(int PermitLimit, TimeSpan Window, int SegmentsPerWindow = 1)
{
    /// <summary>The shortest time after which a rejected client may get a permit back.</summary>
    public TimeSpan Segment => Window / SegmentsPerWindow;
}

/// <summary>
///     D-RL. The <c>RateLimiting</c> configuration section, read once at start-up with its defaults in code. Every
///     value can be overridden by environment variable (<c>RateLimiting__Auth__PermitLimit=20</c>).
/// </summary>
public sealed record RateLimitingSettings(
    bool Enabled,
    RateLimitPolicySettings Auth,
    RateLimitPolicySettings AiPhoto,
    RateLimitPolicySettings Global)
{
    public const string SectionName = "RateLimiting";

    public static RateLimitingSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new RateLimitingSettings(
            section.GetValue<bool?>("Enabled") ?? true,
            // DECISIÓN D-RL: "auth" is a sliding window (as asked) of 6 segments: a client that hit the limit gets
            // permits back every 10 s instead of waiting for the whole minute.
            ReadPolicy(section.GetSection("Auth"), 10, 60, 6),
            // DECISIÓN D-RL: "ai-photo" and "global" are fixed windows, so the Retry-After is exact (the limiter
            // reports it). The AI daily quota of the pipeline (AiRateLimited) still applies on top.
            ReadPolicy(section.GetSection("AiPhoto"), 20, 3600),
            ReadPolicy(section.GetSection("Global"), 300, 60));
    }

    private static RateLimitPolicySettings ReadPolicy(IConfigurationSection section, int permitLimit,
        int windowSeconds, int? slidingSegments = null)
    {
        var limit = Math.Max(1, section.GetValue<int?>("PermitLimit") ?? permitLimit);
        var seconds = Math.Max(1, section.GetValue<int?>("WindowSeconds") ?? windowSeconds);
        // Only a sliding window has segments; a fixed window ignores the key.
        var segmentsPerWindow = slidingSegments is { } segments
            ? Math.Clamp(section.GetValue<int?>("SegmentsPerWindow") ?? segments, 1, seconds)
            : 1;
        return new RateLimitPolicySettings(limit, TimeSpan.FromSeconds(seconds), segmentsPerWindow);
    }
}
