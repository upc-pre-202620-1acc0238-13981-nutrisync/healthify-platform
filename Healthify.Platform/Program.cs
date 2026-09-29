using System.Text;
using Cortex.Mediator.DependencyInjection;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Ai.Configuration;
using Healthify.Platform.Shared.Infrastructure.Ai.Gemini;
using Healthify.Platform.Shared.Infrastructure.Ai.Persistence;
using Healthify.Platform.Shared.Infrastructure.Ai.Prompts;
using Healthify.Platform.Shared.Infrastructure.Ai.Scheduling;
using Healthify.Platform.Shared.Infrastructure.Interfaces.ASP.Configuration;
using Healthify.Platform.Shared.Infrastructure.Interfaces.ASP.RateLimiting;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

// Iam BC
using Healthify.Platform.Iam.Application.Acl;
using Healthify.Platform.Iam.Application.CommandServices;
using Healthify.Platform.Iam.Application.Internal.CommandServices;
using Healthify.Platform.Iam.Application.Internal.QueryServices;
using Healthify.Platform.Iam.Application.QueryServices;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Iam.Domain.Services;
using Healthify.Platform.Iam.Infrastructure.Hashing.BCrypt;
using Healthify.Platform.Iam.Infrastructure.Localization;
using Healthify.Platform.Iam.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Iam.Infrastructure.Security;
using Healthify.Platform.Iam.Infrastructure.Tokens.JWT;
using Healthify.Platform.Iam.Infrastructure.Tokens.Refresh;
using Healthify.Platform.Iam.Interfaces.Acl;

// CareRelationship BC
using Healthify.Platform.CareRelationship.Application.Acl;
using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Application.Internal.CommandServices;
using Healthify.Platform.CareRelationship.Application.Internal.QueryServices;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.CareRelationship.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.CareRelationship.Infrastructure.Scheduling;
using Healthify.Platform.CareRelationship.Interfaces.Acl;

// NutritionalCare BC
using Healthify.Platform.NutritionalCare.Application.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal.QueryServices;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.NutritionalCare.Infrastructure.Ai.Lexicon;
using Healthify.Platform.NutritionalCare.Infrastructure.Calculators;
using Healthify.Platform.NutritionalCare.Infrastructure.Clock;
using Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.NutritionalCare.Infrastructure.Scheduling;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;

// FoodCatalog BC
using Healthify.Platform.FoodCatalog.Application.Acl;
using Healthify.Platform.FoodCatalog.Application.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal.QueryServices;
using Healthify.Platform.FoodCatalog.Application.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Repositories;
using Healthify.Platform.FoodCatalog.Domain.Services;
using Healthify.Platform.FoodCatalog.Infrastructure.External.OpenFoodFacts;
using Healthify.Platform.FoodCatalog.Infrastructure.External.Usda;
using Healthify.Platform.FoodCatalog.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.FoodCatalog.Infrastructure.Persistence.EFC.Seeders;
using Healthify.Platform.FoodCatalog.Infrastructure.Scheduling;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;

// IntakeBodyResponse BC
using Healthify.Platform.IntakeBodyResponse.Application.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Caching;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Ai.Lexicon;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Imaging;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Maintenance;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Protocols;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Scheduling;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;

// MonitoringAdherence BC
using Healthify.Platform.MonitoringAdherence.Application.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.QueryServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Ai.Caching;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Ai.Lexicon;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;

// ReadModels composition layer
using Healthify.Platform.ReadModels.Application;

var builder = WebApplication.CreateBuilder(args);

// An external food provider that does not answer promptly is the ordinary case that the local-first
// search rule of Food Catalog Subflow 6.3 exists for, so it is bounded rather than waited on.
var externalProviderTimeout = TimeSpan.FromSeconds(10);

// ---------------------------------------------------------------------------------------------
// 1. Lowercase routing
// ---------------------------------------------------------------------------------------------
builder.Services.AddRouting(options => options.LowercaseUrls = true);

// ---------------------------------------------------------------------------------------------
// 2. Localization
// ---------------------------------------------------------------------------------------------
builder.Services.AddLocalization();

// ---------------------------------------------------------------------------------------------
// 3. Controllers + kebab-case route convention + localized data annotations
// ---------------------------------------------------------------------------------------------
builder.Services
    .AddControllers(options => options.Conventions.Add(new KebabCaseRouteNamingConvention()))
    .AddDataAnnotationsLocalization();

