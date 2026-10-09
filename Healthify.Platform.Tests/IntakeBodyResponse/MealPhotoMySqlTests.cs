using System.Data.Common;
using Cortex.Mediator;
using Healthify.Platform.FoodCatalog.Application.Acl;
using Healthify.Platform.FoodCatalog.Application.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal.QueryServices;
using Healthify.Platform.FoodCatalog.Application.QueryServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.Errors;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Repositories;
using Healthify.Platform.FoodCatalog.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     IN-7 against a real MySQL server: the four migrations run down and up again over existing rows (foods become
///     Imported or LocalOverride and verified, preferences stay off), two requests creating the same AI-estimated
///     dish at the same instant leave one row (the unique index on source_hash decides), the analysis round-trips
///     without any image, one analysis is logged once, and a resent clientEntryId does not duplicate.
/// </summary>
/// <remarks>Skipped unless <c>HEALTHIFY_IT_MYSQL</c> is set (CLAUDE.md §5).</remarks>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class MealPhotoMySqlTests
{
    private const int PatientId = 10;
    private const string BeforeIn7 = "20261007033438_NutritionalCare_ReviewItemResolutionNoteCodes";
    private static readonly DateOnly Today = new(2026, 9, 15);

    [MySqlFact]
    public async Task The_migrations_backfill_existing_rows_and_run_down_and_up_again()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await using (var context = db.NewContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeIn7);
            Assert.Equal(0L, await Scalar(context,
                "SELECT COUNT(*) AS Value FROM information_schema.columns WHERE table_schema = DATABASE() AND " +
                "column_name IN ('input_image_hash', 'meal_photo_recognition_enabled', 'is_verified', 'verified_by', " +
                "'meal_photo_analysis_id', 'proposed_ai_generation_id')"));

            // Rows as they are before IN-7.
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO reference_foods (local_name, energy_kcal_per_100g, protein_g_per_100g, carb_g_per_100g, " +
                "fat_g_per_100g, source_hash, is_local_override) VALUES " +
                "('Arroz blanco cocido', 130, 2.7, 28.2, 0.3, REPEAT('a', 64), 0), " +
                "('Cuy al horno', 155, 21, 0, 7.8, REPEAT('b', 64), 1)");
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO ai_preferences (patient_id, weekly_summary_enabled, meal_ideas_enabled, " +
                "suggested_questions_enabled) VALUES (10, 1, 1, 1)");

            await migrator.MigrateAsync();
        }

        await using (var context = db.NewContext())
        {
            Assert.Equal(1L, await Scalar(context,
                "SELECT COUNT(*) AS Value FROM reference_foods WHERE local_name = 'Arroz blanco cocido' " +
                "AND source = 'Imported' AND is_verified = 1 AND verified_by IS NULL AND ai_generation_id IS NULL"));
            Assert.Equal(1L, await Scalar(context,
                "SELECT COUNT(*) AS Value FROM reference_foods WHERE local_name = 'Cuy al horno' " +
                "AND source = 'LocalOverride' AND is_verified = 1"));
            Assert.Equal(1L, await Scalar(context,
                "SELECT COUNT(*) AS Value FROM ai_preferences WHERE patient_id = 10 " +
                "AND meal_photo_recognition_enabled = 0 AND meal_ideas_enabled = 1"));
            Assert.Equal(0L, await Scalar(context,
                "SELECT COUNT(*) AS Value FROM information_schema.columns WHERE table_schema = DATABASE() " +
                "AND table_name = 'meal_photo_analyses' AND data_type IN ('blob', 'longblob', 'mediumblob', " +
                "'binary', 'varbinary')"));

            // The EF model reads them back as the domain expects.
            var foods = await context.Set<ReferenceFood>().OrderBy(f => f.LocalNameText).ToListAsync();
            Assert.Equal([FoodSource.Imported, FoodSource.LocalOverride], foods.Select(f => f.Source.Value));
        }
    }

    [MySqlFact]
    public async Task Two_requests_creating_the_same_dish_at_once_leave_one_food()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        var barrier = new InsertBarrier("INSERT INTO `reference_foods`", 2);
        await using var services = Services(db, barrier);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IReferenceFoodCommandService>()
                .Handle(new CreateAiEstimatedFoodCommand("Ají de gallina", 150m, 10m, 12m, 7m, 77));
        })));

        Assert.True(barrier.Released, "Both inserts reached the database together.");
        var ids = results.Select(r => Assert.IsType<Result<ReferenceFood, FoodCatalogError>.Success>(r).Value.Id.Value)
            .ToList();
        Assert.Single(ids.Distinct());
        await using var context = db.NewContext();
        Assert.Equal(1L, await Scalar(context,
            "SELECT COUNT(*) AS Value FROM reference_foods WHERE source = 'AiEstimated' AND is_verified = 1 " +
            "AND verified_by IS NULL AND ai_generation_id = 77"));
    }

    [MySqlFact]
    public async Task An_analysis_round_trips_is_logged_once_and_a_resent_log_does_not_duplicate()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await using var services = Services(db, null);
        int foodId;
        await using (var scope = services.CreateAsyncScope())
        {
            var facade = scope.ServiceProvider.GetRequiredService<IFoodCatalogContextFacade>();
            foodId = (await facade.CreateAiEstimatedFood("Seco de res", new FoodNutrientsItem(150m, 10m, 12m, 7m), 5))!
                .ReferenceFoodId;
        }

        var analysis = new MealPhotoAnalysis(PatientId, foodId, 320m, new Confidence(0.82m),
            [new MealPhotoAlternative("Tallarín saltado", 300m, null), new MealPhotoAlternative("Seco de res", 250m, foodId)],
            5, DateTimeOffset.UtcNow.AddHours(24));
        var expired = new MealPhotoAnalysis(PatientId, foodId, 200m, new Confidence(0.5m), [], 6,
            DateTimeOffset.UtcNow.AddMinutes(-5));
        await using (var scope = services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IMealPhotoAnalysisRepository>();
            await repository.AddAsync(analysis);
            await repository.AddAsync(expired);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var read = await scope.ServiceProvider.GetRequiredService<IMealPhotoAnalysisRepository>()
                .FindByIdAsync(analysis.Id);
            Assert.NotNull(read);
            Assert.Equal((foodId, 320m, 0.82m, 5L), (read.ReferenceFoodId, read.EstimatedGrams, read.Confidence,
                read.AiGenerationId));
            Assert.Equal([("Tallarín saltado", 300m, (int?)null), ("Seco de res", 250m, foodId)],
                read.Alternatives.Select(a => (a.Name, a.Grams, a.ReferenceFoodId)));
        }

        var clientEntryId = Guid.NewGuid();
        var log = new LogMealByPhotoCommand(PatientId, DateTimeOffset.UtcNow.AddHours(-1), null, 0, 0m, 0m,
            new PhotoConfirmation(PhotoConfirmation.Adjusted, foodId, 300m), "InPlan", analysis.Id, clientEntryId);
        var first = await Log(services, log);
        var resent = await Log(services, log);
        var again = await Log(services, log with { ClientEntryId = Guid.NewGuid() }); // same analysis, new id

        Assert.Equal(first.Id.Value, resent.Id.Value);
        Assert.Equal(first.Id.Value, again.Id.Value);
        await using (var context = db.NewContext())
        {
            Assert.Equal(1L, await Scalar(context,
                $"SELECT COUNT(*) AS Value FROM diary_entries WHERE meal_photo_analysis_id = '{analysis.Id}' " +
                "AND proposed_reference_food_id = " + foodId + " AND proposed_portion_grams = 320 " +
                "AND confirmed_portion_grams = 300 AND proposed_ai_generation_id = 5"));
        }

        // The unique index itself refuses a second entry for the analysis.
        await using (var context = db.NewContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
                "INSERT INTO diary_entries (patient_id, local_timestamp, local_utc_offset_minutes, provenance, " +
                "plan_adherence, sync_state, meal_photo_analysis_id) VALUES (10, NOW(), 0, 'Photo', 'NotAnswered', " +
                "'Synced', {0})", analysis.Id.ToString()));
        }

        // Purges: the expired one by the worker's command, the rest with the consent.
        await using (var scope = services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IMealPhotoAnalysisRepository>();
            Assert.Equal(1, await repository.DeleteExpiredAsync(DateTimeOffset.UtcNow));
            Assert.Equal(1, await repository.DeleteByPatientIdAsync(PatientId));
        }

        await using (var context = db.NewContext())
        {
            Assert.Equal(0L, await Scalar(context, "SELECT COUNT(*) AS Value FROM meal_photo_analyses"));
            // The food created by the AI stays: it is shared catalog.
            Assert.Equal(1L, await Scalar(context,
                "SELECT COUNT(*) AS Value FROM reference_foods WHERE source = 'AiEstimated'"));
        }
    }

    [MySqlFact]
    public async Task The_catalog_hints_are_the_foods_most_logged_by_at_least_three_patients()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await using (var context = db.NewContext())
        {
            // Food 7: three patients, three logs. Food 8: two patients, six logs (more, but too few people).
            // Food 9: four patients, four logs. Food 10: only proposed, never confirmed.
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO diary_entries (patient_id, local_timestamp, local_utc_offset_minutes, provenance, " +
                "plan_adherence, sync_state, confirmed_reference_food_id, confirmed_portion_grams, " +
                "proposed_reference_food_id) VALUES " +
                "(1, NOW(), 0, 'Manual', 'InPlan', 'Synced', 7, 100, NULL), (2, NOW(), 0, 'Manual', 'InPlan', 'Synced', 7, 100, NULL), " +
                "(3, NOW(), 0, 'Manual', 'InPlan', 'Synced', 7, 100, NULL), " +
                "(1, NOW(), 0, 'Manual', 'InPlan', 'Synced', 8, 100, NULL), (1, NOW(), 0, 'Manual', 'InPlan', 'Synced', 8, 100, NULL), " +
                "(1, NOW(), 0, 'Manual', 'InPlan', 'Synced', 8, 100, NULL), (2, NOW(), 0, 'Manual', 'InPlan', 'Synced', 8, 100, NULL), " +
                "(2, NOW(), 0, 'Manual', 'InPlan', 'Synced', 8, 100, NULL), (2, NOW(), 0, 'Manual', 'InPlan', 'Synced', 8, 100, NULL), " +
                "(1, NOW(), 0, 'Manual', 'InPlan', 'Synced', 9, 100, NULL), (2, NOW(), 0, 'Manual', 'InPlan', 'Synced', 9, 100, NULL), " +
                "(3, NOW(), 0, 'Manual', 'InPlan', 'Synced', 9, 100, NULL), (4, NOW(), 0, 'Manual', 'InPlan', 'Synced', 9, 100, NULL), " +
                "(1, NOW(), 0, 'Photo', 'NotAnswered', 'Synced', NULL, NULL, 10), (2, NOW(), 0, 'Photo', 'NotAnswered', 'Synced', NULL, NULL, 10), " +
                "(3, NOW(), 0, 'Photo', 'NotAnswered', 'Synced', NULL, NULL, 10)");
        }

        await using var services = Services(db, null);
        await using var scope = services.CreateAsyncScope();
        var ids = await scope.ServiceProvider.GetRequiredService<IDiaryEntryRepository>()
            .ListMostLoggedReferenceFoodIdsAsync(3, 10);

        Assert.Equal([9, 7], ids);
    }

    [MySqlFact]
    public async Task Verified_foods_and_foods_by_ids_are_read_in_one_query_each()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await using (var context = db.NewContext())
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO reference_foods (id, local_name, energy_kcal_per_100g, protein_g_per_100g, carb_g_per_100g, " +
                "fat_g_per_100g, source_hash, is_local_override, source, is_verified) VALUES " +
                "(1, 'Ceviche', 110, 18, 6, 1.5, REPEAT('a', 64), 0, 'Imported', 1), " +
                "(2, 'Cuy al horno', 155, 21, 0, 7.8, REPEAT('b', 64), 1, 'LocalOverride', 1), " +
                "(3, 'Arroz blanco cocido', 130, 2.7, 28.2, 0.3, REPEAT('c', 64), 0, 'Imported', 1), " +
                "(4, 'Sin verificar', 100, 5, 10, 4, REPEAT('d', 64), 0, 'Imported', 0)");

        await using var services = Services(db, null);
        await using var scope = services.CreateAsyncScope();
        var facade = scope.ServiceProvider.GetRequiredService<IFoodCatalogContextFacade>();

        Assert.Equal(["Cuy al horno", "Ceviche"], (await facade.ListVerifiedFoods(2, [3])).Select(f => f.LocalName));
        Assert.Equal([1, 3], (await facade.GetReferenceFoodsByIds([3, 1, 99])).Select(f => f.ReferenceFoodId).Order());
    }

    private static async Task<DiaryEntry> Log(ServiceProvider services, LogMealByPhotoCommand command)
    {
        await using var scope = services.CreateAsyncScope();
        var service = ActivatorUtilities.CreateInstance<DiaryEntryCommandService>(scope.ServiceProvider);
        var result = await service.Handle(command);
        return Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result).Value;
    }

    private static ServiceProvider Services(MySqlIntegrationDatabase db, InsertBarrier? barrier)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseMySQL(db.ConnectionString);
            if (barrier is not null) options.AddInterceptors(barrier);
        });
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton(Substitute.For<IMediator>());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IReferenceFoodRepository, ReferenceFoodRepository>();
        services.AddScoped<IReferenceFoodCommandService, ReferenceFoodCommandService>();
        services.AddScoped<IReferenceFoodQueryService, ReferenceFoodQueryService>();
        services.AddScoped<IFoodCatalogContextFacade, FoodCatalogContextFacade>();
        services.AddScoped<IDiaryEntryRepository, DiaryEntryRepository>();
        services.AddScoped<IMealPhotoAnalysisRepository, MealPhotoAnalysisRepository>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<long> Scalar(AppDbContext context, string sql)
    {
        return await context.Database.SqlQueryRaw<long>(sql).SingleAsync();
    }

    /// <summary>Holds each matching insert until <c>count</c> of them are about to run, so they truly race.</summary>
    private sealed class InsertBarrier(string sql, int count) : DbCommandInterceptor
    {
        private readonly Barrier _barrier = new(count);

        public bool Released { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Wait(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Wait(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Wait(DbCommand command)
        {
            if (!command.CommandText.Contains(sql, StringComparison.OrdinalIgnoreCase)) return;
            Released = _barrier.SignalAndWait(TimeSpan.FromSeconds(30)) || Released;
        }
    }
}
