using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Healthify.Platform.Shared.Infrastructure.Interfaces.ASP.RateLimiting;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Healthify.Platform.Shared.Interfaces.REST.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Healthify.Platform.Tests.Shared;

/// <summary>
///     D-RL. The real rate limiter middleware with the policies of <c>AddPlatformRateLimiting</c>, behind the same
///     request localization as <c>Program.cs</c>, with low limits set only for the tests. Requests carry the endpoint
///     metadata a marked controller action carries (<c>[EnableRateLimiting]</c>), so the partitioning is the real one.
/// </summary>
public class RateLimitingTests
{
    private static readonly IPAddress IpA = IPAddress.Parse("203.0.113.10");
    private static readonly IPAddress IpB = IPAddress.Parse("203.0.113.20");

    [Fact]
    public async Task Auth_answers_normally_within_the_limit_and_429_with_code_and_retry_after_above_it()
    {
        var limiter = Pipeline(("Auth:PermitLimit", "3"));

        for (var i = 0; i < 3; i++)
            Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(RateLimitingPolicies.Auth, IpA)).Response.StatusCode);

        var rejected = await limiter.Send(RateLimitingPolicies.Auth, IpA);

        AssertTooManyRequests(rejected, maxRetryAfterSeconds: 60);
    }

    [Fact]
    public async Task Auth_is_counted_per_ip()
    {
        var limiter = Pipeline(("Auth:PermitLimit", "2"));
        await limiter.Send(RateLimitingPolicies.Auth, IpA);
        await limiter.Send(RateLimitingPolicies.Auth, IpA);

        Assert.Equal(StatusCodes.Status429TooManyRequests,
            (await limiter.Send(RateLimitingPolicies.Auth, IpA)).Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(RateLimitingPolicies.Auth, IpB)).Response.StatusCode);
    }

    [Fact]
    public async Task Auth_counts_the_ip_even_with_a_session()
    {
        var limiter = Pipeline(("Auth:PermitLimit", "2"));
        await limiter.Send(RateLimitingPolicies.Auth, IpA, userId: 1);
        await limiter.Send(RateLimitingPolicies.Auth, IpA, userId: 2);

        Assert.Equal(StatusCodes.Status429TooManyRequests,
            (await limiter.Send(RateLimitingPolicies.Auth, IpA, userId: 3)).Response.StatusCode);
    }

    [Fact]
    public async Task A_few_legitimate_refresh_retries_in_a_row_are_not_rejected_with_the_default_limits()
    {
        // IAM-4: within the 30 s grace the app may repeat the same refresh (lost connection); the limit must not get
        // in the way of sign-in followed by those retries.
        var limiter = Pipeline();

        Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(RateLimitingPolicies.Auth, IpA)).Response.StatusCode);
        for (var retry = 0; retry < 4; retry++)
            Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(RateLimitingPolicies.Auth, IpA)).Response.StatusCode);
    }

    [Fact]
    public async Task The_default_auth_limit_is_ten_requests_per_minute()
    {
        var limiter = Pipeline();
        for (var i = 0; i < 10; i++)
            Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(RateLimitingPolicies.Auth, IpA)).Response.StatusCode);

        Assert.Equal(StatusCodes.Status429TooManyRequests,
            (await limiter.Send(RateLimitingPolicies.Auth, IpA)).Response.StatusCode);
    }

    [Fact]
    public async Task Ai_photo_answers_normally_within_the_limit_and_429_with_code_and_retry_after_above_it()
    {
        var limiter = Pipeline(("AiPhoto:PermitLimit", "2"));

        for (var i = 0; i < 2; i++)
            Assert.Equal(StatusCodes.Status200OK,
                (await limiter.Send(RateLimitingPolicies.AiPhoto, IpA, userId: 7)).Response.StatusCode);

        var rejected = await limiter.Send(RateLimitingPolicies.AiPhoto, IpA, userId: 7);

        AssertTooManyRequests(rejected, maxRetryAfterSeconds: 3600);
    }

    [Fact]
    public async Task Ai_photo_is_counted_per_user_two_users_do_not_share_their_quota()
    {
        var limiter = Pipeline(("AiPhoto:PermitLimit", "2"));
        await limiter.Send(RateLimitingPolicies.AiPhoto, IpA, userId: 7);
        await limiter.Send(RateLimitingPolicies.AiPhoto, IpA, userId: 7);

        Assert.Equal(StatusCodes.Status429TooManyRequests,
            (await limiter.Send(RateLimitingPolicies.AiPhoto, IpA, userId: 7)).Response.StatusCode);
        // Same IP, another user: own quota.
        Assert.Equal(StatusCodes.Status200OK,
            (await limiter.Send(RateLimitingPolicies.AiPhoto, IpA, userId: 8)).Response.StatusCode);
        // Same user from another network: still the same quota.
        Assert.Equal(StatusCodes.Status429TooManyRequests,
            (await limiter.Send(RateLimitingPolicies.AiPhoto, IpB, userId: 7)).Response.StatusCode);
    }

    [Fact]
    public async Task Global_answers_normally_within_the_limit_and_429_with_code_and_retry_after_above_it()
    {
        var limiter = Pipeline(("Global:PermitLimit", "3"));

        for (var i = 0; i < 3; i++)
            Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(null, IpA, userId: 7)).Response.StatusCode);

        AssertTooManyRequests(await limiter.Send(null, IpA, userId: 7), maxRetryAfterSeconds: 60);
    }

    [Fact]
    public async Task Global_counts_the_user_and_without_a_session_the_ip()
    {
        var limiter = Pipeline(("Global:PermitLimit", "2"));
        await limiter.Send(null, IpA);
        await limiter.Send(null, IpA);

        Assert.Equal(StatusCodes.Status429TooManyRequests, (await limiter.Send(null, IpA)).Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(null, IpB)).Response.StatusCode);
        // A signed-in user on the exhausted IP has their own quota…
        Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(null, IpA, userId: 7)).Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(null, IpB, userId: 7)).Response.StatusCode);
        // …that follows them across networks.
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await limiter.Send(null, IpB, userId: 7)).Response.StatusCode);
    }

    [Fact]
    public async Task The_global_limiter_also_applies_to_marked_endpoints()
    {
        var limiter = Pipeline(("Global:PermitLimit", "2"), ("Auth:PermitLimit", "50"));
        await limiter.Send(RateLimitingPolicies.Auth, IpA);
        await limiter.Send(RateLimitingPolicies.Auth, IpA);

        Assert.Equal(StatusCodes.Status429TooManyRequests,
            (await limiter.Send(RateLimitingPolicies.Auth, IpA)).Response.StatusCode);
    }

    [Fact]
    public async Task An_endpoint_with_disable_rate_limiting_is_never_limited()
    {
        // For health checks (none exist yet): [DisableRateLimiting] skips the global limiter too.
        var limiter = Pipeline(("Global:PermitLimit", "1"));

        for (var i = 0; i < 5; i++)
            Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(Disabled, IpA)).Response.StatusCode);
    }

    [Theory]
    [InlineData("es", "Demasiadas solicitudes", "Recibimos muchas solicitudes seguidas. Espera un momento y vuelve a intentarlo.")]
    [InlineData("es-PE", "Demasiadas solicitudes", "Recibimos muchas solicitudes seguidas. Espera un momento y vuelve a intentarlo.")]
    [InlineData("en", "Too many requests", "We received many requests in a short time. Please wait a moment and try again.")]
    [InlineData("en-US", "Too many requests", "We received many requests in a short time. Please wait a moment and try again.")]
    public async Task The_title_and_detail_follow_accept_language(string language, string title, string detail)
    {
        var limiter = Pipeline(("Auth:PermitLimit", "1"));
        await limiter.Send(RateLimitingPolicies.Auth, IpA, language: language);

        var rejected = await limiter.Send(RateLimitingPolicies.Auth, IpA, language: language);

        var body = Body(rejected);
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal(detail, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_429_body_carries_no_internal_data()
    {
        var limiter = Pipeline(("AiPhoto:PermitLimit", "1"));
        await limiter.Send(RateLimitingPolicies.AiPhoto, IpA, userId: 4242);

        var rejected = await limiter.Send(RateLimitingPolicies.AiPhoto, IpA, userId: 4242);

        var body = Body(rejected);
        var members = body.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Subset(new HashSet<string> { "type", "title", "status", "detail", "instance", "code" }, members);
        var raw = body.GetRawText();
        Assert.DoesNotContain(IpA.ToString(), raw);
        Assert.DoesNotContain("4242", raw);
        Assert.DoesNotContain("user:", raw);
        Assert.DoesNotContain("ip:", raw);
        Assert.DoesNotContain("3600", raw);
    }

    [Fact]
    public async Task Switched_off_nothing_is_limited()
    {
        var limiter = Pipeline(("Enabled", "false"), ("Auth:PermitLimit", "1"), ("AiPhoto:PermitLimit", "1"),
            ("Global:PermitLimit", "1"));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(RateLimitingPolicies.Auth, IpA)).Response.StatusCode);
            Assert.Equal(StatusCodes.Status200OK,
                (await limiter.Send(RateLimitingPolicies.AiPhoto, IpA, userId: 7)).Response.StatusCode);
            Assert.Equal(StatusCodes.Status200OK, (await limiter.Send(null, IpA)).Response.StatusCode);
        }
    }

    [Fact]
    public void The_defaults_live_in_code()
    {
        var settings = RateLimitingSettings.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.True(settings.Enabled);
        Assert.Equal(new RateLimitPolicySettings(10, TimeSpan.FromMinutes(1), 6), settings.Auth);
        Assert.Equal(new RateLimitPolicySettings(20, TimeSpan.FromHours(1)), settings.AiPhoto);
        Assert.Equal(new RateLimitPolicySettings(300, TimeSpan.FromMinutes(1)), settings.Global);
    }

    [Fact]
    public void Environment_variables_override_the_section()
    {
        const string prefix = "HEALTHIFY_DRL_TEST_";
        var variables = new Dictionary<string, string>
        {
            [prefix + "RateLimiting__Enabled"] = "false",
            [prefix + "RateLimiting__Auth__PermitLimit"] = "25",
            [prefix + "RateLimiting__AiPhoto__WindowSeconds"] = "600",
            [prefix + "RateLimiting__Global__PermitLimit"] = "1000"
        };
        try
        {
            foreach (var (name, value) in variables) Environment.SetEnvironmentVariable(name, value);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "12" })
                .AddEnvironmentVariables(prefix)
                .Build();

            var settings = RateLimitingSettings.FromConfiguration(configuration);

            Assert.False(settings.Enabled);
            Assert.Equal(25, settings.Auth.PermitLimit);
            Assert.Equal(TimeSpan.FromMinutes(10), settings.AiPhoto.Window);
            Assert.Equal(1000, settings.Global.PermitLimit);
        }
        finally
        {
            foreach (var name in variables.Keys) Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Each_policy_covers_exactly_its_endpoints()
    {
        var marked = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => (Action: $"{m.DeclaringType!.Name}.{m.Name}",
                Policy: m.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
                        ?? m.DeclaringType!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName))
            .Where(a => a.Policy is not null)
            .ToList();

        Assert.Equal(
            ["AuthenticationController.RefreshToken", "AuthenticationController.SignIn",
                "AuthenticationController.SignUp"],
            marked.Where(a => a.Policy == RateLimitingPolicies.Auth).Select(a => a.Action).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["ConsultationsController.SuggestDiagnosis", "ConsultationsController.SuggestGuidelines",
                "PatientAiSummariesController.GetMonitoringSummary",
                "PatientAiSummariesController.GetSuggestedQuestions", "PatientMealIdeasController.GenerateMealIdeas",
                "PatientMealPhotoAnalysesController.AnalyzeMealPhoto"],
            marked.Where(a => a.Policy == RateLimitingPolicies.AiPhoto).Select(a => a.Action).Order(StringComparer.Ordinal));
        Assert.All(marked, a => Assert.Contains(a.Policy, new[] { RateLimitingPolicies.Auth, RateLimitingPolicies.AiPhoto }));
    }

    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Marker for <see cref="Send" />: an endpoint with <c>[DisableRateLimiting]</c>.</summary>
    private const string Disabled = "<disabled>";

    private static void AssertTooManyRequests(HttpContext rejected, int maxRetryAfterSeconds)
    {
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        Assert.StartsWith("application/problem+json", rejected.Response.ContentType);

        var retryAfter = int.Parse(rejected.Response.Headers.RetryAfter.ToString(), CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, maxRetryAfterSeconds);

        var body = Body(rejected);
        Assert.Equal(ProblemDetailsErrorCodes.TooManyRequests, body.GetProperty("code").GetString());
        Assert.Equal(429, body.GetProperty("status").GetInt32());
        Assert.Equal(RateLimitingServiceCollectionExtensions.ProblemType, body.GetProperty("type").GetString());
    }

    private static JsonElement Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement.Clone();
    }

    private static RateLimitedPipeline Pipeline(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => $"{RateLimitingSettings.SectionName}:{s.Key}",
                s => (string?)s.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddPlatformRateLimiting(configuration);
        var provider = services.BuildServiceProvider();

        // Same order and cultures as Program.cs: localization, then the limiter, then the endpoint.
        string[] supportedCultures = ["en", "en-US", "es", "es-PE"];
        var app = new ApplicationBuilder(provider);
        app.UseRequestLocalization(new RequestLocalizationOptions()
            .SetDefaultCulture(supportedCultures[0])
            .AddSupportedCultures(supportedCultures)
            .AddSupportedUICultures(supportedCultures));
        app.UseRateLimiter();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        return new RateLimitedPipeline(provider, app.Build());
    }

    private sealed class RateLimitedPipeline(IServiceProvider services, RequestDelegate pipeline)
    {
        /// <summary>One request to an endpoint marked with <paramref name="policy" /> (null: unmarked).</summary>
        public async Task<HttpContext> Send(string? policy, IPAddress ip, int? userId = null, string language = "en")
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/v1/test";
            context.Request.Headers.AcceptLanguage = language;
            context.Connection.RemoteIpAddress = ip;
            context.Response.Body = new MemoryStream();
            if (userId is not null)
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString(CultureInfo.InvariantCulture))],
                    "Bearer"));

            object[] metadata = policy switch
            {
                null => [],
                Disabled => [new DisableRateLimitingAttribute()],
                _ => [new EnableRateLimitingAttribute(policy)]
            };
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata),
                "test endpoint"));

            var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            try
            {
                await pipeline(context);
            }
            finally
            {
                (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
            }

            return context;
        }
    }
}
