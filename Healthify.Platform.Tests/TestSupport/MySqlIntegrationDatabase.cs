using System.Data.Common;
using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.EventHandlers;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.NutritionalCare.Infrastructure.Calculators;
using Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Tests.NutritionalCare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using NSubstitute;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     A <c>[Fact]</c> that runs only when <see cref="MySqlIntegrationDatabase.ConnectionVariable" /> holds a
///     MySQL connection string; otherwise the test is reported as skipped, never as failed.
/// </summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MySqlIntegrationDatabase.ConnectionVariable)))
            Skip = $"Set {MySqlIntegrationDatabase.ConnectionVariable} to a MySQL server connection string to run it.";
    }
}

/// <summary>
///     A throw-away MySQL database for one test: <c>healthify_it_&lt;guid&gt;</c> on the server named by
///     <see cref="ConnectionVariable" />, with every migration applied, and dropped on dispose even when the test
///     fails. It refuses any other database name, so the development database is never touched.
/// </summary>
/// <remarks>
///     The services are the real ones (EF Core repositories, unit of work, command services, the publication
///     policy and the Intake cache); only the Iam and Care Relationship facades and the mediator are fakes, and
///     the mediator delivers the publication fan-out to the real handlers, each in its own DI scope as in
///     production.
/// </remarks>
public sealed class MySqlIntegrationDatabase : IAsyncDisposable
{
    public const string ConnectionVariable = "HEALTHIFY_IT_MYSQL";
    private const string DatabasePrefix = "healthify_it_";

    private readonly ServiceProvider _provider;

    private MySqlIntegrationDatabase(string connectionString, DateOnly today)
    {
        ConnectionString = connectionString;
        Iam.IsPractitioner(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        CareRelationship.IsCareLinkActive(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        CareRelationship.GetActiveCareLinkByPatientId(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new CareLinkStatusItem(1, call.ArgAt<int>(0), 20, true, true, null));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySQL(connectionString).AddInterceptors(Failure));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IConsultationRepository, ConsultationRepository>();
        services.AddScoped<IPatientBaselineRepository, PatientBaselineRepository>();
        services.AddScoped<INutritionalAssessmentRepository, NutritionalAssessmentRepository>();
        services.AddScoped<INutritionalDiagnosisRepository, NutritionalDiagnosisRepository>();
        services.AddScoped<INutritionPlanRepository, NutritionPlanRepository>();
        services.AddScoped<IActiveTargetsCacheRepository, ActiveTargetsCacheRepository>();
        services.AddSingleton<IClinicalDateProvider>(new FixedClinicalDate(today));
        services.AddSingleton<IBmrCalculator, BmrCalculator>();
        services.AddSingleton<IDefaultTargetParametersPolicy>(DefaultTargetParametersPolicyTests.Policy());
        services.AddSingleton(Iam);
        services.AddSingleton(CareRelationship);
        services.AddSingleton(Mediator);
        services.AddScoped<IConsultationCommandService, ConsultationCommandService>();
        services.AddScoped<INutritionPlanCommandService, NutritionPlanCommandService>();
        services.AddScoped<IActiveTargetsCacheCommandService, ActiveTargetsCacheCommandService>();
        // NC-10: the review inbox and the acceptance of a plan proposal.
        services.AddScoped<IReviewItemRepository, ReviewItemRepository>();
        services.AddSingleton(Substitute.For<IMonitoringContextFacade>());
        services.AddSingleton(Substitute.For<IIntakeContextFacade>());
        services.AddSingleton<ICalorieFloorPolicy>(new ConfiguredCalorieFloorPolicy(new ConfigurationBuilder().Build()));
        services.AddScoped<PlanAdjustmentInputReader>();
        services.AddScoped<IPlanProposalCommandService, PlanProposalCommandService>();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var scopes = _provider.GetRequiredService<IServiceScopeFactory>();
        Fakes.Route(Mediator, new OnNutritionPlanPublishedHandler(scopes,
            NullLogger<OnNutritionPlanPublishedHandler>.Instance));
        Fakes.Route(Mediator, new OnNutritionPlanAdjustedHandler(scopes,
            NullLogger<OnNutritionPlanAdjustedHandler>.Instance));
        Fakes.Route(Mediator, new OnActiveTargetsUpdatedIntakeHandler(scopes,
            NullLogger<OnActiveTargetsUpdatedIntakeHandler>.Instance));
    }

    public string ConnectionString { get; }
    public IIamContextFacade Iam { get; } = Substitute.For<IIamContextFacade>();
    public ICareRelationshipContextFacade CareRelationship { get; } = Substitute.For<ICareRelationshipContextFacade>();
    public IMediator Mediator { get; } = Substitute.For<IMediator>();

    /// <summary>Makes a chosen SQL statement fail, to prove what a transaction rolls back.</summary>
    public FailingCommandInterceptor Failure { get; } = new();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var context = NewContext();
            await context.Database.EnsureDeletedAsync();
        }
        finally
        {
            await _provider.DisposeAsync();
        }
    }

    /// <summary>Creates the database and applies every migration. The caller disposes it in a finally.</summary>
    public static async Task<MySqlIntegrationDatabase> CreateAsync(DateOnly today)
    {
        var server = Environment.GetEnvironmentVariable(ConnectionVariable)
                     ?? throw new InvalidOperationException($"{ConnectionVariable} is not set.");
        var builder = new MySqlConnectionStringBuilder(server) { Database = DatabasePrefix + Guid.NewGuid().ToString("N") };
        // Never the development database, whatever the variable says.
        if (!builder.Database.StartsWith(DatabasePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Integration tests only use databases named healthify_it_*.");

        var database = new MySqlIntegrationDatabase(builder.ConnectionString, today);
        try
        {
            await using var context = database.NewContext();
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    /// <summary>The consultation endpoints: every call in its own request scope.</summary>
    public IConsultationCommandService Consultations => new PerRequestConsultationService(_provider);

    /// <summary>A request scope, as the API opens one per HTTP request.</summary>
    public AsyncServiceScope NewScope()
    {
        return _provider.CreateAsyncScope();
    }

    /// <summary>A fresh context that reads what the database holds, not what a scope tracks.</summary>
    public AppDbContext NewContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseMySQL(ConnectionString).Options);
    }
}

/// <summary>Fails every command whose SQL contains <see cref="FailOn" /> while it is set.</summary>
public sealed class FailingCommandInterceptor : DbCommandInterceptor
{
    public string? FailOn { get; set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Check(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Check(DbCommand command)
    {
        if (FailOn is not null && command.CommandText.Contains(FailOn, StringComparison.OrdinalIgnoreCase))
            throw new DbUpdateException($"Simulated database failure of: {FailOn}");
    }
}

/// <summary>
///     Every class with <see cref="MySqlFactAttribute" /> tests joins this collection, which runs alone and in
///     sequence. EF Core serialises migrations with the server-wide <c>GET_LOCK('__EFMigrationsLock')</c>, and
///     MySql.Data runs its async calls synchronously: several classes migrating in parallel blocked every xUnit
///     worker thread on that lock while the holder waited for a thread to continue, and the run never ended.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MySqlCollection
{
    public const string Name = "MySql";
}
