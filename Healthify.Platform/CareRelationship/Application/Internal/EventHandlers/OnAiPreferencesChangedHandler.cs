using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.CareRelationship.Application.Internal.EventHandlers;

/// <summary>
///     IA-1. A patient function that is off no longer uses the diary ("dejamos de usar tu diario para estas
///     funciones"): the technical audit rows of each function that is off are purged. Idempotent; the contexts of
///     the functions purge their own content with their handlers of the same event.
/// </summary>
public class OnAiPreferencesChangedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnAiPreferencesChangedHandler> logger) : IEventHandler<AiPreferencesChanged>
{
    public async Task Handle(AiPreferencesChanged notification, CancellationToken cancellationToken)
    {
        var disabled = new List<AiFeature>();
        if (!notification.WeeklySummaryEnabled) disabled.Add(AiFeature.WeeklySummary);
        if (!notification.MealIdeasEnabled) disabled.Add(AiFeature.MealIdeas);
        if (!notification.SuggestedQuestionsEnabled) disabled.Add(AiFeature.SuggestedQuestions);
        if (!notification.MealPhotoRecognitionEnabled) disabled.Add(AiFeature.MealPhotoRecognition);
        if (disabled.Count == 0) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var log = scope.ServiceProvider.GetRequiredService<IAiGenerationLog>();
            foreach (var feature in disabled)
                await log.PurgeForPatientAsync(notification.PatientId, feature, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not purge the AI generations of the functions patient {PatientId} turned off",
                notification.PatientId);
        }
    }
}
