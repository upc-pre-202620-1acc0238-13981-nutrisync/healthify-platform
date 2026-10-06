using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.EventHandlers;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     IN-4. Offline self weigh-ins are synchronised idempotently by client identifier, a duplicate is resolved
///     in silence, and the trend is recalculated once per batch.
/// </summary>
public class SelfWeighInSyncTests
{
    private const int PatientId = 4;

    private readonly List<SelfWeighIn> _stored = [];
    private readonly ISelfWeighInRepository _repository = Substitute.For<ISelfWeighInRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public SelfWeighInSyncTests()
    {
        _repository.AddAsync(Arg.Do<SelfWeighIn>(w =>
                _stored.Add(Identity.Assign(w, new SelfWeighInId(_stored.Count + 1)))),
            Arg.Any<CancellationToken>());
        _repository.FindByClientEntryIdAsync(Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => _stored.FirstOrDefault(w =>
                w.PatientId == call.ArgAt<int>(0) && w.ClientEntryId == call.ArgAt<Guid>(1)));
    }

    [Fact]
    public async Task A_batch_stores_each_reading_and_asks_for_one_recalculation()
    {
        var outcome = await Sync(Pending(Guid.NewGuid(), 80m), Pending(Guid.NewGuid(), 79.6m),
            Pending(Guid.NewGuid(), 79.4m, false));

        Assert.Equal(3, outcome.Created);
        Assert.Equal(3, _stored.Count);
        Assert.All(_stored, w => Assert.NotNull(w.ClientEntryId));

        var published = Fakes.Published(_mediator);
        Assert.Equal(3, published.OfType<SelfWeighInRecorded>().Count(e => e.ViaSynchronization));
        var batch = Assert.Single(published.OfType<SelfWeighInBatchSynchronized>());
        Assert.Equal([1, 2, 3], batch.SelfWeighInIds);
    }

    [Fact]
    public async Task A_resent_reading_is_resolved_in_silence()
    {
        var clientId = Guid.NewGuid();
        await Sync(Pending(clientId, 80m));
        _mediator.ClearReceivedCalls();

        // The device did not hear back and sends it again, even with a different value.
        var outcome = await Sync(Pending(clientId, 81m));

        var entry = Assert.Single(outcome.Entries);
        Assert.Equal(SyncedEntryOutcome.AlreadyPresent, entry.Outcome);
        Assert.Equal(1, entry.SelfWeighInId);
        Assert.Null(entry.Reason);
        Assert.Equal(0, outcome.Rejected);
        Assert.Single(_stored);
        Assert.Equal(80m, _stored[0].ValueKg);
        Assert.Empty(Fakes.Published(_mediator));
    }

    [Fact]
    public async Task A_reading_repeated_inside_the_batch_is_stored_once_and_reported_already_present()
    {
        var clientId = Guid.NewGuid();

        var outcome = await Sync(Pending(clientId, 80m), Pending(clientId, 80m));

        Assert.Single(_stored);
        Assert.Equal([SyncedEntryOutcome.Created, SyncedEntryOutcome.AlreadyPresent],
            outcome.Entries.Select(e => e.Outcome));
        Assert.Equal(0, outcome.Rejected);
    }

    [Fact]
    public async Task The_same_client_identifier_from_another_patient_is_a_different_reading()
    {
        var clientId = Guid.NewGuid();
        await Sync(Pending(clientId, 80m), patientId: 99);

        var outcome = await Sync(Pending(clientId, 80m));

        Assert.Equal(1, outcome.Created);
        Assert.Equal(2, _stored.Count);
    }

    [Fact]
    public async Task One_bad_reading_does_not_take_the_batch_down()
    {
        var outcome = await Sync(Pending(Guid.NewGuid(), 5m), Pending(Guid.Empty, 80m),
            Pending(Guid.NewGuid(), 80m));

        Assert.Equal(1, outcome.Created);
        Assert.Equal(2, outcome.Rejected);
        Assert.Equal(nameof(IntakeError.ImplausibleWeightValue), outcome.Entries[0].Reason);
        Assert.Equal(nameof(IntakeError.ClientEntryIdRequired), outcome.Entries[1].Reason);
    }

    [Fact]
    public async Task No_retroactive_window_applies_to_a_queued_reading()
    {
        var outcome = await Sync(new PendingSelfWeighIn(Guid.NewGuid(), 80m, DateTimeOffset.UtcNow.AddDays(-6),
            true));

        Assert.Equal(1, outcome.Created);
    }

    [Fact]
    public async Task A_batch_that_created_nothing_does_not_recalculate()
    {
        var outcome = await Sync(Pending(Guid.NewGuid(), 5m));

        Assert.Equal(1, outcome.Rejected);
        Assert.Empty(Fakes.Published(_mediator).OfType<SelfWeighInBatchSynchronized>());
    }