// ---------------------------------------------------------------------------------------------
// 4. ProblemDetails (RFC 7807)
// ---------------------------------------------------------------------------------------------
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.ProblemDetails.Status is null or >= 500)
        {
            var localizer = context.HttpContext.RequestServices
                .GetRequiredService<IStringLocalizer<SharedResource>>();
            context.ProblemDetails.Title ??= localizer["UnexpectedServerError"].Value;
            context.ProblemDetails.Detail ??= localizer["UnexpectedErrorProcessingRequest"].Value;
        }

        // X-3: ValidationFailed for model validation, InternalError for 5xx; assembler problems already carry theirs.
        ProblemDetailsErrorCodes.ApplyDefault(context.ProblemDetails);
    };
});

// ---------------------------------------------------------------------------------------------
// 5. Swagger
// ---------------------------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Healthify Platform API",
        Version = "v1",
        Description =
            "Nutritional follow-up between consultations. Two asymmetric actors, Patient and Practitioner, " +
            "over six bounded contexts."
    });
    options.EnableAnnotations();

    // Several bounded contexts declare types with the same short name; the full name avoids collisions.
    options.CustomSchemaIds(type => type.FullName!.Replace("+", "."));

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Session token issued by the sign-in endpoint. It carries the immutable role claim."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), [] }
    });
});

// ---------------------------------------------------------------------------------------------
// 6. Database context
// ---------------------------------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var template = builder.Configuration.GetConnectionString("DefaultConnection")
                   ?? throw new InvalidOperationException("Database connection string is not set.");
    var connectionString = Environment.ExpandEnvironmentVariables(template);

    options.UseMySQL(connectionString)
        .UseLoggerFactory(sp.GetRequiredService<ILoggerFactory>())
        .EnableDetailedErrors();

    if (builder.Environment.IsDevelopment()) options.EnableSensitiveDataLogging();
});

// ---------------------------------------------------------------------------------------------
// 7. CORS
// ---------------------------------------------------------------------------------------------
const string frontendCorsPolicy = "FrontendPolicy";
builder.Services.AddCors(options => options.AddPolicy(frontendCorsPolicy, policy =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    // AllowCredentials is incompatible with AllowAnyOrigin, so the origins are always explicit.
    policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
}));

// ---------------------------------------------------------------------------------------------
// 8. JWT bearer authentication
// ---------------------------------------------------------------------------------------------
var tokenSettings = builder.Configuration.GetSection("TokenSettings");
var jwtSecret = tokenSettings["Secret"]
                ?? throw new InvalidOperationException("TokenSettings:Secret is not configured. Set JWT_SECRET.");
if (jwtSecret.Contains('%'))
    throw new InvalidOperationException(
        "TokenSettings:Secret contains an unsubstituted placeholder. Set JWT_SECRET.");
if (jwtSecret.Length < 32)
    throw new InvalidOperationException("TokenSettings:Secret must be at least 32 characters long.");

var jwtIssuer = tokenSettings["Issuer"] ?? "healthify-platform";
var jwtAudience = tokenSettings["Audience"] ?? "healthify-clients";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(2)
    };

    // Answer the challenge with a localized ProblemDetails instead of the default empty 401.
    options.Events = new JwtBearerEvents
    {
        OnChallenge = async context =>
        {
            context.HandleResponse();

            var localizer = context.HttpContext.RequestServices
                .GetRequiredService<IStringLocalizer<SharedResource>>();
            var problem = ProblemDetailsFactory.Create(
                StatusCodes.Status401Unauthorized,
                localizer["Unauthorized"].Value,
                localizer["AuthenticationRequired"].Value,
                context.Request.Path,
                ProblemDetailsErrorCodes.AuthenticationRequired);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem);
        }
    };
});

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------------------------
// 8b. Request rate limiting (D-RL). Policies "auth" (per IP), "ai-photo" (per user) and the global limiter (per
//     user, or per IP without a session); limits in the RateLimiting section, RateLimiting:Enabled switches it off.
//     The client IP is the connection's RemoteIpAddress: there is no UseForwardedHeaders yet, so behind a reverse
//     proxy every client shares the proxy IP.
// ---------------------------------------------------------------------------------------------
builder.Services.AddPlatformRateLimiting(builder.Configuration);

// ---------------------------------------------------------------------------------------------
// 9. Event bus (Cortex.Mediator scans this assembly for every IEventHandler)
// ---------------------------------------------------------------------------------------------
builder.Services.AddCortexMediator(
    [typeof(Program)],
    options => options.AddDefaultBehaviors());

