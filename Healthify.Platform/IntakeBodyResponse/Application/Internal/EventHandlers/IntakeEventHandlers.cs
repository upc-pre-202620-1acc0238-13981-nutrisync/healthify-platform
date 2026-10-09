using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Active Targets Updated" (Intake and Body Response, Subflow 4.1).
/// </summary>
/// <remarks>
///     Integration event 4 of 13. This is the only place where anything from Nutritional Care enters
///     this context, and it enters as a published event rather than a query. What it carries is the
///     whole of what the patient ever learns about their plan: the targets, the guidelines and the
///     restrictions. No diagnosis, no rationale, no calculation basis.
/// </remarks>
public class OnActiveTargetsUpdatedIntakeHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnActiveTargetsUpdatedIntakeHandler> logger) : IEventHandler<ActiveTargetsUpdated>
{
    public async Task Handle(ActiveTargetsUpdated notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider
            .GetRequiredService<IActiveTargetsCacheCommandService>();

        var result = await commandService.Handle(
            new RefreshActiveTargetsCacheCommand(
                notification.PatientId,
                notification.PlanVersion,
                notification.ValidFrom,
                notification.DailyTargets.EnergyKcal,
                notification.DailyTargets.ProteinG,
                notification.DailyTargets.CarbG,
                notification.DailyTargets.FatG,
                notification.Guidelines,
                notification.Restrictions,
                notification.GuidelineItems?.Select(g => new CachedGuideline(g.Code, g.Custom)).ToList(),
                notification.LegacyRestrictions,
                notification.ChangesFromPrevious?.Select(c =>
                    new CachedPlanChange(c.Type, c.Code, c.Custom, c.Macro, c.From, c.To)).ToList(),
                notification.PatientMessage),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not refresh the active targets cache of patient {PatientId}",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When Meal Logged And Provenance Is Photo" (Intake and Body Response, Subflow 4.2).
/// </summary>
/// <remarks>
///     IN-7: the proposal is either the AI's, made on the server from the photo before the entry existed
///     (<c>meal-photo-analyses</c>), or, in the legacy flow, what the device computed. Either way it arrives
///     with the event. NOTE (legacy flow): portion estimation runs on-device; the server persists the proposal only. This
///     handler makes no network call, runs no model and reaches no AI service. It takes what the
///     device already computed, has the food resolved against the local catalog, and stores the
///     result as a proposal.
///     The provenance guard is the policy trigger itself. A manual entry is confirmed from the start
///     and an off-plan entry has no detail at all, so neither has anything to estimate.
/// </remarks>
public class OnMealLoggedPhotoEstimationHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnMealLoggedPhotoEstimationHandler> logger) : IEventHandler<MealLogged>
{
    public async Task Handle(MealLogged notification, CancellationToken cancellationToken)
    {
        if (notification.Provenance != Provenance.Photo) return;

        // IN-2: confirmed on the device and saved in one transaction, proposal included. There is
        // nothing left to store, and trying would collide with the patient's confirmation.
        if (notification.ConfirmedOnCreation) return;

        if (notification.ProposedReferenceFoodId is null || notification.ProposedPortionGrams is null
                                                         || notification.ProposedConfidence is null)
        {
            logger.LogWarning("Photo entry {DiaryEntryId} arrived without an on-device estimate",
                notification.DiaryEntryId);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IDiaryEntryCommandService>();

        var result = await commandService.Handle(
            new EstimatePortionCommand(
                notification.DiaryEntryId,
                notification.ProposedReferenceFoodId.Value,
                notification.ProposedPortionGrams.Value,
                notification.ProposedConfidence.Value),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not store the proposed estimate on entry {DiaryEntryId}",
                notification.DiaryEntryId);
    }
}

/// <summary>
///     Policy "When Self Weigh In Recorded" (Intake and Body Response, Subflow 4.5).
/// </summary>
/// <remarks>
///     It runs for every reading, including the ones taken outside the protocol. That is deliberate:
///     the aggregate is the one that decides what smooths the trend, and a reading that does not
///     smooth it still needs the trend recalculated around it so the chart stays consistent with the
///     data behind it.
/// </remarks>
public class OnSelfWeighInRecordedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnSelfWeighInRecordedHandler> logger) : IEventHandler<SelfWeighInRecorded>
{
    public async Task Handle(SelfWeighInRecorded notification, CancellationToken cancellationToken)
    {
        // IN-4: a reading from a synchronisation batch leaves the recalculation to the batch, once.
        if (notification.ViaSynchronization) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IWeightTrendCommandService>();

        var result = await commandService.Handle(
            new RecalculateWeightTrendCommand(notification.PatientId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not recalculate the weight trend of patient {PatientId}",
                notification.PatientId);
    }
}

/// <summary>
///     IN-4. Policy "When Self Weigh In Batch Synchronized". Internal to this context.
/// </summary>
/// <remarks>
///     Recalculates the trend once for the whole batch: the readings of a week spent offline change the
///     same series, and rebuilding it once per reading would do the same work N times.
/// </remarks>
public class OnSelfWeighInBatchSynchronizedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnSelfWeighInBatchSynchronizedHandler> logger) : IEventHandler<SelfWeighInBatchSynchronized>
{
    public async Task Handle(SelfWeighInBatchSynchronized notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IWeightTrendCommandService>();

        var result = await commandService.Handle(
            new RecalculateWeightTrendCommand(notification.PatientId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not recalculate the weight trend of patient {PatientId} after a sync",
                notification.PatientId);
    }
}

/// <summary>
///     Policy "When AI Processing Consent Withdrawn" (CR-2, §12-#14), this context's part: purge the meal ideas
///     (IA-3) it keeps for the patient.
/// </summary>
/// <remarks>
///     The ideas live only in the two-hour cache, so the purge evicts it. The technical audit (<c>ai_generations</c>)
///     is purged by Care Relationship. Never throws: a failed purge is logged, and the consent gate still refuses to
///     serve what is left.
/// </remarks>
public class OnAiProcessingConsentChangedIntakeHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnAiProcessingConsentChangedIntakeHandler> logger) : IEventHandler<AiProcessingConsentChanged>
{
    public async Task Handle(AiProcessingConsentChanged notification, CancellationToken cancellationToken)
    {
        if (notification.Granted) return;

        await MealIdeasPurge.Run(scopeFactory, logger, notification.PatientId, cancellationToken);
        // IN-7: the temporary photo analyses go with the consent. The foods created from them stay: they are the
        // shared catalog and hold nothing of the patient.
        await MealPhotoAnalysesPurge.Run(scopeFactory, logger, notification.PatientId, cancellationToken);
    }
}

/// <summary>IA-1. Meal ideas (IA-3) going off stop using the diary, and the cached ideas are purged.</summary>
/// <remarks>Same purge as <see cref="OnAiProcessingConsentChangedIntakeHandler" />.</remarks>
public class OnAiPreferencesChangedIntakeHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnAiPreferencesChangedIntakeHandler> logger) : IEventHandler<AiPreferencesChanged>
{
    public async Task Handle(AiPreferencesChanged notification, CancellationToken cancellationToken)
    {
        if (!notification.MealIdeasEnabled)
            await MealIdeasPurge.Run(scopeFactory, logger, notification.PatientId, cancellationToken);

        // IN-7: «Reconocer comidas por foto» going off deletes the temporary photo analyses.
        if (!notification.MealPhotoRecognitionEnabled)
            await MealPhotoAnalysesPurge.Run(scopeFactory, logger, notification.PatientId, cancellationToken);
    }
}

/// <summary>IN-7, §12-#14. The purge of the photo analyses both policies above issue, in a scope of its own.</summary>
internal static class MealPhotoAnalysesPurge
{
    public static async Task Run(IServiceScopeFactory scopeFactory, ILogger logger, int patientId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IMealPhotoAnalysisCommandService>();

            var result = await commandService.Handle(new PurgeMealPhotoAnalysesCommand(patientId), cancellationToken);
            if (result is Result<int, IntakeError>.Success { Value: > 0 } purged)
                logger.LogInformation("Purged {Count} meal photo analyses of patient {PatientId}", purged.Value,
                    patientId);
            else if (result.IsFailure)
                logger.LogWarning("Could not purge the meal photo analyses of patient {PatientId}", patientId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not purge the meal photo analyses of patient {PatientId}", patientId);
        }
    }
}

/// <summary>IA-3, §12-#14. The purge both policies above issue, in a scope of its own.</summary>
internal static class MealIdeasPurge
{
    public static async Task Run(IServiceScopeFactory scopeFactory, ILogger logger, int patientId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IMealIdeasCommandService>();

            var result = await commandService.Handle(new PurgeMealIdeasCommand(patientId), cancellationToken);
            if (result is Result<int, IntakeError>.Success { Value: > 0 } purged)
                logger.LogInformation("Purged {Count} cached meal idea answer(s) of patient {PatientId}",
                    purged.Value, patientId);
            else if (result.IsFailure)
                logger.LogWarning("Could not purge the meal ideas of patient {PatientId}", patientId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not purge the meal ideas of patient {PatientId}", patientId);
        }
    }
}
