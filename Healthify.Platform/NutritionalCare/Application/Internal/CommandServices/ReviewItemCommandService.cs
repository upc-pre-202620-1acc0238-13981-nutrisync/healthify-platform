using System.Globalization;
using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

/// <summary>
///     The inbox. Signals from Monitoring enter here and stop.
/// </summary>
/// <remarks>
///     Business rule: Signal Notifies Never Modifies The Plan (Subflow 3.7), reformulated by NC-10 as "No signal or
///     algorithm modifies the plan without an explicit action of the practitioner". Nothing in this class writes a
///     NutritionPlan, and it does not depend on the plan command service at all: the absence of that dependency is
///     what makes the rule structural rather than a matter of care. NC-10 adds two things that stay on this side of
///     the line: the AI proposal (IA-8), which reads the version in force to describe it and attaches a text to the
///     item, and the scheduled recheck, which opens another item. Assigning a proposed plan is a different command
///     service (<c>PlanProposalCommandService</c>), reached only from the practitioner's POST.
/// </remarks>
public class ReviewItemCommandService(
    IReviewItemRepository reviewItemRepository,
    IUnitOfWork unitOfWork,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    ILogger<ReviewItemCommandService> logger,
    IMediator mediator,
    PlanAdjustmentInputReader planAdjustmentInputReader,
    IPlanAdjustmentProposer planAdjustmentProposer,
    IMonitoringContextFacade monitoringContextFacade,
    IClinicalDateProvider clinicalDate,
    TimeProvider timeProvider,
    IPlanProposalGenerationQueue planProposalGenerationQueue,
    IAiSettings aiSettings,
    IAiConsentPolicy aiConsentPolicy) : IReviewItemCommandService
{
    /// <summary>Subflow 3.7 - Open Review Item. Reached only from a policy.</summary>
    public async Task<Result<ReviewItem, NutritionalCareError>> Handle(OpenReviewItemCommand command,
        CancellationToken cancellationToken = default)
    {
        SignalType signalType;
        try
        {
            signalType = new SignalType(command.SignalType);
        }
        catch (ArgumentException)
        {
            return Failure(NutritionalCareError.UnexpectedError);
        }

        try
        {
            // Business rule: One Open Item Per Patient And Signal Type (Subflow 3.7). A second
            // identical signal does not add noise to the inbox, and it keeps the policy idempotent.
            if (await reviewItemRepository.ExistsOpenForPatientAndSignalTypeAsync(command.PatientId,
                    signalType, cancellationToken))
                return Failure(NutritionalCareError.ReviewItemAlreadyOpenForSignalType);

            // The inbox is read per practitioner, so the item records who is looking after the
            // patient at the moment the signal arrived.
            var careLink = await careRelationshipContextFacade.GetActiveCareLinkByPatientId(
                command.PatientId, cancellationToken);
            if (careLink is null) return Failure(NutritionalCareError.ActiveCareLinkRequired);

            var reviewItem = new ReviewItem(command, careLink.PractitionerId);

            await reviewItemRepository.AddAsync(reviewItem, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new ReviewItemCreated(reviewItem.Id.Value, reviewItem.PatientId, reviewItem.PractitionerId,
                    signalType.Value), cancellationToken);

            return new Result<ReviewItem, NutritionalCareError>.Success(reviewItem);
        }
        catch (ArgumentException)
        {
            return Failure(NutritionalCareError.UnexpectedError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error opening a review item for patient {PatientId}", command.PatientId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>Subflow 3.7 - Resolve Review Item. A person decides, and says what they decided.</summary>
    /// <remarks>NC-10: unchanged contract. A proposal still waiting is dismissed ("Resolver sin asignar").</remarks>
    public async Task<Result<ReviewItem, NutritionalCareError>> Handle(ResolveReviewItemCommand command,
        CancellationToken cancellationToken = default)
    {
        // Business rule: Resolution States Whether The Plan Was Adjusted (Subflow 3.7)
        if (command.ResolvedWithAdjustment is null)
            return Failure(NutritionalCareError.ResolutionOutcomeRequired);

        try
        {
            var reviewItem = await reviewItemRepository.FindByIdAsync(command.ReviewItemId, cancellationToken);
            if (reviewItem is null) return Failure(NutritionalCareError.ReviewItemNotFound);
            if (reviewItem.PractitionerId != command.PractitionerId)
                return Failure(NutritionalCareError.PractitionerOnly);

            reviewItem.Resolve(command.ResolvedWithAdjustment.Value, command.ResolutionNote);

            reviewItemRepository.Update(reviewItem);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new ReviewItemResolved(reviewItem.Id.Value, reviewItem.PatientId,
                    reviewItem.ResolvedWithAdjustment!.Value), cancellationToken);

            return new Result<ReviewItem, NutritionalCareError>.Success(reviewItem);
        }
        catch (InvalidOperationException)
        {
            return Failure(NutritionalCareError.ReviewItemNotFound);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving review item {ReviewItemId}", command.ReviewItemId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     NC-10 - Generate Plan Proposal. Reached only from the background worker queued by the sustained deviation
    ///     policy.
    /// </summary>
    /// <remarks>
    ///     The plan is read to describe it to the model, never written: this command saves the review item and nothing
    ///     else. When the AI is off, the patient did not consent (§12-#5), the provider fails or the proposal breaks a
    ///     hard rule, the item stays without a proposal and PR14 shows it without AI.
    /// </remarks>
    public async Task<Result<ReviewItem, NutritionalCareError>> Handle(GeneratePlanProposalCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reviewItem = await reviewItemRepository.FindByIdAsync(command.ReviewItemId, cancellationToken);
            if (reviewItem is null) return Failure(NutritionalCareError.ReviewItemNotFound);
            // Business rule: Proposal Only For A Sustained Deviation (NC-10, DECISIÓN §12-#11).
            if (!reviewItem.SignalType.IsSustainedDeviation)
                return Failure(NutritionalCareError.ReviewItemNotSustainedDeviation);
            if (!reviewItem.IsOpen) return Failure(NutritionalCareError.ReviewItemNotFound);
            if (reviewItem.HasPlanProposal) return Failure(NutritionalCareError.PlanProposalAlreadyDecided);

            var (input, error) = await planAdjustmentInputReader.ReadAsync(reviewItem, cancellationToken);
            if (input is null) return Failure(error);

            var generation = await planAdjustmentProposer.ProposePlanAdjustment(input, cancellationToken);
            if (generation is not Result<AiGenerationOutcome<PlanAdjustmentProposalOutput>, AiError>.Success
                success)
            {
                var reason = generation is Result<AiGenerationOutcome<PlanAdjustmentProposalOutput>, AiError>
                    .Failure failure
                    ? failure.Error.ToString()
                    : "Unknown";
                logger.LogInformation("AI feature {Feature} left review item {ReviewItemId} without a proposal: {Reason}",
                    AiFeature.PlanAdjustmentProposal.Name, reviewItem.Id.Value, reason);
                return Failure(NutritionalCareError.PlanProposalNotFound);
            }

            // The practitioner may have resolved the item while the model answered.
            if (!await reviewItemRepository.ExistsOpenWithoutProposalAsync(reviewItem.Id.Value, cancellationToken))
                return Failure(NutritionalCareError.PlanProposalAlreadyDecided);

            // IA-8. The patient's AI processing may have ended while the model answered; its purge already ran, so
            // the proposal is not attached. NOTE: hotspot. A withdrawal committed between this check and the save
            // below still leaves one proposal behind (no lock spans both contexts); the next withdrawal purges it.
            if (!await aiConsentPolicy.IsAllowedAsync(reviewItem.PatientId, AiFeature.PlanAdjustmentProposal,
                    cancellationToken))
            {
                logger.LogInformation("AI feature {Feature} dropped the proposal of review item {ReviewItemId}: " +
                                      "consent ended during the generation",
                    AiFeature.PlanAdjustmentProposal.Name, reviewItem.Id.Value);
                return Failure(NutritionalCareError.PlanProposalNotFound);
            }

            var output = success.Value.Output;
            reviewItem.AttachProposal(success.Value.GenerationId, output.Title, output.EnergyKcal, output.ProteinG,
                output.CarbG, output.FatG, output.AddedGuidelines, output.RemovedGuidelines, output.PatientMessage,
                output.RecheckAfterDays, output.Rationale, timeProvider.GetUtcNow(), output.PractitionerLanguage,
                output.PatientLanguage);

            reviewItemRepository.Update(reviewItem);
            await unitOfWork.CompleteAsync(cancellationToken);

            return new Result<ReviewItem, NutritionalCareError>.Success(reviewItem);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            return Failure(NutritionalCareError.PlanProposalAlreadyDecided);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating the plan proposal of review item {ReviewItemId}",
                command.ReviewItemId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     NC-10 - Open the scheduled rechecks (DECISIÓN §12-#12). One new <c>ScheduledRecheck</c> item per adjustment
    ///     whose date arrived, with its evidence read from Monitoring: "Revisión programada tras ajuste del 8 sept.".
    /// </summary>
    /// <remarks>
    ///     Each recheck is saved on its own, so one failure does not stop the others. When nobody cares for the patient
    ///     any more, or a recheck of the patient is already open, the recheck is marked issued without a new item.
    /// </remarks>
    public async Task<Result<int, NutritionalCareError>> Handle(OpenDueRechecksCommand command,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReviewItem> due;
        try
        {
            due = await reviewItemRepository.ListRecheckDueAsync(command.Now, Math.Max(1, command.BatchSize),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading the due review item rechecks");
            return new Result<int, NutritionalCareError>.Failure(NutritionalCareError.UnexpectedError);
        }

        var opened = 0;
        foreach (var source in due)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                if (await OpenRecheckAsync(source, command.Now, cancellationToken)) opened++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not open the scheduled recheck of review item {ReviewItemId}",
                    source.Id.Value);
            }
        }

        return new Result<int, NutritionalCareError>.Success(opened);
    }

    /// <summary>
    ///     IA-8 - Purge the unaccepted AI plan proposals of a patient (policy "When AI Processing Consent Withdrawn",
    ///     CR-2, §12-#14). A proposal still Proposed or Dismissed is removed; one a practitioner accepted produced a
    ///     version of the plan and stays as clinical record. The review items are kept as they are: an open one is
    ///     PR14 without AI.
    /// </summary>
    /// <remarks>
    ///     DECISIÓN IA-8: "no aceptadas" includes the Dismissed ones too. They hold the same AI text and are not part
    ///     of any version; the resolution of their item (with its note) is what the record keeps.
    /// </remarks>
    public async Task<Result<int, NutritionalCareError>> Handle(PurgeUnacceptedPlanProposalsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return new Result<int, NutritionalCareError>.Success(0);

        try
        {
            var items = await reviewItemRepository.ListWithUnacceptedProposalByPatientIdAsync(command.PatientId,
                cancellationToken);

            // Business rule: Accepted Proposal Is Clinical Record (IA-8), asserted by the aggregate.
            var purged = 0;
            foreach (var item in items)
            {
                if (!item.PurgeUnacceptedProposal()) continue;
                reviewItemRepository.Update(item);
                purged++;
            }

            if (purged > 0) await unitOfWork.CompleteAsync(cancellationToken);

            return new Result<int, NutritionalCareError>.Success(purged);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error purging the AI plan proposals of patient {PatientId}", command.PatientId);
            return new Result<int, NutritionalCareError>.Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     NC-10 - Recover the plan proposals. The queue lives in memory, so a restart loses what was waiting; this
    ///     runs at start-up and every N minutes and queues the open sustained deviations of the last hours that have
    ///     no proposal, as long as the function is on and the patient consented (otherwise the generation could only
    ///     end in a refusal). Queuing is all it does: the worker generates, and generating never touches the plan.
    /// </summary>
    public async Task<Result<int, NutritionalCareError>> Handle(RecoverPlanProposalsCommand command,
        CancellationToken cancellationToken = default)
    {
        var feature = AiFeature.PlanAdjustmentProposal;
        if (!aiSettings.IsEnabled(feature)) return new Result<int, NutritionalCareError>.Success(0);

        IReadOnlyList<ReviewItem> waiting;
        try
        {
            waiting = await reviewItemRepository.ListOpenWithoutProposalAsync(
                new SignalType(SignalType.SustainedDeviation), command.Now - command.MaxAge,
                Math.Max(1, command.BatchSize), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading the review items waiting for a plan proposal");
            return new Result<int, NutritionalCareError>.Failure(NutritionalCareError.UnexpectedError);
        }

        var queued = 0;
        foreach (var item in waiting)
        {
            if (cancellationToken.IsCancellationRequested) break;
            // Idempotent: already waiting or being generated.
            if (planProposalGenerationQueue.IsPending(item.Id.Value)) continue;
            try
            {
                // §12-#5: without the patient's AI consent the practitioner function has no proposal.
                if (!await aiConsentPolicy.IsAllowedAsync(item.PatientId, feature, cancellationToken)) continue;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read the AI consent for review item {ReviewItemId}", item.Id.Value);
                continue;
            }

            if (planProposalGenerationQueue.TryEnqueue(item.Id.Value)) queued++;
        }

        if (queued > 0)
            logger.LogInformation("Queued {Count} plan proposals that were waiting without one", queued);
        return new Result<int, NutritionalCareError>.Success(queued);
    }

    private async Task<bool> OpenRecheckAsync(ReviewItem source, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var careLink = await careRelationshipContextFacade.GetActiveCareLinkByPatientId(source.PatientId,
            cancellationToken);
        var alreadyOpen = careLink is not null && await reviewItemRepository.ExistsOpenForPatientAndSignalTypeAsync(
            source.PatientId, new SignalType(SignalType.ScheduledRecheck), cancellationToken);
        if (careLink is null || alreadyOpen)
        {
            // Business rule: One Open Item Per Patient And Signal Type (3.7); a recheck without care is nobody's.
            source.MarkRecheckIssued(now);
            reviewItemRepository.Update(source);
            await unitOfWork.CompleteAsync(cancellationToken);
            logger.LogInformation("Scheduled recheck of review item {ReviewItemId} closed without a new item",
                source.Id.Value);
            return false;
        }

        var adjustedOn = clinicalDate.DateOf(source.ResolvedAt ?? now);
        var version = source.Proposal?.AssignedPlanVersion;
        var today = clinicalDate.Today();
        var summary = adjustedOn <= today
            ? await monitoringContextFacade.GetComplianceSummary(source.PatientId, adjustedOn, today,
                cancellationToken)
            : null;

        // Evidence, not a verdict: days outside the target among the logged ones; unlogged days are not counted.
        var deviated = summary is null ? (int?)null : summary.Short + summary.Exceeded;
        var evidence = $"Scheduled review after the plan adjustment of " +
                       $"{adjustedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                       (version is null ? "" : $" (version {version})") +
                       (summary is null
                           ? "."
                           : $": {deviated} of {summary.Logged} logged days outside the target since then; " +
                             "days without a record are not counted.");

        var recheck = new ReviewItem(new OpenReviewItemCommand(source.PatientId, SignalType.ScheduledRecheck,
            evidence, new ReviewItemEvidenceDto(null, deviated, summary?.Logged, null, adjustedOn, version)),
            careLink.PractitionerId);
        source.MarkRecheckIssued(now);

        await reviewItemRepository.AddAsync(recheck, cancellationToken);
        reviewItemRepository.Update(source);
        await unitOfWork.CompleteAsync(cancellationToken);

        await mediator.PublishAsync(new ReviewItemCreated(recheck.Id.Value, recheck.PatientId,
            recheck.PractitionerId, SignalType.ScheduledRecheck), cancellationToken);
        return true;
    }

    private static Result<ReviewItem, NutritionalCareError> Failure(NutritionalCareError error)
    {
        return new Result<ReviewItem, NutritionalCareError>.Failure(error);
    }
}
