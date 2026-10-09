using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.CareRelationship.Application.Internal.CommandServices;

public class AiPreferencesCommandService(
    IAiPreferencesRepository preferencesRepository,
    ICareLinkRepository careLinkRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<AiPreferencesCommandService> logger,
    IMediator mediator) : IAiPreferencesCommandService
{
    /// <summary>IA-1 - Update AI Preferences (PT21.IA).</summary>
    public async Task<Result<AiPreferencesStatus, CareRelationshipError>> Handle(UpdateAiPreferencesCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Business rule: AI Preferences Require Consent (IA-1). Checked against the active link, which only
            // the database knows; the aggregate reasserts it.
            // TODO: hotspot. The consent can be withdrawn between this read and the commit. The window closes by
            // policy: the withdrawal publishes AI Processing Consent Changed, which turns every function off after
            // its own commit, and the pipeline's consent policy asks for both on every generation.
            var link = await careLinkRepository.FindActiveByPatientIdAsync(command.PatientId, cancellationToken);
            var consented = link is { HasAiProcessingConsent: true };
            var turnsOn = command.WeeklySummaryEnabled || command.MealIdeasEnabled ||
                          command.SuggestedQuestionsEnabled || command.MealPhotoRecognitionEnabled == true;
            if (turnsOn && !consented)
                return new Result<AiPreferencesStatus, CareRelationshipError>.Failure(
                    CareRelationshipError.AiConsentRequiredToEnableFeature);

            var preferences = await preferencesRepository.FindByPatientIdAsync(command.PatientId, cancellationToken);
            var isNew = preferences is null;
            preferences ??= new AiPreferences(command.PatientId);

            // DECISIÓN IN-7: a PUT without mealPhotoRecognitionEnabled (a client older than IN-7) keeps it as stored.
            var changed = preferences.Change(command.WeeklySummaryEnabled, command.MealIdeasEnabled,
                command.SuggestedQuestionsEnabled, consented, command.MealPhotoRecognitionEnabled);

            await SaveAsync(preferences, isNew, changed, cancellationToken);
            if (changed) await PublishChangedAsync(preferences, cancellationToken);

            return new Result<AiPreferencesStatus, CareRelationshipError>.Success(Status(preferences, consented));
        }
        catch (InvalidOperationException)
        {
            return new Result<AiPreferencesStatus, CareRelationshipError>.Failure(
                CareRelationshipError.AiConsentRequiredToEnableFeature);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating the AI preferences of patient {PatientId}", command.PatientId);
            return new Result<AiPreferencesStatus, CareRelationshipError>.Failure(
                CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>
    ///     IA-1 - Policy "When AI Processing Consent Changed". Granted: the preferences are created or reactivated
    ///     with the three functions on. Withdrawn: all three go off. Idempotent: no change, no event.
    /// </summary>
    public async Task<Result<AiPreferencesStatus, CareRelationshipError>> Handle(
        SyncAiPreferencesWithConsentCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var preferences = await preferencesRepository.FindByPatientIdAsync(command.PatientId, cancellationToken);

            // Never had any and consent goes off: off is already the state.
            if (preferences is null && !command.AiProcessingGranted)
                return new Result<AiPreferencesStatus, CareRelationshipError>.Success(
                    AiPreferencesStatus.Of(command.PatientId, false, false, false, false));

            var isNew = preferences is null;
            preferences ??= new AiPreferences(command.PatientId);

            var changed = command.AiProcessingGranted ? preferences.EnableAll() : preferences.DisableAll();

            await SaveAsync(preferences, isNew, changed, cancellationToken);
            if (changed) await PublishChangedAsync(preferences, cancellationToken);

            return new Result<AiPreferencesStatus, CareRelationshipError>.Success(
                Status(preferences, command.AiProcessingGranted));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error syncing the AI preferences of patient {PatientId} with the consent",
                command.PatientId);
            return new Result<AiPreferencesStatus, CareRelationshipError>.Failure(
                CareRelationshipError.UnexpectedError);
        }
    }

    private async Task SaveAsync(AiPreferences preferences, bool isNew, bool changed,
        CancellationToken cancellationToken)
    {
        if (!isNew && !changed) return;

        if (isNew) await preferencesRepository.AddAsync(preferences, cancellationToken);
        else preferencesRepository.Update(preferences);
        await unitOfWork.CompleteAsync(cancellationToken);
    }

    private async Task PublishChangedAsync(AiPreferences preferences, CancellationToken cancellationToken)
    {
        await mediator.PublishAsync(
            new AiPreferencesChanged(preferences.PatientId, preferences.WeeklySummaryEnabled,
                preferences.MealIdeasEnabled, preferences.SuggestedQuestionsEnabled, timeProvider.GetUtcNow(),
                preferences.MealPhotoRecognitionEnabled),
            cancellationToken);
    }

    private static AiPreferencesStatus Status(AiPreferences preferences, bool consented)
    {
        return AiPreferencesStatus.Of(preferences.PatientId, consented, preferences.WeeklySummaryEnabled,
            preferences.MealIdeasEnabled, preferences.SuggestedQuestionsEnabled,
            preferences.MealPhotoRecognitionEnabled);
    }
}
