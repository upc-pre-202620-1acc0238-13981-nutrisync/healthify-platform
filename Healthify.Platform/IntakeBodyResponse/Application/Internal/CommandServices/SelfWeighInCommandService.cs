using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;

/// <summary>
///     Subflow 4.5 - Record Self Weigh In.
/// </summary>
/// <remarks>
///     A reading taken outside the protocol is recorded exactly like one taken inside it. The
///     declaration is stored, the reading is stored, and the only difference appears later, when the
///     trend is smoothed. Nothing here rejects a reading for being inconvenient.
/// </remarks>
public class SelfWeighInCommandService(
    ISelfWeighInRepository selfWeighInRepository,
    ISelfWeighInProtocolProvider protocolProvider,
    IUnitOfWork unitOfWork,
    ILogger<SelfWeighInCommandService> logger,
    IMediator mediator) : ISelfWeighInCommandService
{
    public async Task<Result<SelfWeighIn, IntakeError>> Handle(RecordSelfWeighInCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return Failure(IntakeError.PatientWriteOnly);

        WeightKg valueKg;
        try
        {
            // Business rule: Plausible Weight Range (Subflow 4.5)
            valueKg = new WeightKg(command.ValueKg);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.ImplausibleWeightValue);
        }

        LocalTimestamp localTimestamp;
        try
        {
            localTimestamp = new LocalTimestamp(command.LocalTimestamp);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.LocalTimestampRequired);
        }

        // Business rule: Protocol Compliance Declared (Subflow 4.5). IN-3: fasted is the one question
        // asked; same time of day and same scale are stored as an older client declared them, or null.
        var protocolCompliance = new ProtocolCompliance(command.FastedState, command.SameTimeOfDay,
            command.SameScale);

        try
        {
            var weighIn = new SelfWeighIn(command.PatientId, valueKg, localTimestamp, protocolCompliance);

            await selfWeighInRepository.AddAsync(weighIn, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new SelfWeighInRecorded(weighIn.Id.Value, weighIn.PatientId,
                    weighIn.FollowsProtocolUnder(protocolProvider.Current)),
                cancellationToken);

            return new Result<SelfWeighIn, IntakeError>.Success(weighIn);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.ProtocolComplianceRequired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record a self weigh-in for patient {PatientId}",
                command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>
    ///     IN-4 - Sync Pending Self Weigh Ins.
    /// </summary>
    /// <remarks>
    ///     Each reading is its own committed step, as in Subflow 4.6, so an interrupted batch keeps what it
    ///     had already stored and the resend finds it. The retroactive window does not apply: a queued
    ///     reading was taken at the moment it declares (§12.2), and weigh-ins have no window anyway.
    /// </remarks>
    public async Task<Result<SelfWeighInSyncOutcome, IntakeError>> Handle(SyncSelfWeighInsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0)
            return new Result<SelfWeighInSyncOutcome, IntakeError>.Failure(IntakeError.PatientWriteOnly);

        var protocol = protocolProvider.Current;
        var outcomes = new List<SyncedSelfWeighInOutcome>();
        var seenInThisBatch = new Dictionary<Guid, SyncedSelfWeighInOutcome>();
        var created = new List<int>();

        foreach (var pending in command.Entries ?? [])
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Business rule: Idempotency By Aggregate Id (Subflow 4.6), inside one batch. Unlike the
            // diary, a repeat is resolved in silence («Duplicado en envío → se resuelve en silencio»).
            // DECISIÓN IN-4: the repeat reports what its first copy got; AlreadyPresent when it was stored.
            if (seenInThisBatch.TryGetValue(pending.ClientEntryId, out var first))
            {
                outcomes.Add(first.SelfWeighInId is { } id
                    ? new SyncedSelfWeighInOutcome(pending.ClientEntryId, id, SyncedEntryOutcome.AlreadyPresent,
                        null)
                    : first);
                continue;
            }

            SyncedSelfWeighInOutcome outcome;
            try
            {
                outcome = await ReconcileAsync(command.PatientId, pending, protocol, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not reconcile self weigh-in {ClientEntryId}", pending.ClientEntryId);
                outcome = Rejected(pending, IntakeError.UnexpectedError);
            }

            if (outcome.Outcome == SyncedEntryOutcome.Created) created.Add(outcome.SelfWeighInId!.Value);
            seenInThisBatch[pending.ClientEntryId] = outcome;
            outcomes.Add(outcome);
        }

        // IN-4: one Recalculate Weight Trend per batch, not per reading.
        if (created.Count > 0)
            await mediator.PublishAsync(new SelfWeighInBatchSynchronized(command.PatientId, created),
                cancellationToken);

        return new Result<SelfWeighInSyncOutcome, IntakeError>.Success(new SelfWeighInSyncOutcome(
            command.PatientId,
            outcomes.Count(o => o.Outcome == SyncedEntryOutcome.Created),
            outcomes.Count(o => o.Outcome == SyncedEntryOutcome.AlreadyPresent),
            outcomes.Count(o => o.Outcome == SyncedEntryOutcome.Rejected),
            outcomes));
    }

    private async Task<SyncedSelfWeighInOutcome> ReconcileAsync(int patientId, PendingSelfWeighIn pending,
        SelfWeighInProtocol protocol, CancellationToken cancellationToken)
    {
        if (pending.ClientEntryId == Guid.Empty) return Rejected(pending, IntakeError.ClientEntryIdRequired);

        // Business rule: Idempotency By Aggregate Id (Subflow 4.6). A resent reading is already here: it is
        // resolved in silence. A reading is never edited, so the first copy stands even if the resend
        // disagrees with it.
        var existing = await selfWeighInRepository.FindByClientEntryIdAsync(patientId, pending.ClientEntryId,
            cancellationToken);
        if (existing is not null) return AlreadyPresent(pending, existing);

        WeightKg valueKg;
        try
        {
            // Business rule: Plausible Weight Range (Subflow 4.5)
            valueKg = new WeightKg(pending.ValueKg);
        }
        catch (ArgumentException)
        {
            return Rejected(pending, IntakeError.ImplausibleWeightValue);
        }

        LocalTimestamp localTimestamp;
        try
        {
            localTimestamp = new LocalTimestamp(pending.LocalTimestamp);
        }
        catch (ArgumentException)
        {
            return Rejected(pending, IntakeError.LocalTimestampRequired);
        }

        var weighIn = new SelfWeighIn(patientId, valueKg, localTimestamp,
            new ProtocolCompliance(pending.FastedState, pending.SameTimeOfDay, pending.SameScale),
            pending.ClientEntryId);

        await selfWeighInRepository.AddAsync(weighIn, cancellationToken);
        try
        {
            await unitOfWork.CompleteAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Two batches with the same reading racing each other: the unique index kept one. The insert
            // that lost was never stored; removing it only detaches it so the next item can be saved.
            selfWeighInRepository.Remove(weighIn);
            var winner = await selfWeighInRepository.FindByClientEntryIdAsync(patientId, pending.ClientEntryId,
                cancellationToken);
            if (winner is null) throw;
            return AlreadyPresent(pending, winner);
        }

        await mediator.PublishAsync(
            new SelfWeighInRecorded(weighIn.Id.Value, weighIn.PatientId, weighIn.FollowsProtocolUnder(protocol),
                ViaSynchronization: true), cancellationToken);

        return new SyncedSelfWeighInOutcome(pending.ClientEntryId, weighIn.Id.Value, SyncedEntryOutcome.Created,
            null);
    }

    private static SyncedSelfWeighInOutcome AlreadyPresent(PendingSelfWeighIn pending, SelfWeighIn existing)
    {
        return new SyncedSelfWeighInOutcome(pending.ClientEntryId, existing.Id.Value,
            SyncedEntryOutcome.AlreadyPresent, null);
    }

    private static SyncedSelfWeighInOutcome Rejected(PendingSelfWeighIn pending, IntakeError reason)
    {
        return new SyncedSelfWeighInOutcome(pending.ClientEntryId, null, SyncedEntryOutcome.Rejected,
            reason.ToString());
    }

    private static Result<SelfWeighIn, IntakeError> Failure(IntakeError error)
    {
        return new Result<SelfWeighIn, IntakeError>.Failure(error);
    }
}

