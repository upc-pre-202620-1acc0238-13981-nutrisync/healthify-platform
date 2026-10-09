using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Healthify.Platform.Shared.Interfaces.REST.RateLimiting;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Healthify.Platform.Shared.Infrastructure.Interfaces.ASP.RateLimiting;

/// <summary>
///     D-RL. Request rate limiting with the native <c>Microsoft.AspNetCore.RateLimiting</c> middleware: the three
///     policies of <see cref="RateLimitingPolicies" /> and the 429 problem (<c>TooManyRequests</c>).
/// </summary>
/// <remarks>
///     <c>app.UseRateLimiter()</c> goes after authentication (the user partitions read the session) and after request
///     localization (the 429 texts follow <c>Accept-Language</c>). The client IP is
///     <see cref="ConnectionInfo.RemoteIpAddress" />: behind a reverse proxy every request shares the proxy IP until
///     forwarded headers are configured (not done in D-RL).
/// </remarks>
public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>RFC 6585 §4 (429 Too Many Requests).</summary>
    public const string ProblemType = "https://tools.ietf.org/html/rfc6585#section-4";

    private const string ProblemContentType = "application/problem+json";

    public static IServiceCollection AddPlatformRateLimiting(this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = RateLimitingSettings.FromConfiguration(configuration);
        services.AddSingleton(settings);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Policies are always registered, also when switched off: an [EnableRateLimiting] naming an unknown
            // policy fails at request time. Off, every partition is a no-limiter.
            options.AddPolicy(RateLimitingPolicies.Auth, context =>
                !settings.Enabled
                    ? RateLimitPartition.GetNoLimiter(string.Empty)
                    : RateLimitPartition.GetSlidingWindowLimiter(IpPartitionKey(context), _ =>
                        new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = settings.Auth.PermitLimit,
                            Window = settings.Auth.Window,
                            SegmentsPerWindow = settings.Auth.SegmentsPerWindow,
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));

            // DECISIÓN D-RL: one quota per user shared by every endpoint that calls the AI provider. Without a
            // session (the endpoint answers 401 anyway) the request is counted by IP.
            options.AddPolicy(RateLimitingPolicies.AiPhoto, context =>
                !settings.Enabled
                    ? RateLimitPartition.GetNoLimiter(string.Empty)
                    : RateLimitPartition.GetFixedWindowLimiter(UserOrIpPartitionKey(context), _ =>
                        FixedWindow(settings.AiPhoto)));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                !settings.Enabled
                    ? RateLimitPartition.GetNoLimiter(string.Empty)
                    : RateLimitPartition.GetFixedWindowLimiter(UserOrIpPartitionKey(context), _ =>
                        FixedWindow(settings.Global)));

            options.OnRejected = (context, cancellationToken) =>
                WriteTooManyRequestsAsync(context.HttpContext, context.Lease, settings, cancellationToken);
        });

        return services;
    }

    /// <summary>The client IP as the connection reports it; IPv4-mapped IPv6 is folded to IPv4.</summary>
    public static string IpPartitionKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is { IsIPv4MappedToIPv6: true }) address = address.MapToIPv4();
        return "ip:" + (address?.ToString() ?? IPAddress.None.ToString());
    }

    /// <summary>The authenticated user id (same claims as <c>GetAuthenticatedUserId</c>), or the IP without a session.</summary>
    public static string UserOrIpPartitionKey(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return string.IsNullOrEmpty(userId) ? IpPartitionKey(context) : "user:" + userId;
    }

    private static FixedWindowRateLimiterOptions FixedWindow(RateLimitPolicySettings policy)
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.PermitLimit,
            Window = policy.Window,
            QueueLimit = 0,
            AutoReplenishment = true
        };
    }

    /// <summary>
    ///     RFC 7807 problem with <c>extensions.code = TooManyRequests</c> (X-3), localized title and detail, and a
    ///     <c>Retry-After</c> header in seconds. The body never carries the IP, the partition or the limits.
    /// </summary>
    private static async ValueTask WriteTooManyRequestsAsync(HttpContext context, RateLimitLease lease,
        RateLimitingSettings settings, CancellationToken cancellationToken)
    {
        // Fixed windows report the exact wait. The sliding window ("auth") reports none: its next segment is the
        // earliest moment a permit can come back.
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var reported)
            ? reported
            : settings.Auth.Segment;
        context.Response.Headers.RetryAfter =
            Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var localizer = context.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
        var problem = ProblemDetailsFactory.Create(
            StatusCodes.Status429TooManyRequests,
            localizer["TooManyRequests"].Value,
            localizer["RequestRateExceeded"].Value,
            context.Request.Path,
            ProblemDetailsErrorCodes.TooManyRequests);
        problem.Type = ProblemType;

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, ProblemContentType,
            cancellationToken);
    }
}
