using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When AI Processing Consent Withdrawn" (CR-2, §12-#14), this context's part: purge the AI suggestions
///     it keeps for the patient (IA-6 diagnosis suggestion, IA-7 guideline suggestions, IA-8 plan adjustment
///     proposal) that no practitioner accepted. What the practitioner accepted is the practitioner's clinical act and
///     stays.
/// </summary>
/// <remarks>
///     IA-6 and IA-7 store nothing in this context: their suggestions are shown and only what the practitioner saves
///     (a clinical act) is kept. IA-8: the proposals still Proposed or Dismissed are deleted; the accepted ones
///     produced a version of the plan and stay; the review items are untouched (PR14 without AI). The event is also
///     published when the link ends (any reason) or at discharge (rule «AI Processing Ends With The Link»), so this
///     one handler covers the three cases. The practitioner functions read no AI preference (§12-#5), so there is no
///     handler of AI Preferences Changed here. The technical audit (<c>ai_generations</c>) is purged by Care
///     Relationship.
/// </remarks>
public class OnAiProcessingConsentChangedNutritionalCareHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnAiProcessingConsentChangedNutritionalCareHandler> logger) : IEventHandler<AiProcessingConsentChanged>
{
    public async Task Handle(AiProcessingConsentChanged notification, CancellationToken cancellationToken)
    {
        if (notification.Granted) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IReviewItemCommandService>();

            var result = await commandService.Handle(
                new PurgeUnacceptedPlanProposalsCommand(notification.PatientId), cancellationToken);

            if (result is Result<int, NutritionalCareError>.Success purged)
                logger.LogInformation("AI processing ended for patient {PatientId}: {Count} plan proposal(s) purged",
                    notification.PatientId, purged.Value);
            else
                logger.LogWarning("AI processing ended for patient {PatientId}: the plan proposals were not purged",
                    notification.PatientId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI processing ended for patient {PatientId}: the plan proposals were not purged",
                notification.PatientId);
        }
    }
}
