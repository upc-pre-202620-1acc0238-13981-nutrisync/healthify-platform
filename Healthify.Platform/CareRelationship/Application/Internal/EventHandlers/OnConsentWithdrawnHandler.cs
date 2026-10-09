using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.CareRelationship.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Consent Withdrawn" (Care Relationship, Subflow 2.5): withdrawing consent revokes
///     the link. Revoke Care Link has no endpoint precisely because it is never a separate decision.
/// </summary>
public class OnConsentWithdrawnHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnConsentWithdrawnHandler> logger) : IEventHandler<ConsentWithdrawn>
{
    public async Task Handle(ConsentWithdrawn notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<ICareLinkCommandService>();

        var result = await commandService.Handle(
            new RevokeCareLinkCommand(notification.CareLinkId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not revoke care link {CareLinkId} after consent was withdrawn",
                notification.CareLinkId);
    }
}
