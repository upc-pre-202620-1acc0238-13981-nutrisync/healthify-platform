using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.CareRelationship.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Active Targets Updated" (Care Relationship, Subflow 2.4): a new published version
///     starts waiting for the patient to acknowledge it.
/// </summary>
/// <remarks>
///     Integration event 6 of 13, and the reason this handler belongs to Phase 3 rather than Phase 2:
///     it subscribes to an event that Nutritional Care owns, so it could not exist before that
///     context did.
///     Acknowledging is an act of the relationship, not of the clinical act, which is why the pending
///     flag lives on the care link and not on the plan. Nothing here reaches back into Nutritional
///     Care, and the plan does not change because the patient read it.
///     This handler imports only <c>NutritionalCare.Domain.Model.Events</c>, which is one of the two
///     cross-context imports the platform allows.
/// </remarks>
public class OnActiveTargetsUpdatedCareRelationshipHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnActiveTargetsUpdatedCareRelationshipHandler> logger) : IEventHandler<ActiveTargetsUpdated>
{
    public async Task Handle(ActiveTargetsUpdated notification, CancellationToken cancellationToken)
    {
        // Isolated DI scope with its own DbContext: this event has three subscribers and they are
        // handled in parallel, so sharing the request-scoped DbContext would raise a concurrency
        // error.
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<ICareLinkCommandService>();

        var result = await commandService.Handle(
            new MarkTargetsPendingAcknowledgementCommand(notification.PatientId, notification.PlanVersion),
            cancellationToken);

        if (result.IsFailure)
            logger.LogWarning(
                "Could not mark targets version {PlanVersion} as pending for patient {PatientId}",
                notification.PlanVersion, notification.PatientId);
    }
}
