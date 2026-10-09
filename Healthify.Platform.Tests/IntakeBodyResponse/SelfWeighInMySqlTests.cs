using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Maintenance;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     IN-3, IN-4 and IN-5 against a real MySQL (Category=MySql; skipped without HEALTHIFY_IT_MYSQL): nullable
///     protocol columns, the per-patient unique client identifier, a lost race on that index, the one-shot
///     recalculation and the range summary.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class SelfWeighInMySqlTests
{
    private const int PatientId = 10;

    [MySqlFact]
    public async Task Readings_round_trip_with_and_without_the_removed_questions()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        await using (var context = db.NewContext())
        {
            context.Add(Reading(DateTimeOffset.UtcNow.AddDays(-2), new ProtocolCompliance(true, true, false)));
            context.Add(Reading(DateTimeOffset.UtcNow.AddDays(-1), new ProtocolCompliance(true)));
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            var readings = (await new SelfWeighInRepository(context).ListByPatientIdAsync(PatientId)).ToList();
            Assert.Equal((true, (bool?)true, (bool?)false),
                (readings[0].ProtocolFastedState, readings[0].ProtocolSameTimeOfDay, readings[0].ProtocolSameScale));
            Assert.Null(readings[1].ProtocolSameTimeOfDay);
            Assert.Null(readings[1].ProtocolSameScale);
            Assert.Null(readings[1].ClientEntryId);
            Assert.All(readings, r => Assert.True(r.FollowsProtocol));
        }
    }

    [MySqlFact]
    public async Task The_client_identifier_is_unique_per_patient_and_any_number_of_online_readings_have_none()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        var clientId = Guid.NewGuid();
        await using (var context = db.NewContext())
        {
            context.Add(Reading(DateTimeOffset.UtcNow.AddHours(-3), new ProtocolCompliance(true), clientId));
            context.Add(Reading(DateTimeOffset.UtcNow.AddHours(-3), new ProtocolCompliance(true), clientId, 11));
            context.Add(Reading(DateTimeOffset.UtcNow.AddHours(-2), new ProtocolCompliance(true)));
            context.Add(Reading(DateTimeOffset.UtcNow.AddHours(-1), new ProtocolCompliance(true)));
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            context.Add(Reading(DateTimeOffset.UtcNow.AddHours(-1), new ProtocolCompliance(true), clientId));
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [MySqlFact]
    public async Task A_batch_with_a_duplicate_and_a_resend_stores_each_reading_once()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var moment = DateTimeOffset.UtcNow.AddDays(-3);

        var batch = await Sync(db, new PendingSelfWeighIn(first, 80m, moment, true),
            new PendingSelfWeighIn(first, 80m, moment, true), new PendingSelfWeighIn(second, 79.8m, moment.AddDays(1), true));
        var resend = await Sync(db, new PendingSelfWeighIn(second, 79.8m, moment.AddDays(1), true));

        Assert.Equal((2, 1), (batch.Created, batch.AlreadyPresent));
        Assert.Equal(SyncedEntryOutcome.AlreadyPresent, Assert.Single(resend.Entries).Outcome);
        await using var context = db.NewContext();
        Assert.Equal(2, await context.Set<SelfWeighIn>().CountAsync());
    }

    [MySqlFact]
    public async Task Losing_the_race_on_the_unique_index_reports_already_present_and_the_batch_goes_on()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        var raced = Guid.NewGuid();
        var moment = DateTimeOffset.UtcNow.AddHours(-5);
        await using (var context = db.NewContext())
        {
            context.Add(Reading(moment, new ProtocolCompliance(true), raced));
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            // The other request committed between this one's lookup and its insert.
            var repository = new FirstLookupMisses(new SelfWeighInRepository(context));
            var service = Service(repository, new UnitOfWork(context));

            var result = await service.Handle(new SyncSelfWeighInsCommand(PatientId,
            [
                new PendingSelfWeighIn(raced, 80m, moment, true),
                new PendingSelfWeighIn(Guid.NewGuid(), 79.9m, moment.AddHours(1), true)
            ]));

            var outcome = Assert.IsType<Result<SelfWeighInSyncOutcome, IntakeError>.Success>(result).Value;
            Assert.Equal([SyncedEntryOutcome.AlreadyPresent, SyncedEntryOutcome.Created],
                outcome.Entries.Select(e => e.Outcome));
        }

        await using (var context = db.NewContext())
            Assert.Equal(2, await context.Set<SelfWeighIn>().CountAsync());
    }

    [MySqlFact]
    public async Task The_one_shot_job_rebuilds_a_trend_computed_under_the_old_protocol()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        var today = DateTimeOffset.UtcNow;
        await using (var context = db.NewContext())
        {
            // Fasted on another scale: excluded before IN-3, in the line after it.
            context.Add(Reading(today.AddDays(-14), new ProtocolCompliance(true, true, false)));
            context.Add(Reading(today.AddDays(-7), new ProtocolCompliance(true, true, true)));
            context.Add(Reading(today.AddDays(-1), new ProtocolCompliance(false, true, true)));
            var stale = new WeightTrend(PatientId, 1);
            context.Add(stale);
            await context.SaveChangesAsync();
        }

        await using var provider = Services(db);
        var report = await new WeightTrendRecalculationJob(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WeightTrendRecalculationJob>.Instance).RunAsync();

        Assert.Equal((1, 1), (report.Patients, report.Recalculated));
        await using var scope = provider.CreateAsyncScope();
        var view = await scope.ServiceProvider.GetRequiredService<IWeightTrendQueryService>()
            .Handle(new GetWeightTrendRangeByPatientIdQuery(PatientId));
        Assert.NotNull(view);
        Assert.Equal(2, view.Trend.Points.Count);
        Assert.Equal(1, view.Range.ExcludedReadingsCount);
        Assert.Equal(2, view.Range.Summary.PointCount);
    }

    private static async Task<SelfWeighInSyncOutcome> Sync(MySqlIntegrationDatabase db,
        params PendingSelfWeighIn[] entries)
    {
        await using var context = db.NewContext();
        var service = Service(new SelfWeighInRepository(context), new UnitOfWork(context));
        var result = await service.Handle(new SyncSelfWeighInsCommand(PatientId, entries));
        return Assert.IsType<Result<SelfWeighInSyncOutcome, IntakeError>.Success>(result).Value;
    }

    private static SelfWeighInCommandService Service(ISelfWeighInRepository repository, IUnitOfWork unitOfWork)
    {
        var protocol = Substitute.For<ISelfWeighInProtocolProvider>();
        protocol.Current.Returns(SelfWeighInProtocol.Default);
        return new SelfWeighInCommandService(repository, protocol, unitOfWork,
            NullLogger<SelfWeighInCommandService>.Instance, Substitute.For<IMediator>());
    }

    private static ServiceProvider Services(MySqlIntegrationDatabase db)
    {
        var protocol = Substitute.For<ISelfWeighInProtocolProvider>();
        protocol.Current.Returns(SelfWeighInProtocol.Default);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseMySQL(db.ConnectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ISelfWeighInRepository, SelfWeighInRepository>();
        services.AddScoped<IWeightTrendRepository, WeightTrendRepository>();
        services.AddSingleton(protocol);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IMediator>());
        services.AddScoped<ISelfWeighInQueryService, SelfWeighInQueryService>();
        services.AddScoped<IWeightTrendQueryService, WeightTrendQueryService>();
        services.AddScoped<IWeightTrendCommandService, WeightTrendCommandService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static SelfWeighIn Reading(DateTimeOffset moment, ProtocolCompliance protocol, Guid? clientId = null,
        int patientId = PatientId)
    {
        return new SelfWeighIn(patientId, new WeightKg(80m), new LocalTimestamp(moment), protocol, clientId);
    }

    /// <summary>The real repository, except that the first client identifier lookup misses, as in a race.</summary>
    private sealed class FirstLookupMisses(ISelfWeighInRepository inner) : ISelfWeighInRepository
    {
        private bool _missed;

        public async Task<SelfWeighIn?> FindByClientEntryIdAsync(int patientId, Guid clientEntryId,
            CancellationToken cancellationToken = default)
        {
            if (_missed) return await inner.FindByClientEntryIdAsync(patientId, clientEntryId, cancellationToken);
            _missed = true;
            return null;
        }

        public Task<SelfWeighIn?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return inner.FindByIdAsync(id, cancellationToken);
        }

        public Task AddAsync(SelfWeighIn entity, CancellationToken cancellationToken = default)
        {
            return inner.AddAsync(entity, cancellationToken);
        }

        public void Update(SelfWeighIn entity)
        {
            inner.Update(entity);
        }

        public void Remove(SelfWeighIn entity)
        {
            inner.Remove(entity);
        }

        public Task<IEnumerable<SelfWeighIn>> ListAsync(CancellationToken cancellationToken = default)
        {
            return inner.ListAsync(cancellationToken);
        }

        public Task<IEnumerable<SelfWeighIn>> ListByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return inner.ListByPatientIdAsync(patientId, cancellationToken);
        }

        public Task<IReadOnlyList<int>> ListPatientIdsAsync(CancellationToken cancellationToken = default)
        {
            return inner.ListPatientIdsAsync(cancellationToken);
        }
    }
}
