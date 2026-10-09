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

// ---------------------------------------------------------------------------------------------
// 10. Dependency injection, grouped by bounded context.
//     Order inside each block: repositories -> infrastructure domain services ->
//     command/query services -> ACL facade.
// ---------------------------------------------------------------------------------------------
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Shared AI module (IA-0). Technical only: no business rule and no reference to any context. Each AI function
// lives in the context that owns its data; the consent policy is implemented by CareRelationship (IA-1).
// Off unless Ai:Enabled is true (default in code). Gemini is the only provider; tests use
// FakeLanguageModelClient and never reach Google.
builder.Services.AddSingleton<IAiSettings, ConfiguredAiSettings>();
builder.Services.AddSingleton<IPromptCatalog>(PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly));
builder.Services.AddScoped<IAiGenerationLog, EfAiGenerationLog>();
builder.Services.AddScoped<IAiGenerationPipeline, AiGenerationPipeline>();
builder.Services.AddSingleton<IGoogleAccessTokenProvider, GoogleApplicationDefaultTokenProvider>();
// Each attempt is bounded by Ai:TimeoutSeconds inside the client, which also owns the single retry (5xx or
// timeout only); the HttpClient's own timeout is only an outer safety net.
builder.Services.AddHttpClient<ILanguageModelClient, GeminiLanguageModelClient>("Gemini", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Healthify-Platform/1.0");
});

// Iam Bounded Context
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserSessionRepository, UserSessionRepository>();
builder.Services.AddScoped<IHashingService, BCryptHashingService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<ISignInLockoutPolicy, ConfiguredSignInLockoutPolicy>();
builder.Services.AddScoped<IUserCommandService, UserCommandService>();
builder.Services.AddScoped<IUserSessionCommandService, UserSessionCommandService>();
builder.Services.AddScoped<IUserQueryService, UserQueryService>();
builder.Services.AddScoped<IUserSessionQueryService, UserSessionQueryService>();
builder.Services.AddScoped<IIamContextFacade, IamContextFacade>();

// CareRelationship Bounded Context
builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
builder.Services.AddScoped<ICareLinkRepository, CareLinkRepository>();
builder.Services.AddScoped<IAiPreferencesRepository, AiPreferencesRepository>();
builder.Services.AddScoped<IInvitationCommandService, InvitationCommandService>();
builder.Services.AddScoped<ICareLinkCommandService, CareLinkCommandService>();
builder.Services.AddScoped<IAiPreferencesCommandService, AiPreferencesCommandService>();
builder.Services.AddScoped<IInvitationQueryService, InvitationQueryService>();
builder.Services.AddScoped<ICareLinkQueryService, CareLinkQueryService>();
builder.Services.AddScoped<IAiPreferencesQueryService, AiPreferencesQueryService>();
builder.Services.AddScoped<ICareRelationshipContextFacade, CareRelationshipContextFacade>();
// IA-1: consent lives here, so this context answers the AI pipeline's consent question (port in Shared).
builder.Services.AddScoped<IAiConsentPolicy, CareRelationshipAiConsentPolicy>();

// NutritionalCare Bounded Context
builder.Services.AddScoped<INutritionalAssessmentRepository, NutritionalAssessmentRepository>();
builder.Services.AddScoped<INutritionalDiagnosisRepository, NutritionalDiagnosisRepository>();
builder.Services.AddScoped<INutritionPlanRepository, NutritionPlanRepository>();
builder.Services.AddScoped<IReviewItemRepository, ReviewItemRepository>();
builder.Services.AddScoped<IPatientBaselineRepository, PatientBaselineRepository>();
builder.Services.AddScoped<IConsultationRepository, ConsultationRepository>();
builder.Services.AddScoped<IBmrCalculator, BmrCalculator>();
builder.Services.AddScoped<IActivityFactorProvider, ConfiguredActivityFactorProvider>();
builder.Services.AddScoped<IDefaultTargetParametersPolicy, DefaultTargetParametersPolicy>();
builder.Services.AddScoped<IDefaultGuidelinesProvider, ConfiguredDefaultGuidelinesProvider>();
builder.Services.AddScoped<ICalorieFloorPolicy, ConfiguredCalorieFloorPolicy>();
builder.Services.AddSingleton<IPatientMessageLexicon>(EmbeddedPatientMessageLexicon.Instance);
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IClinicalDateProvider, ClinicalTimeZoneDateProvider>();
builder.Services.AddScoped<INutritionalAssessmentCommandService, NutritionalAssessmentCommandService>();
builder.Services.AddScoped<INutritionalDiagnosisCommandService, NutritionalDiagnosisCommandService>();
builder.Services.AddScoped<INutritionPlanCommandService, NutritionPlanCommandService>();
builder.Services.AddScoped<IReviewItemCommandService, ReviewItemCommandService>();
builder.Services.AddScoped<IPatientBaselineCommandService, PatientBaselineCommandService>();
builder.Services.AddScoped<IConsultationCommandService, ConsultationCommandService>();
builder.Services.AddScoped<IConsultationAiCommandService, ConsultationAiCommandService>();
builder.Services.AddScoped<IPlanAdjustmentProposer, PlanAdjustmentProposer>();
builder.Services.AddScoped<PlanAdjustmentInputReader>();
builder.Services.AddSingleton<IPlanProposalGenerationQueue, InMemoryPlanProposalGenerationQueue>();
builder.Services.AddScoped<IPlanProposalCommandService, PlanProposalCommandService>();
builder.Services.AddScoped<INutritionalAssessmentQueryService, NutritionalAssessmentQueryService>();
builder.Services.AddScoped<INutritionalDiagnosisQueryService, NutritionalDiagnosisQueryService>();
builder.Services.AddScoped<INutritionPlanQueryService, NutritionPlanQueryService>();
builder.Services.AddScoped<IReviewItemQueryService, ReviewItemQueryService>();
builder.Services.AddScoped<IPatientBaselineQueryService, PatientBaselineQueryService>();
builder.Services.AddScoped<IConsultationQueryService, ConsultationQueryService>();
builder.Services.AddScoped<INutritionalCareContextFacade, NutritionalCareContextFacade>();

