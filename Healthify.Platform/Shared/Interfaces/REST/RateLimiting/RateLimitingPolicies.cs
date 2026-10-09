namespace Healthify.Platform.Shared.Interfaces.REST.RateLimiting;

/// <summary>
///     D-RL. Names of the request rate limiting policies. A context only marks its endpoints with
///     <c>[EnableRateLimiting(RateLimitingPolicies.X)]</c>; the limits live in Shared
///     (<c>Shared/Infrastructure/Interfaces/ASP/RateLimiting</c>) and in the <c>RateLimiting</c> configuration section.
/// </summary>
public static class RateLimitingPolicies
{
    /// <summary>Anonymous account endpoints (sign-up, sign-in, token refresh), counted per client IP.</summary>
    public const string Auth = "auth";

    /// <summary>Endpoints that call the AI provider (Gemini) while the client waits, counted per authenticated user.</summary>
    public const string AiPhoto = "ai-photo";

    /// <summary>
    ///     Not attached to endpoints: the global limiter that every endpoint goes through, counted per authenticated
    ///     user or, without a session, per client IP. Named only for configuration and documentation.
    /// </summary>
    public const string Global = "global";
}
