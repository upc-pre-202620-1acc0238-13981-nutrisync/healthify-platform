using Cortex.Mediator;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;

/// <summary>
///     The diary: Subflows 4.2, 4.3, 4.4 and 4.6.
/// </summary>
/// <remarks>
///     Nothing in this class removes an entry, and nothing in it qualifies one. There is no call to
///     the repository's inherited Remove, no compliance flag and no comparison against a target,
///     because comparing what was prescribed with what was eaten belongs to Monitoring and Adherence.
///     This service records what the patient said, as they said it.
/// </remarks>
public class DiaryEntryCommandService(
    IDiaryEntryRepository diaryEntryRepository,
    IUnitOfWork unitOfWork,
    IFoodCatalogContextFacade foodCatalogContextFacade,
    IConfiguration configuration,
    ILogger<DiaryEntryCommandService> logger,
    IMediator mediator,
    IMealPhotoAnalysisRepository? mealPhotoAnalysisRepository = null,
    TimeProvider? timeProvider = null) : IDiaryEntryCommandService
{
    private const int DefaultRetroactiveLoggingWindowHours = 48;

    /// <summary>IN-6. DECISIÓN IN-6: an idea has two to eight ingredients (IA-3); ten leaves room for a hand-made group.</summary>
    public const int MaximumMealGroupItems = 10;

    /// <summary>Subflow 4.2 - Log Meal By Photo.</summary>
    /// <remarks>
    ///     IN-7: with an <c>analysisId</c>, the proposal is the one the AI made on the server from the photo
    ///     (<see cref="LogAnalyzedPhotoMeal" />). Without it, the legacy flow of IN-2: the reference food, the portion
    ///     and the confidence arrive already computed on the device. This method creates the entry and announces it;
    ///     without a confirmation, the policy of Subflow 4.2 is what stores the proposal.
    ///     IN-7: a <c>clientEntryId</c> already stored for this patient returns that entry, with no new entry and no
    ///     event (the day is not evaluated again). It is checked first, so a retry is answered even after the
    ///     analysis expired.
    /// </remarks>
    public async Task<Result<DiaryEntry, IntakeError>> Handle(LogMealByPhotoCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return Failure(IntakeError.PatientWriteOnly);

        // Business rule: Idempotency By Aggregate Id (Subflow 4.6, IN-7)
        if (await ReplayAsync(command.PatientId, command.ClientEntryId, cancellationToken) is { } replay)
            return replay;

        if (command.AnalysisId is { } analysisId)
            return await LogAnalyzedPhotoMeal(command, analysisId, cancellationToken);

        // Business rule: Confidence Always Attached (Subflow 4.2). An estimate without it is a
        // number pretending to be a measurement, so the entry is refused before it exists.
        Confidence confidence;
        try
        {
            confidence = new Confidence(command.Confidence);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.ConfidenceRequired);
        }

        if (command.ReferenceFoodId <= 0 || command.PortionGrams <= 0m)
            return Failure(IntakeError.ReferenceFoodNotResolved);

        var timestamp = BuildLocalTimestamp(command.LocalTimestamp, out var timestampError);
        if (timestamp is null) return Failure(timestampError);

        // IN-2: the patient already confirmed or adjusted on the device (PT7). Without a
        // confirmation, nothing below changes: the entry waits as «Por confirmar».
        if (command.Confirmation is not null)
            return await LogConfirmedPhotoMeal(command, command.Confirmation, timestamp, confidence,
                cancellationToken);

        return await LogUnconfirmedPhotoMeal(command, timestamp, confidence, null, cancellationToken);
    }

    /// <summary>
    ///     IN-7. A photo meal whose proposal the AI made on the server (<c>POST /patients/{id}/meal-photo-analyses</c>).
    /// </summary>
    /// <remarks>
    ///     The order is the specification:
    ///     1. the moment (with the retroactive window);
    ///     2. the analysis: of this patient (otherwise it does not exist for them), and logged at most once (the entry
    ///     it already became is returned); then not expired;
    ///     3. the proposal taken from the analysis, never from the body; then, as the legacy flow does, the
    ///     confirmation and its answer validated and stored beside the proposal (Proposal Kept Alongside
    ///     Confirmation).
    /// </remarks>
    private async Task<Result<DiaryEntry, IntakeError>> LogAnalyzedPhotoMeal(LogMealByPhotoCommand command,
        Guid analysisId, CancellationToken cancellationToken)
    {
        var timestamp = BuildLocalTimestamp(command.LocalTimestamp, out var timestampError);
        if (timestamp is null) return Failure(timestampError);
        if (mealPhotoAnalysisRepository is null || analysisId == Guid.Empty)
            return Failure(IntakeError.MealPhotoAnalysisNotFound);

        MealPhotoAnalysis? analysis;
        try
        {
            analysis = await mealPhotoAnalysisRepository.FindByIdAsync(analysisId, cancellationToken);

            // An analysis of another patient does not exist for this one.
            if (analysis is null || !analysis.BelongsTo(command.PatientId))
                return Failure(IntakeError.MealPhotoAnalysisNotFound);

            // One analysis, one entry: a second log of it returns the first.
            var logged = await diaryEntryRepository.FindByMealPhotoAnalysisIdAsync(analysisId, cancellationToken);
            if (logged is not null)
                return logged.PatientId == command.PatientId
                    ? Success(logged)
                    : Failure(IntakeError.MealPhotoAnalysisNotFound);

            if (analysis.IsExpiredAt((timeProvider ?? TimeProvider.System).GetUtcNow()))
                return Failure(IntakeError.MealPhotoAnalysisExpired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the photo analysis for patient {PatientId}", command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }

        // The proposal is the analysis'; whatever the body proposed is ignored.
        var proposed = command with
        {
            ReferenceFoodId = analysis.ReferenceFoodId,
            PortionGrams = analysis.EstimatedGrams,
            Confidence = analysis.Confidence,
            PhotoRef = null
        };
        var confidence = new Confidence(analysis.Confidence);

        return command.Confirmation is not null
            ? await LogConfirmedPhotoMeal(proposed, command.Confirmation, timestamp, confidence, cancellationToken,
                analysis)
            : await LogUnconfirmedPhotoMeal(proposed, timestamp, confidence, analysis, cancellationToken);
    }

    /// <summary>Subflow 4.2 as before IN-2: the entry waits as «Por confirmar» and the policy stores the proposal.</summary>
    private async Task<Result<DiaryEntry, IntakeError>> LogUnconfirmedPhotoMeal(LogMealByPhotoCommand command,
        LocalTimestamp timestamp, Confidence confidence, MealPhotoAnalysis? analysis,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = new DiaryEntry(command.PatientId, timestamp, new Provenance(Provenance.Photo),
                new SyncState(SyncState.Synced), command.PhotoRef, command.ClientEntryId);
            if (analysis is not null) entry.RecordPhotoAnalysis(analysis.Id, analysis.AiGenerationId);

            await diaryEntryRepository.AddAsync(entry, cancellationToken);
            if (await SaveOrFindWinnerAsync(entry, cancellationToken) is { } winner)
                return winner.PatientId == command.PatientId
                    ? Success(winner)
                    : Failure(IntakeError.DuplicatedClientEntryId);

            await mediator.PublishAsync(
                new MealLogged(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                    entry.Provenance.Value, command.ReferenceFoodId, command.PortionGrams,
                    confidence.Value), cancellationToken);

            return Success(entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not log a photo meal for patient {PatientId}", command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>
    ///     IN-2. A photo meal saved already confirmed: the proposal the device computed and what the
    ///     patient decided about it are stored together, in one transaction.
    /// </summary>
    /// <remarks>
    ///     Business rule: Proposal Kept Alongside Confirmation (Subflow 4.2). The proposal is stored
    ///     first and the confirmation beside it, so «Lo que propusimos: 320 g» survives a 300 g answer.
    ///     The events leave in the same order as the two-request flow, and MealLogged says the
    ///     confirmation follows, so the estimation policy does not store the proposal a second time and
    ///     Monitoring evaluates the day once (MA-1).
    /// </remarks>
    private async Task<Result<DiaryEntry, IntakeError>> LogConfirmedPhotoMeal(LogMealByPhotoCommand command,
        PhotoConfirmation confirmation, LocalTimestamp timestamp, Confidence confidence,
        CancellationToken cancellationToken, MealPhotoAnalysis? analysis = null)
    {
        var isAdjusted = confirmation.Kind.Equals(PhotoConfirmation.Adjusted, StringComparison.OrdinalIgnoreCase);
        var isAsProposed = confirmation.Kind.Equals(PhotoConfirmation.AsProposed,
            StringComparison.OrdinalIgnoreCase);

        if (!isAdjusted && !isAsProposed) return Failure(IntakeError.InvalidEstimateConfirmation);
        if (isAdjusted && (confirmation.ReferenceFoodId is not > 0 || confirmation.PortionGrams is not > 0m))
            return Failure(IntakeError.InvalidEstimateConfirmation);

        // Business rule: Plan Adherence Required On Confirmation (IN-1)
        var adherence = BuildAnswer(command.PlanAdherence, out var adherenceError);
        if (adherence is null) return Failure(adherenceError);

        try
        {
            // Business rule: Food Resolved From Local Catalog (Subflow 4.2), for the proposal and for
            // the correction alike. Nothing is stored if either cannot be resolved.
            if (await foodCatalogContextFacade.GetReferenceFoodById(command.ReferenceFoodId, cancellationToken)
                is null)
                return Failure(IntakeError.ReferenceFoodNotResolved);
            if (isAdjusted && await foodCatalogContextFacade.GetReferenceFoodById(
                    confirmation.ReferenceFoodId!.Value, cancellationToken) is null)
                return Failure(IntakeError.ReferenceFoodNotResolved);

            var entry = new DiaryEntry(command.PatientId, timestamp, new Provenance(Provenance.Photo),
                new SyncState(SyncState.Synced), command.PhotoRef, command.ClientEntryId);

            // IN-7: with an analysis, the proposal is the AI's, traced to its generation.
            entry.ProposeEstimate(analysis?.ToProposal(DateTimeOffset.UtcNow) ??
                                  new ProposedEstimate(command.ReferenceFoodId, command.PortionGrams, confidence,
                                      DateTimeOffset.UtcNow));
            if (analysis is not null) entry.RecordPhotoAnalysis(analysis.Id, analysis.AiGenerationId);

            if (isAdjusted)
                entry.AdjustProposedEstimate(confirmation.ReferenceFoodId!.Value, confirmation.PortionGrams!.Value,
                    adherence);
            else
                entry.ConfirmProposedEstimate(adherence);

            await diaryEntryRepository.AddAsync(entry, cancellationToken);
            if (await SaveOrFindWinnerAsync(entry, cancellationToken) is { } winner)
                return winner.PatientId == command.PatientId
                    ? Success(winner)
                    : Failure(IntakeError.DuplicatedClientEntryId);

            await mediator.PublishAsync(
                new MealLogged(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                    entry.Provenance.Value, command.ReferenceFoodId, command.PortionGrams, confidence.Value,
                    entry.PlanAdherence.Value, ConfirmedOnCreation: true), cancellationToken);

            if (isAdjusted)
                await mediator.PublishAsync(
                    new EstimateAdjustedByPatient(entry.Id.Value, entry.PatientId,
                        confirmation.ReferenceFoodId!.Value, confirmation.PortionGrams!.Value,
                        entry.PlanAdherence.Value), cancellationToken);

            await mediator.PublishAsync(
                new EstimateConfirmedByPatient(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                    entry.PlanAdherence.Value), cancellationToken);

            return Success(entry);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.ReferenceFoodNotResolved);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not log a confirmed photo meal for patient {PatientId}", command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>Subflow 4.2 - Estimate Portion. Reached only from the photo estimation policy.</summary>
    public async Task<Result<DiaryEntry, IntakeError>> Handle(EstimatePortionCommand command,
        CancellationToken cancellationToken = default)
    {
        Confidence confidence;
        try
        {
            confidence = new Confidence(command.Confidence);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.ConfidenceRequired);
        }

        try
        {
            var entry = await diaryEntryRepository.FindByIdAsync(command.DiaryEntryId, cancellationToken);
            if (entry is null) return Failure(IntakeError.DiaryEntryNotFound);

            // Business rule: Food Resolved From Local Catalog (Subflow 4.2). The catalog is asked
            // through the ACL; an identifier this platform cannot resolve does not become intake.
            var referenceFood = await foodCatalogContextFacade.GetReferenceFoodById(
                command.ReferenceFoodId, cancellationToken);
            if (referenceFood is null) return Failure(IntakeError.ReferenceFoodNotResolved);

            entry.ProposeEstimate(new ProposedEstimate(command.ReferenceFoodId, command.PortionGrams,
                confidence, DateTimeOffset.UtcNow));

            diaryEntryRepository.Update(entry);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new EstimateProposed(entry.Id.Value, entry.PatientId, command.ReferenceFoodId,
                    command.PortionGrams, confidence.Value), cancellationToken);

            return Success(entry);
        }
        catch (InvalidOperationException)
        {
            return Failure(IntakeError.EstimateAlreadyConfirmed);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.ReferenceFoodNotResolved);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not store a proposed estimate on entry {DiaryEntryId}",
                command.DiaryEntryId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>Subflow 4.2 - Confirm Estimate.</summary>
    public async Task<Result<DiaryEntry, IntakeError>> Handle(ConfirmEstimateCommand command,
        CancellationToken cancellationToken = default)
    {
        // Business rule: Plan Adherence Required On Confirmation (IN-1)
        var adherence = BuildAnswer(command.PlanAdherence, out var adherenceError);
        if (adherence is null) return Failure(adherenceError);

        try
        {
            var entry = await diaryEntryRepository.FindByIdAsync(command.DiaryEntryId, cancellationToken);
            if (entry is null) return Failure(IntakeError.DiaryEntryNotFound);
            if (entry.PatientId != command.PatientId) return Failure(IntakeError.PatientWriteOnly);
            if (!entry.HasProposedEstimate) return Failure(IntakeError.EstimateNotProposed);
            if (entry.HasConfirmedEstimate) return Failure(IntakeError.EstimateAlreadyConfirmed);

            // Business rule: Proposal Kept Alongside Confirmation (Subflow 4.2). The aggregate copies
            // the proposal into the confirmation and leaves the proposal untouched.
            entry.ConfirmProposedEstimate(adherence);

            diaryEntryRepository.Update(entry);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new EstimateConfirmedByPatient(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                    entry.PlanAdherence.Value), cancellationToken);

            return Success(entry);
        }
        catch (InvalidOperationException)
        {
            return Failure(IntakeError.EstimateAlreadyConfirmed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not confirm the estimate on entry {DiaryEntryId}",
                command.DiaryEntryId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>Subflow 4.2 - Adjust Estimate.</summary>
    public async Task<Result<DiaryEntry, IntakeError>> Handle(AdjustEstimateCommand command,
        CancellationToken cancellationToken = default)
    {
        // Business rule: Plan Adherence Required On Confirmation (IN-1)
        var adherence = BuildAnswer(command.PlanAdherence, out var adherenceError);
        if (adherence is null) return Failure(adherenceError);

        try
        {
            var entry = await diaryEntryRepository.FindByIdAsync(command.DiaryEntryId, cancellationToken);
            if (entry is null) return Failure(IntakeError.DiaryEntryNotFound);
            if (entry.PatientId != command.PatientId) return Failure(IntakeError.PatientWriteOnly);
            if (!entry.HasProposedEstimate) return Failure(IntakeError.EstimateNotProposed);
            if (entry.HasConfirmedEstimate) return Failure(IntakeError.EstimateAlreadyConfirmed);

            var referenceFood = await foodCatalogContextFacade.GetReferenceFoodById(
                command.ReferenceFoodId, cancellationToken);
            if (referenceFood is null) return Failure(IntakeError.ReferenceFoodNotResolved);

            entry.AdjustProposedEstimate(command.ReferenceFoodId, command.PortionGrams, adherence);

            diaryEntryRepository.Update(entry);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Both events are published: the correction is a confirmation with a correction inside
            // it, and Monitoring reacts to the confirmation, not to the size of the correction.
            await mediator.PublishAsync(
                new EstimateAdjustedByPatient(entry.Id.Value, entry.PatientId, command.ReferenceFoodId,
                    command.PortionGrams, entry.PlanAdherence.Value), cancellationToken);
            await mediator.PublishAsync(
                new EstimateConfirmedByPatient(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                    entry.PlanAdherence.Value), cancellationToken);

            return Success(entry);
        }
        catch (InvalidOperationException)
        {
            return Failure(IntakeError.EstimateAlreadyConfirmed);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.ReferenceFoodNotResolved);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not adjust the estimate on entry {DiaryEntryId}",
                command.DiaryEntryId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>Subflow 4.3 - Log Meal Manually.</summary>
    /// <remarks>
    ///     This exists so the photo is not a single point of failure: mixed plates, poor lighting,
    ///     food already eaten. What the patient types is a confirmation from the start, with no
    ///     proposal beside it, because no model was involved.
    /// </remarks>
    public async Task<Result<DiaryEntry, IntakeError>> Handle(LogMealManuallyCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return Failure(IntakeError.PatientWriteOnly);

        // Business rule: Idempotency By Aggregate Id (Subflow 4.6, IN-7). A retry returns the stored entry.
        if (await ReplayAsync(command.PatientId, command.ClientEntryId, cancellationToken) is { } replay)
            return replay;

        var timestamp = BuildLocalTimestamp(command.LocalTimestamp, out var timestampError);
        if (timestamp is null) return Failure(timestampError);

        // Business rule: Plan Adherence Required On Confirmation (IN-1). What the patient types is a
        // confirmation from the start, so it answers the question from the start.
        var adherence = BuildAnswer(command.PlanAdherence, out var adherenceError);
        if (adherence is null) return Failure(adherenceError);

        try
        {
            // Business rule: Food Resolved From Local Catalog (Subflow 4.3)
            var referenceFood = await foodCatalogContextFacade.GetReferenceFoodById(
                command.ReferenceFoodId, cancellationToken);
            if (referenceFood is null) return Failure(IntakeError.ReferenceFoodNotResolved);

            var entry = new DiaryEntry(command.PatientId, timestamp, new Provenance(Provenance.Manual),
                new SyncState(SyncState.Synced), clientEntryId: command.ClientEntryId);

            entry.ConfirmDirectly(new ConfirmedEstimate(command.ReferenceFoodId, command.PortionGrams,
                DateTimeOffset.UtcNow), adherence);

            await diaryEntryRepository.AddAsync(entry, cancellationToken);
            if (await SaveOrFindWinnerAsync(entry, cancellationToken) is { } winner)
                return winner.PatientId == command.PatientId
                    ? Success(winner)
                    : Failure(IntakeError.DuplicatedClientEntryId);

            // ConfirmedOnCreation: the confirmation below follows in this same request, so the day is
            // evaluated once, with its final totals (MA-1).
            await mediator.PublishAsync(
                new MealLogged(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                    entry.Provenance.Value, null, null, null, entry.PlanAdherence.Value,
                    ConfirmedOnCreation: true), cancellationToken);
            await mediator.PublishAsync(
                new EstimateConfirmedByPatient(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                    entry.PlanAdherence.Value), cancellationToken);

            return Success(entry);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.ReferenceFoodNotResolved);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not log a manual meal for patient {PatientId}", command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>IN-6 - Log a meal in a group (§12-#4, option A).</summary>
    /// <remarks>
    ///     DECISIÓN §12-#4: N <c>Manual</c> entries with a shared <c>MealGroupId</c> (option A), not an entry with
    ///     several items: the model, Monitoring and synchronisation stay as they are.
    ///     The order is the specification:
    ///     1. the moment (with the retroactive window, as every interactive log), the items (1 to 10, each with a
    ///     food and a positive portion), the origin and the answer: «¿Estaba en tu plan?» is InPlan by default for an
    ///     idea, which respects the plan by construction (IA-3), and an explicit answer of the patient is kept;
    ///     2. every food resolved in the local catalog (Food Resolved From Local Catalog); one that is not leaves
    ///     nothing stored, and the app lets the patient adjust it (PT8/PT9);
    ///     3. the entries, confirmed from the start, in one save: all of them or none;
    ///     4. the events after the commit: each entry announces itself as a manual log does, but leaves the day to
    ///     <see cref="MealGroupLogged" />, so Monitoring evaluates the day once.
    ///     DECISIÓN IN-6: <c>origin.mealIdeaId</c> is accepted but not stored: the MD adds only <c>origin</c> and
    ///     <c>meal_group_id</c>, and an idea lives two hours in a cache.
    /// </remarks>
    public async Task<Result<MealGroupLogOutcome, IntakeError>> Handle(LogMealGroupManuallyCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return GroupFailure(IntakeError.PatientWriteOnly);

        // 1. Value objects, each to its own error.
        var timestamp = BuildLocalTimestamp(command.LocalTimestamp, out var timestampError);
        if (timestamp is null) return GroupFailure(timestampError);

        var items = command.Items ?? [];
        if (items.Count is < 1 or > MaximumMealGroupItems ||
            items.Any(i => i is null || i.ReferenceFoodId <= 0 || i.PortionGrams <= 0m))
            return GroupFailure(IntakeError.InvalidMealGroupItems);

        EntryOrigin? origin = null;
        if (command.Origin is not null)
            try
            {
                origin = new EntryOrigin(command.Origin.Kind!);
            }
            catch (ArgumentException)
            {
                return GroupFailure(IntakeError.InvalidEntryOrigin);
            }

        // IN-7. Business rule: Idempotency By Aggregate Id (Subflow 4.6), per item: a resent meal returns the
        // entries it already became.
        var replay = await ReplayGroupAsync(command.PatientId, items, cancellationToken);
        if (replay is not null) return replay;

        // Business rule: Plan Adherence Required On Confirmation (IN-1). An idea of IA-3 is in the plan unless the
        // patient says otherwise.
        var declared = string.IsNullOrWhiteSpace(command.PlanAdherence) && origin is { IsMealIdea: true }
            ? PlanAdherence.InPlan
            : command.PlanAdherence;
        var adherence = BuildAnswer(declared, out var adherenceError);
        if (adherence is null) return GroupFailure(adherenceError);

        try
        {
            // 2. Business rule: Food Resolved From Local Catalog (Subflow 4.3), for every item.
            foreach (var referenceFoodId in items.Select(i => i.ReferenceFoodId).Distinct())
                if (await foodCatalogContextFacade.GetReferenceFoodById(referenceFoodId, cancellationToken) is null)
                    return GroupFailure(IntakeError.ReferenceFoodNotResolved);

            // 3. One entry per food, all confirmed, in one save.
            var mealGroupId = Guid.NewGuid();
            var entries = new List<DiaryEntry>();
            foreach (var item in items)
            {
                var entry = new DiaryEntry(command.PatientId, timestamp, new Provenance(Provenance.Manual),
                    new SyncState(SyncState.Synced), clientEntryId: item.ClientEntryId);
                entry.ConfirmDirectly(new ConfirmedEstimate(item.ReferenceFoodId, item.PortionGrams,
                    DateTimeOffset.UtcNow), adherence);
                entry.JoinMealGroup(mealGroupId, origin);

                await diaryEntryRepository.AddAsync(entry, cancellationToken);
                entries.Add(entry);
            }

            try
            {
                await unitOfWork.CompleteAsync(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested &&
                                    items.Any(i => i.ClientEntryId is not null))
            {
                // A concurrent retry of the same meal stored it first: the unique index kept its entries.
                if (await ReplayGroupAsync(command.PatientId, items, cancellationToken) is { } winner) return winner;
                throw;
            }

            // 4. Each entry as a manual log, the day once for the whole meal (MA-1).
            foreach (var entry in entries)
            {
                await mediator.PublishAsync(
                    new MealLogged(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                        entry.Provenance.Value, null, null, null, entry.PlanAdherence.Value,
                        ConfirmedOnCreation: true), cancellationToken);
                await mediator.PublishAsync(
                    new EstimateConfirmedByPatient(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                        entry.PlanAdherence.Value, EvaluatesDay: false), cancellationToken);
            }

            await mediator.PublishAsync(
                new MealGroupLogged(mealGroupId, command.PatientId, timestamp.Value,
                    entries.Select(e => e.Id.Value).ToList(), origin?.Value), cancellationToken);

            return new Result<MealGroupLogOutcome, IntakeError>.Success(new MealGroupLogOutcome(mealGroupId, entries));
        }
        catch (ArgumentException)
        {
            return GroupFailure(IntakeError.InvalidMealGroupItems);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not log a meal group for patient {PatientId}", command.PatientId);
            return GroupFailure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>Subflow 4.4 - Log Off Plan Meal. One tap.</summary>
    /// <remarks>
    ///     Business rules: No Detail Requested, No Deviation Computed, No Visual Penalty and Streak
    ///     Rewards Logging Not Deficit (Subflow 4.4). Notice how short this method is. Nothing asks
    ///     what was eaten, nothing computes anything from it, and the event that leaves carries the
    ///     fact and the moment. The whole point is that declaring costs less than omitting.
    /// </remarks>
    public async Task<Result<DiaryEntry, IntakeError>> Handle(LogOffPlanMealCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return Failure(IntakeError.PatientWriteOnly);

        var timestamp = BuildLocalTimestamp(command.LocalTimestamp, out var timestampError);
        if (timestamp is null) return Failure(timestampError);

        try
        {
            // Deprecated by IN-1: kept working, unchanged, for clients that still send the one tap.
            var entry = DiaryEntry.LegacyOffPlan(command.PatientId, timestamp, new SyncState(SyncState.Synced));

            await diaryEntryRepository.AddAsync(entry, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new MealLogged(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                    entry.Provenance.Value, null, null, null, entry.PlanAdherence.Value), cancellationToken);
            await mediator.PublishAsync(
                new OffPlanEntryLogged(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp),
                cancellationToken);

            return Success(entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not log an off-plan meal for patient {PatientId}",
                command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>Subflow 4.6 - Sync Pending Entries.</summary>
    /// <remarks>
    ///     Each item is its own committed step. That is what makes the batch idempotent item by item,
    ///     and it is also why an interrupted batch leaves a real Pending Sync Queue behind instead of
    ///     losing everything it had already accepted.
    /// </remarks>
    public async Task<Result<SyncOutcome, IntakeError>> Handle(SyncPendingEntriesCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0)
            return new Result<SyncOutcome, IntakeError>.Failure(IntakeError.PatientWriteOnly);

        var outcomes = new List<SyncedEntryOutcome>();
        var seenInThisBatch = new HashSet<Guid>();
        var touchedDays = new SortedSet<DateOnly>();

        foreach (var pending in command.Entries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Business rule: Idempotency By Aggregate Id (Subflow 4.6), read inside one batch. The
            // same client identifier twice in one request is a client defect, not a replay.
            if (!seenInThisBatch.Add(pending.ClientEntryId))
            {
                outcomes.Add(new SyncedEntryOutcome(pending.ClientEntryId, null,
                    SyncedEntryOutcome.Rejected, nameof(IntakeError.DuplicatedClientEntryId)));
                continue;
            }

            try
            {
                var reconciled = await ReconcileAsync(command.PatientId, pending, cancellationToken);
                outcomes.Add(reconciled);

                // The day the entry declared, as the device read it. A rejected entry touched nothing.
                if (reconciled.Outcome != SyncedEntryOutcome.Rejected)
                    touchedDays.Add(DateOnly.FromDateTime(pending.LocalTimestamp.Date));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not reconcile entry {ClientEntryId}", pending.ClientEntryId);
                outcomes.Add(new SyncedEntryOutcome(pending.ClientEntryId, null,
                    SyncedEntryOutcome.Rejected, nameof(IntakeError.UnexpectedError)));
            }
        }

        // MA-1: Late Entry Re Evaluates Its Own Day Only, once per day per batch. The per-entry events
        // above say they leave the evaluation to this announcement.
        if (touchedDays.Count > 0)
            await mediator.PublishAsync(new DiaryBatchSynchronized(command.PatientId, touchedDays.ToList()),
                cancellationToken);

        var outcome = new SyncOutcome(
            command.PatientId,
            outcomes.Count(o => o.Outcome == SyncedEntryOutcome.Created),
            outcomes.Count(o => o.Outcome == SyncedEntryOutcome.AlreadyPresent),
            outcomes.Count(o => o.Outcome == SyncedEntryOutcome.ConflictResolved),
            outcomes.Count(o => o.Outcome == SyncedEntryOutcome.Rejected),
            outcomes);

        return new Result<SyncOutcome, IntakeError>.Success(outcome);
    }

    private async Task<SyncedEntryOutcome> ReconcileAsync(int patientId, PendingDiaryEntry pending,
        CancellationToken cancellationToken)
    {
        var existing = await diaryEntryRepository.FindByClientEntryIdAsync(pending.ClientEntryId,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.PatientId != patientId)
                return Rejected(pending, IntakeError.PatientWriteOnly);

            // Business rule: Declared Local Timestamp Never Rewritten (Subflow 4.6). A resent copy
            // that disagrees about when it happened does not get to move it. The entry stays as the
            // patient first declared it and the disagreement is reported back to the device.
            if (!existing.DeclaredLocalTimestamp.EqualsExact(pending.LocalTimestamp))
                return Rejected(pending, IntakeError.LocalTimestampCannotBeRewritten);

            if (!TryBuildSyncAnswer(pending, out var resentAnswer))
                return Rejected(pending, IntakeError.InvalidPlanAdherence);

            // IN-1: a copy that is still «Por confirmar» on the device carries nothing to reconcile.
            if (pending.ReferenceFoodId is null || pending.PortionGrams is null || pending.Confirmed == false)
            {
                await MarkSynchronizedAsync(existing, cancellationToken);
                return new SyncedEntryOutcome(pending.ClientEntryId, existing.Id.Value,
                    SyncedEntryOutcome.AlreadyPresent, null);
            }

            // Business rule: Last Write Wins (Subflow 4.6), applied to the estimate and to nothing
            // else. The moment is not a field this can reach.
            existing.MarkConflicted();
            // IN-1: the answer travels with the confirmation and is recorded once, never rewritten.
            var changed = existing.ResolveWithLatest(pending.ReferenceFoodId.Value,
                pending.PortionGrams.Value, DateTimeOffset.UtcNow, resentAnswer);
            existing.MarkSynchronized();

            diaryEntryRepository.Update(existing);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new EntrySynchronized(existing.Id.Value, existing.PatientId, existing.DeclaredLocalTimestamp,
                    ReEvaluatesDay: false), cancellationToken);

            if (!changed)
                return new SyncedEntryOutcome(pending.ClientEntryId, existing.Id.Value,
                    SyncedEntryOutcome.AlreadyPresent, null);

            await mediator.PublishAsync(
                new SyncConflictResolved(existing.Id.Value, existing.PatientId, "LastWriteWins"),
                cancellationToken);

            return new SyncedEntryOutcome(pending.ClientEntryId, existing.Id.Value,
                SyncedEntryOutcome.ConflictResolved, null);
        }

        Provenance provenance;
        try
        {
            provenance = new Provenance(pending.Provenance);
        }
        catch (ArgumentException)
        {
            return Rejected(pending, IntakeError.ProvenanceRequired);
        }

        LocalTimestamp localTimestamp;
        try
        {
            // TODO: ambiguity - the retroactive logging window is deliberately not applied here.
            // Interpretation assumed: an entry arriving through synchronisation was created on the
            // device at the moment it declares, so rejecting it would discard something the patient
            // really did record, which is worse than the backfilling the window exists to prevent.
            // The window still applies to every interactive logging endpoint. Source: event storming
            // v3, section 4, hotspot 1.
            localTimestamp = new LocalTimestamp(pending.LocalTimestamp);
        }
        catch (ArgumentException)
        {
            return Rejected(pending, IntakeError.LocalTimestampRequired);
        }

        if (!TryBuildSyncAnswer(pending, out var answer))
            return Rejected(pending, IntakeError.InvalidPlanAdherence);

        // The entry is written in the state it had on the device, then moved forward as its own
        // committed step, so that the queue is real rather than a formality. A queue sent by a client
        // older than IN-1 may still declare OffPlan as a provenance: it is accepted as the legacy entry
        // it is, rather than discarding something the patient recorded.
        var entry = provenance.IsOffPlan
            ? DiaryEntry.LegacyOffPlan(patientId, localTimestamp, new SyncState(SyncState.Pending),
                pending.ClientEntryId)
            : new DiaryEntry(patientId, localTimestamp, provenance, new SyncState(SyncState.Pending),
                pending.PhotoRef, pending.ClientEntryId);

        if (pending.ReferenceFoodId is > 0 && pending.PortionGrams is > 0m && !provenance.IsOffPlan)
        {
            var referenceFood = await foodCatalogContextFacade.GetReferenceFoodById(
                pending.ReferenceFoodId.Value, cancellationToken);
            if (referenceFood is null) return Rejected(pending, IntakeError.ReferenceFoodNotResolved);

            if (pending.Confirmed == false && provenance.IsPhoto && pending.Confidence is not null)
            {
                // IN-1: a photo entry the patient had not confirmed yet («Por confirmar») is stored as
                // the proposal it is, and does not count towards the day until confirmed.
                Confidence confidence;
                try
                {
                    confidence = new Confidence(pending.Confidence.Value);
                }
                catch (ArgumentException)
                {
                    return Rejected(pending, IntakeError.ConfidenceRequired);
                }

                entry.ProposeEstimate(new ProposedEstimate(pending.ReferenceFoodId.Value,
                    pending.PortionGrams.Value, confidence, DateTimeOffset.UtcNow));
            }
            else
            {
                var confirmed = new ConfirmedEstimate(pending.ReferenceFoodId.Value,
                    pending.PortionGrams.Value, DateTimeOffset.UtcNow);

                if (answer is not null) entry.ConfirmDirectly(confirmed, answer);
                else entry.ConfirmFromLegacySync(confirmed);
            }
        }

        await diaryEntryRepository.AddAsync(entry, cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);

        await mediator.PublishAsync(
            new EntryQueuedOffline(entry.Id.Value, entry.PatientId, pending.ClientEntryId),
            cancellationToken);

        await MarkSynchronizedAsync(entry, cancellationToken);

        await mediator.PublishAsync(
            new MealLogged(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp, entry.Provenance.Value,
                null, null, null, entry.PlanAdherence.Value, ViaSynchronization: true), cancellationToken);

        if (entry.Provenance.IsOffPlan)
            await mediator.PublishAsync(
                new OffPlanEntryLogged(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                    ViaSynchronization: true), cancellationToken);

        return new SyncedEntryOutcome(pending.ClientEntryId, entry.Id.Value, SyncedEntryOutcome.Created,
            null);
    }

    private async Task MarkSynchronizedAsync(DiaryEntry entry, CancellationToken cancellationToken)
    {
        entry.MarkSynchronized();
        diaryEntryRepository.Update(entry);
        await unitOfWork.CompleteAsync(cancellationToken);

        // Only reached from synchronisation: the batch announces the day once (MA-1).
        await mediator.PublishAsync(
            new EntrySynchronized(entry.Id.Value, entry.PatientId, entry.DeclaredLocalTimestamp,
                ReEvaluatesDay: false), cancellationToken);
    }

    /// <summary>
    ///     IN-7. Business rule: Idempotency By Aggregate Id (Subflow 4.6) for the interactive logs. The entry a
    ///     <paramref name="clientEntryId" /> already became, as the answer to a retry; null when it is new.
    /// </summary>
    /// <remarks>
    ///     A replay publishes nothing: the entry and its day were announced when it was first stored. An identifier
    ///     another patient already used is a client defect (409), never that patient's entry.
    /// </remarks>
    private async Task<Result<DiaryEntry, IntakeError>?> ReplayAsync(int patientId, Guid? clientEntryId,
        CancellationToken cancellationToken)
    {
        if (clientEntryId is not { } id) return null;
        if (id == Guid.Empty) return Failure(IntakeError.ClientEntryIdRequired);

        try
        {
            var existing = await diaryEntryRepository.FindByClientEntryIdAsync(id, cancellationToken);
            if (existing is null) return null;
            return existing.PatientId == patientId ? Success(existing) : Failure(IntakeError.DuplicatedClientEntryId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not look up a client entry identifier for patient {PatientId}", patientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>
    ///     IN-7. The entries a resent meal group already became: every item with its <c>clientEntryId</c> stored for
    ///     this patient, in one group. Null when none of them is stored (a new meal). Any other mix (some stored,
    ///     another patient's, or an identifier repeated in the request) is <see cref="IntakeError.DuplicatedClientEntryId" />.
    /// </summary>
    private async Task<Result<MealGroupLogOutcome, IntakeError>?> ReplayGroupAsync(int patientId,
        IReadOnlyList<MealGroupItem> items, CancellationToken cancellationToken)
    {
        var ids = items.Where(i => i.ClientEntryId is not null).Select(i => i.ClientEntryId!.Value).ToList();
        if (ids.Count == 0) return null;
        if (ids.Any(id => id == Guid.Empty)) return GroupFailure(IntakeError.ClientEntryIdRequired);
        if (ids.Distinct().Count() != ids.Count) return GroupFailure(IntakeError.DuplicatedClientEntryId);

        try
        {
            var stored = new List<DiaryEntry>();
            foreach (var id in ids)
                if (await diaryEntryRepository.FindByClientEntryIdAsync(id, cancellationToken) is { } entry)
                    stored.Add(entry);

            if (stored.Count == 0) return null;

            var isSameMeal = stored.Count == items.Count && stored.All(e => e.PatientId == patientId) &&
                             stored.Select(e => e.MealGroupId).Distinct().Count() == 1 &&
                             stored[0].MealGroupId is not null;
            return isSameMeal
                ? new Result<MealGroupLogOutcome, IntakeError>.Success(
                    new MealGroupLogOutcome(stored[0].MealGroupId!.Value, stored))
                : GroupFailure(IntakeError.DuplicatedClientEntryId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not look up the client entry identifiers of a meal for patient {PatientId}",
                patientId);
            return GroupFailure(IntakeError.UnexpectedError);
        }
    }

    /// <summary>
    ///     IN-7. Saves a new entry. When the save loses a race against a retry of the same log (the unique index on
    ///     the client identifier or on the photo analysis kept the other row), returns the entry that won; null when
    ///     this one was stored.
    /// </summary>
    /// <remarks>
    ///     The entry that lost was never stored; it stays detached from nothing and nothing else is saved in this
    ///     request, because the winner is returned without publishing anything (Entry Never Deleted: no removal).
    /// </remarks>
    private async Task<DiaryEntry?> SaveOrFindWinnerAsync(DiaryEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.CompleteAsync(cancellationToken);
            return null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested &&
                                (entry.ClientEntryId is not null || entry.MealPhotoAnalysisId is not null))
        {
            var winner = entry.ClientEntryId is { } clientEntryId
                ? await diaryEntryRepository.FindByClientEntryIdAsync(clientEntryId, cancellationToken)
                : null;
            if (winner is null && entry.MealPhotoAnalysisId is { } analysisId)
                winner = await diaryEntryRepository.FindByMealPhotoAnalysisIdAsync(analysisId, cancellationToken);
            if (winner is null || ReferenceEquals(winner, entry)) throw;
            return winner;
        }
    }

    /// <summary>
    ///     Builds the declared moment and applies the retroactive logging window.
    /// </summary>
    /// <remarks>
    ///     TODO: hotspot (event storming 4, hotspot 1) - how far back can a patient log? An unlimited
    ///     window lets somebody backfill the whole week the night before the consultation, which is
    ///     precisely the behaviour this product exists to prevent. Read from
    ///     Intake:RetroactiveLoggingWindowHours, currently 48, which is the upper end of the
    ///     candidate range in the event storming and has not been validated with a practitioner.
    /// </remarks>
    private LocalTimestamp? BuildLocalTimestamp(DateTimeOffset declared, out IntakeError error)
    {
        LocalTimestamp localTimestamp;
        try
        {
            localTimestamp = new LocalTimestamp(declared);
        }
        catch (ArgumentException)
        {
            error = IntakeError.LocalTimestampRequired;
            return null;
        }

        var hours = configuration.GetValue<int?>("Intake:RetroactiveLoggingWindowHours")
                    ?? DefaultRetroactiveLoggingWindowHours;

        if (hours > 0 && localTimestamp.Value < DateTimeOffset.UtcNow.AddHours(-hours))
        {
            error = IntakeError.RetroactiveLoggingWindowExceeded;
            return null;
        }

        error = IntakeError.UnexpectedError;
        return localTimestamp;
    }

    /// <summary>
    ///     IN-1. Builds the patient's answer to «¿Esta comida estaba en tu plan?» for a confirmation.
    /// </summary>
    /// <remarks>
    ///     Missing, blank or NotAnswered is <see cref="IntakeError.PlanAdherenceRequired" />: a
    ///     confirmation always answers the question. Anything else that is not InPlan or OffPlan is
    ///     <see cref="IntakeError.InvalidPlanAdherence" />.
    /// </remarks>
    private static PlanAdherence? BuildAnswer(string? declared, out IntakeError error)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            error = IntakeError.PlanAdherenceRequired;
            return null;
        }

        PlanAdherence adherence;
        try
        {
            adherence = new PlanAdherence(declared);
        }
        catch (ArgumentException)
        {
            error = IntakeError.InvalidPlanAdherence;
            return null;
        }

        if (!adherence.IsAnswered)
        {
            error = IntakeError.PlanAdherenceRequired;
            return null;
        }

        error = IntakeError.UnexpectedError;
        return adherence;
    }

    /// <summary>
    ///     IN-1, synchronisation. The answer is optional here: a queue from an older client never
    ///     asked the question and is accepted as legacy (it stays NotAnswered). A value that is
    ///     present must still be a valid one.
    /// </summary>
    /// <returns>False only when a value was sent and it is not InPlan, OffPlan or NotAnswered.</returns>
    private static bool TryBuildSyncAnswer(PendingDiaryEntry pending, out PlanAdherence? answer)
    {
        answer = null;
        if (string.IsNullOrWhiteSpace(pending.PlanAdherence)) return true;

        try
        {
            var declared = new PlanAdherence(pending.PlanAdherence);
            answer = declared.IsAnswered ? declared : null;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static SyncedEntryOutcome Rejected(PendingDiaryEntry pending, IntakeError error)
    {
        return new SyncedEntryOutcome(pending.ClientEntryId, null, SyncedEntryOutcome.Rejected,
            error.ToString());
    }

    private static Result<DiaryEntry, IntakeError> Success(DiaryEntry entry)
    {
        return new Result<DiaryEntry, IntakeError>.Success(entry);
    }

    private static Result<MealGroupLogOutcome, IntakeError> GroupFailure(IntakeError error)
    {
        return new Result<MealGroupLogOutcome, IntakeError>.Failure(error);
    }

    private static Result<DiaryEntry, IntakeError> Failure(IntakeError error)
    {
        return new Result<DiaryEntry, IntakeError>.Failure(error);
    }
}