// FoodCatalog Bounded Context
builder.Services.AddScoped<IReferenceFoodRepository, ReferenceFoodRepository>();
builder.Services.AddScoped<IReferenceFoodCommandService, ReferenceFoodCommandService>();
builder.Services.AddScoped<IReferenceFoodQueryService, ReferenceFoodQueryService>();
builder.Services.AddScoped<IFoodCatalogContextFacade, FoodCatalogContextFacade>();
builder.Services.AddScoped<ReferenceFoodSeeder>();

// IntakeBodyResponse Bounded Context
builder.Services.AddScoped<IActiveTargetsCacheRepository, ActiveTargetsCacheRepository>();
builder.Services.AddScoped<IDiaryEntryRepository, DiaryEntryRepository>();
builder.Services.AddScoped<ISelfWeighInRepository, SelfWeighInRepository>();
builder.Services.AddScoped<IWeightTrendRepository, WeightTrendRepository>();
builder.Services.AddScoped<IMealPhotoAnalysisRepository, MealPhotoAnalysisRepository>();
builder.Services.AddScoped<ISelfWeighInProtocolProvider, ConfiguredSelfWeighInProtocolProvider>();
// IN-7: the metadata of a meal photo is removed in memory before it reaches the AI.
builder.Services.AddSingleton<IPhotoMetadataStripper, PhotoMetadataStripper>();
// IA-3: the ingredient -> restriction map and the two-hour cache of meal ideas.
builder.Services.AddSingleton<IRestrictionLexicon>(EmbeddedRestrictionLexicon.Instance);
builder.Services.AddSingleton<IMealIdeasCache, InMemoryMealIdeasCache>();
// IN-7: the catalog names of the photo recognition prompt, one hour in memory.
builder.Services.AddSingleton<ICatalogNameHintsCache, InMemoryCatalogNameHintsCache>();
builder.Services.AddScoped<IActiveTargetsCacheCommandService, ActiveTargetsCacheCommandService>();
builder.Services.AddScoped<IDiaryEntryCommandService, DiaryEntryCommandService>();
builder.Services.AddScoped<ISelfWeighInCommandService, SelfWeighInCommandService>();
builder.Services.AddScoped<IWeightTrendCommandService, WeightTrendCommandService>();
builder.Services.AddScoped<IMealIdeasCommandService, MealIdeasCommandService>();
builder.Services.AddScoped<IMealPhotoAnalysisCommandService, MealPhotoAnalysisCommandService>();
builder.Services.AddScoped<IActiveTargetsCacheQueryService, ActiveTargetsCacheQueryService>();
builder.Services.AddScoped<IDiaryEntryQueryService, DiaryEntryQueryService>();
builder.Services.AddScoped<ISelfWeighInQueryService, SelfWeighInQueryService>();
builder.Services.AddScoped<IWeightTrendQueryService, WeightTrendQueryService>();
builder.Services.AddScoped<IIntakeContextFacade, IntakeContextFacade>();
builder.Services.AddScoped<WeightTrendRecalculationJob>();