/// <summary>
///     Subflow 4.5 - Recalculate Weight Trend. Reached only from the policy that reacts to a reading.
/// </summary>
/// <remarks>
///     Business rule: Only Protocol Compliant Weigh Ins Smooth The Trend (Subflow 4.5). The filtering
///     lives inside the aggregate, which receives every reading and returns the ones it excluded, so
///     this service does not get to decide what counts.
/// </remarks>
public class WeightTrendCommandService(
    IWeightTrendRepository weightTrendRepository,
    ISelfWeighInRepository selfWeighInRepository,
    ISelfWeighInProtocolProvider protocolProvider,
    IUnitOfWork unitOfWork,
    ILogger<WeightTrendCommandService> logger,
    IMediator mediator) : IWeightTrendCommandService
{
    public async Task<Result<WeightTrend, IntakeError>> Handle(RecalculateWeightTrendCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return Failure(IntakeError.PatientWriteOnly);

        try
        {
            var weighIns = (await selfWeighInRepository.ListByPatientIdAsync(command.PatientId,
                cancellationToken)).ToList();

            var trend = await weightTrendRepository.FindByPatientIdAsync(command.PatientId,
                cancellationToken);

            if (trend is null)
            {
                trend = new WeightTrend(command.PatientId);
                await weightTrendRepository.AddAsync(trend, cancellationToken);
            }
            else
            {
                weightTrendRepository.Update(trend);
            }

            var excluded = trend.Recalculate(weighIns, protocolProvider.Current);

            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new WeightTrendRecalculated(trend.PatientId, trend.Points.Count), cancellationToken);

            // Business rule: Excluded Weigh Ins Are Kept As Data (Subflow 4.5). Announcing the
            // exclusion is so the client can explain the gap in the chart. Nothing acts on it, and
            // no other context subscribes.
            foreach (var selfWeighInId in excluded)
                await mediator.PublishAsync(
                    new SelfWeighInExcludedFromTrend(selfWeighInId, trend.PatientId), cancellationToken);

            return new Result<WeightTrend, IntakeError>.Success(trend);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not recalculate the weight trend of patient {PatientId}",
                command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    private static Result<WeightTrend, IntakeError> Failure(IntakeError error)
    {
        return new Result<WeightTrend, IntakeError>.Failure(error);
    }
}
