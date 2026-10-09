using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.CareRelationship.Application.Internal.EventHandlers;

/// <summary>
///     IA-1 - Policy "When AI Processing Consent Changed" (CR-2), this context's part:
///     - the preferences follow the switch (all on when granted, all off when withdrawn);
///     - when withdrawn, every row of the technical audit about the patient is purged (§12-#14).
///     The other contexts purge the AI content they keep with their own handlers of the same event.
/// </summary>
public class OnAiProcessingConsentChangedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnAiProcessingConsentChangedHandler> logger) : IEventHandler<AiProcessingConsentChanged>
{
    public async Task Handle(AiProcessingConsentChanged notification, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IAiPreferencesCommandService>();

            var result = await commandService.Handle(
                new SyncAiPreferencesWithConsentCommand(notification.PatientId, notification.Granted),
                cancellationToken);
            if (result.IsFailure)
                logger.LogWarning("Could not sync the AI preferences of patient {PatientId} with the consent",
                    notification.PatientId);

            if (notification.Granted) return;

            var log = scope.ServiceProvider.GetRequiredService<IAiGenerationLog>();
            var purged = await log.PurgeForPatientAsync(notification.PatientId, null, cancellationToken);
            logger.LogInformation("Purged {Count} AI generations of patient {PatientId} after AI consent was withdrawn",
                purged, notification.PatientId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply the AI consent change of patient {PatientId}",
                notification.PatientId);
        }
    }
}