// MonitoringAdherence Bounded Context
builder.Services.AddScoped<IEvaluationWindowRepository, EvaluationWindowRepository>();
builder.Services.AddScoped<IDeviationRepository, DeviationRepository>();
builder.Services.AddScoped<IConsistencyIndexRepository, ConsistencyIndexRepository>();
builder.Services.AddScoped<IReferralRepository, ReferralRepository>();
builder.Services.AddScoped<IScheduledFollowUpRepository, ScheduledFollowUpRepository>();
builder.Services.AddScoped<IPreVisitCheckInRepository, PreVisitCheckInRepository>();
builder.Services.AddScoped<IWeeklySummaryRepository, WeeklySummaryRepository>();
builder.Services.AddSingleton<IFollowUpCalendar, ClinicalTimeZoneFollowUpCalendar>();
// IA-2/IA-4/IA-5: the words a generated text must not contain, and the in-memory cache of IA-4 and IA-5.
builder.Services.AddSingleton<IAiLanguageLexicon>(EmbeddedAiLanguageLexicon.Instance);
builder.Services.AddSingleton<IMonitoringAiCache, InMemoryMonitoringAiCache>();
builder.Services.AddScoped<MonitoringFactsReader>();
builder.Services.AddScoped<IEvaluationWindowCommandService, EvaluationWindowCommandService>();
builder.Services.AddScoped<IDeviationCommandService, DeviationCommandService>();
builder.Services.AddScoped<IConsistencyIndexCommandService, ConsistencyIndexCommandService>();
builder.Services.AddScoped<IReferralCommandService, ReferralCommandService>();
builder.Services.AddScoped<IScheduledFollowUpCommandService, ScheduledFollowUpCommandService>();
builder.Services.AddScoped<IPreVisitCheckInCommandService, PreVisitCheckInCommandService>();
builder.Services.AddScoped<IWeeklySummaryCommandService, WeeklySummaryCommandService>();
builder.Services.AddScoped<ISuggestedQuestionsCommandService, SuggestedQuestionsCommandService>();
builder.Services.AddScoped<IMonitoringSummaryCommandService, MonitoringSummaryCommandService>();
builder.Services.AddScoped<IMonitoringAiContentCommandService, MonitoringAiContentCommandService>();
builder.Services.AddScoped<IEvaluationWindowQueryService, EvaluationWindowQueryService>();
builder.Services.AddScoped<IDeviationQueryService, DeviationQueryService>();
builder.Services.AddScoped<IConsistencyIndexQueryService, ConsistencyIndexQueryService>();
builder.Services.AddScoped<IReferralQueryService, ReferralQueryService>();
builder.Services.AddScoped<IScheduledFollowUpQueryService, ScheduledFollowUpQueryService>();
builder.Services.AddScoped<IPreVisitCheckInQueryService, PreVisitCheckInQueryService>();
builder.Services.AddScoped<IWeeklySummaryQueryService, WeeklySummaryQueryService>();
builder.Services.AddScoped<IMonitoringContextFacade, MonitoringContextFacade>();

// ReadModels (composition layer, ACL facades only)
// Not a bounded context: no repository, no command service, no error enum and no table. The
// composers reach nothing but the ACL contracts of the five contexts they read.
builder.Services.AddScoped<PatientRecordComposer>();
builder.Services.AddScoped<PatientMonitoringPanelComposer>();
builder.Services.AddScoped<PatientSummaryComposer>();
builder.Services.AddScoped<PatientRosterComposer>();
builder.Services.AddScoped<PatientConsultationsComposer>();

// Typed HttpClients (FoodCatalog only). Both implementations are registered against the same
// contract on purpose: Import Catalog Snapshot consults every provider it is handed, so adding a
// third one is a registration and nothing else.
// Each registration is given an explicit name. Without one, the name is derived from the service
// type, both providers end up sharing a single configured client, and the second base address
// silently wins for both.
builder.Services.AddHttpClient<IExternalFoodCatalogProvider, OpenFoodFactsProvider>("OpenFoodFacts",
    client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OpenFoodFacts:BaseUrl"]
                                 ?? "https://world.openfoodfacts.org");
    client.Timeout = externalProviderTimeout;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Healthify-Platform/1.0");
});
builder.Services.AddHttpClient<IExternalFoodCatalogProvider, UsdaFoodDataProvider>("Usda", client =>
{
    // The trailing slash keeps the /fdc/v1 path when relative request paths are resolved against it.
    var usdaBaseUrl = builder.Configuration["Usda:BaseUrl"] is { Length: > 0 } configured
        ? configured
        : "https://api.nal.usda.gov/fdc/v1";
    client.BaseAddress = new Uri(usdaBaseUrl.TrimEnd('/') + "/");
    client.Timeout = externalProviderTimeout;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Healthify-Platform/1.0");
});