    [Fact]
    public async Task Losing_a_race_on_the_unique_index_is_already_present()
    {
        var clientId = Guid.NewGuid();
        var winner = WeighIns.Reading(50, PatientId, new DateOnly(2026, 9, 1), 80m);
        typeof(SelfWeighIn).GetProperty(nameof(SelfWeighIn.ClientEntryId))!.SetValue(winner, clientId);
        _repository.FindByClientEntryIdAsync(PatientId, clientId, Arg.Any<CancellationToken>())
            .Returns((SelfWeighIn?)null, winner);
        _unitOfWork.CompleteAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("duplicate"));

        var outcome = await Sync(Pending(clientId, 80m));

        var entry = Assert.Single(outcome.Entries);
        Assert.Equal(SyncedEntryOutcome.AlreadyPresent, entry.Outcome);
        Assert.Equal(50, entry.SelfWeighInId);
        _repository.Received(1).Remove(Arg.Any<SelfWeighIn>());
    }

    [Fact]
    public async Task With_the_real_policies_the_trend_is_recalculated_once_for_the_whole_batch()
    {
        var trends = Substitute.For<IWeightTrendCommandService>();
        trends.Handle(Arg.Any<RecalculateWeightTrendCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<WeightTrend, IntakeError>.Success(new WeightTrend(PatientId)));
        var scopes = Fakes.ScopeFactoryWith(trends);
        Fakes.Route(_mediator, new OnSelfWeighInRecordedHandler(scopes,
            NullLogger<OnSelfWeighInRecordedHandler>.Instance));
        Fakes.Route(_mediator, new OnSelfWeighInBatchSynchronizedHandler(scopes,
            NullLogger<OnSelfWeighInBatchSynchronizedHandler>.Instance));

        await Sync(Pending(Guid.NewGuid(), 80m), Pending(Guid.NewGuid(), 79.8m), Pending(Guid.NewGuid(), 79.5m));

        await trends.Received(1).Handle(new RecalculateWeightTrendCommand(PatientId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_online_reading_still_recalculates_on_its_own()
    {
        var trends = Substitute.For<IWeightTrendCommandService>();
        trends.Handle(Arg.Any<RecalculateWeightTrendCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<WeightTrend, IntakeError>.Success(new WeightTrend(PatientId)));
        var handler = new OnSelfWeighInRecordedHandler(Fakes.ScopeFactoryWith(trends),
            NullLogger<OnSelfWeighInRecordedHandler>.Instance);

        await handler.Handle(new SelfWeighInRecorded(1, PatientId, true), CancellationToken.None);
        await handler.Handle(new SelfWeighInRecorded(2, PatientId, true, ViaSynchronization: true),
            CancellationToken.None);

        await trends.Received(1).Handle(Arg.Any<RecalculateWeightTrendCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_body_without_patient_id_maps_to_the_authenticated_patient()
    {
        var clientId = Guid.NewGuid();
        var moment = DateTimeOffset.UtcNow;

        var command = SyncSelfWeighInsCommandAssembler.ToCommand(PatientId,
            new SyncSelfWeighInsResource([new PendingSelfWeighInResource(clientId, 80m, moment, true)]));

        Assert.Equal(PatientId, command.PatientId);
        Assert.Equal(new PendingSelfWeighIn(clientId, 80m, moment, true), Assert.Single(command.Entries));
    }

    private async Task<SelfWeighInSyncOutcome> Sync(params PendingSelfWeighIn[] entries)
    {
        return await Sync(entries, PatientId);
    }

    private async Task<SelfWeighInSyncOutcome> Sync(PendingSelfWeighIn entry, int patientId)
    {
        return await Sync([entry], patientId);
    }

    private async Task<SelfWeighInSyncOutcome> Sync(PendingSelfWeighIn[] entries, int patientId)
    {
        var protocol = Substitute.For<ISelfWeighInProtocolProvider>();
        protocol.Current.Returns(SelfWeighInProtocol.Default);
        var service = new SelfWeighInCommandService(_repository, protocol, _unitOfWork,
            NullLogger<SelfWeighInCommandService>.Instance, _mediator);

        var result = await service.Handle(new SyncSelfWeighInsCommand(patientId, entries));
        return Assert.IsType<Result<SelfWeighInSyncOutcome, IntakeError>.Success>(result).Value;
    }

    private static PendingSelfWeighIn Pending(Guid clientEntryId, decimal kg, bool fasted = true)
    {
        return new PendingSelfWeighIn(clientEntryId, kg, DateTimeOffset.UtcNow.AddHours(-3), fasted);
    }
}