// ---------------------------------------------------------------------------------------------
// 11. Hosted services: the event storming policies that the passage of time triggers.
// ---------------------------------------------------------------------------------------------
// Policy "When Expiration Date Reached" - CareRelationship, Subflow 2.1
builder.Services.AddHostedService<InvitationExpiryHostedService>();

// Policy "When Scheduled Import Due" - FoodCatalog, Subflow 6.1
builder.Services.AddHostedService<CatalogImportHostedService>();

// Policy "When N Days Without Diary Entry" - MonitoringAdherence, Subflow 5.9
builder.Services.AddHostedService<LoggingGapDetectionHostedService>();

// Policy "When Consistency Alert Sustained Three Weeks" - MonitoringAdherence, Subflow 5.8
builder.Services.AddHostedService<ConsistencyEscalationHostedService>();

// Policy "When Scheduled Date Passed Without Visit" - MonitoringAdherence, Subflow 5.10
builder.Services.AddHostedService<MissedFollowUpHostedService>();

// Policy "When The Week Ends" (IA-2) - MonitoringAdherence: weekly summaries, Ai:Features:WeeklySummary:Cron
builder.Services.AddHostedService<WeeklySummaryHostedService>();

// Retention of ai_generations (IA-0, §12-#14) - Shared AI module
builder.Services.AddHostedService<AiGenerationPurgeHostedService>();

// Policy "When A Photo Analysis Expires" (IN-7) - IntakeBodyResponse: the 24-hour meal photo analyses
builder.Services.AddHostedService<MealPhotoAnalysisPurgeHostedService>();

// NC-10: the AI plan proposals queued by the sustained deviation policy, and the scheduled rechecks.
builder.Services.AddHostedService<PlanProposalGenerationHostedService>();
builder.Services.AddHostedService<ReviewItemRecheckHostedService>();
// NC-10: the queue is in memory; at start-up and every N minutes the lost proposals are queued again.
builder.Services.AddHostedService<PlanProposalRecoveryHostedService>();

var app = builder.Build();

// ---------------------------------------------------------------------------------------------
// 12a. One-shot maintenance, only when asked for on the command line (IN-3):
//      dotnet Healthify.Platform.dll recalculate-weight-trends
//      It does not migrate and does not serve: it refuses to run while migrations are pending, runs
//      Recalculate Weight Trend once per patient with readings, and exits.
// ---------------------------------------------------------------------------------------------
if (args.Contains(WeightTrendRecalculationJob.CommandLineVerb))
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
    if (pending.Count > 0)
    {
        app.Logger.LogError("Pending migrations ({Migrations}); start the API once to apply them first",
            string.Join(", ", pending));
        Environment.ExitCode = 1;
        return;
    }

    var report = await scope.ServiceProvider.GetRequiredService<WeightTrendRecalculationJob>().RunAsync();
    app.Logger.LogInformation("Weight trends recalculated: {Recalculated} of {Patients}",
        report.Recalculated, report.Patients);
    Environment.ExitCode = report.FailedPatientIds.Count == 0 ? 0 : 1;
    return;
}

// ---------------------------------------------------------------------------------------------
// 12. Migrations and reference data seeding
// ---------------------------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.Migrate();

    // Idempotent and gated behind Seeder:Enabled. It writes through Cache Food Locally, so seeded
    // rows enter the catalog by the same single door as imported ones.
    var referenceFoodSeeder = scope.ServiceProvider.GetRequiredService<ReferenceFoodSeeder>();
    await referenceFoodSeeder.SeedAsync();
}

// ---------------------------------------------------------------------------------------------
// 13. HTTP pipeline. The order below is significant.
// ---------------------------------------------------------------------------------------------
string[] supportedCultures = ["en", "en-US", "es", "es-PE"];
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
localizationOptions.ApplyCurrentCultureToResponseHeaders = true;
// IAM-3: last provider, so query string, cookie and Accept-Language keep precedence; it answers only when
// the request carries no Accept-Language, from the lang claim of the session token.
localizationOptions.RequestCultureProviders.Add(new LanguageClaimRequestCultureProvider());

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Healthify Platform API v1");
    options.DocumentTitle = "Healthify Platform API";
});
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseCors(frontendCorsPolicy); // before authentication, so preflight and Authorization survive
app.UseAuthentication();
// IAM-3: after authentication, so the lang claim is readable; before authorization, so the 401/403
// problem details and every controller already run in the request culture.
app.UseRequestLocalization(localizationOptions);
// D-RL: after authentication (user partitions) and localization (429 texts); before authorization, so an anonymous
// flood is counted by IP before the 401.
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>
///     Assembly marker used by the Cortex.Mediator scanner. Declared explicitly so that the
///     top-level program class is reachable from the registration call above.
/// </summary>
public partial class Program;
