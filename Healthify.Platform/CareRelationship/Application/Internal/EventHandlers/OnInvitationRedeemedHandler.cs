using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.CareRelationship.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Invitation Redeemed" (Care Relationship, Subflow 2.2): redeeming the QR code is
///     what creates the care link. This is the only path into one, which is how the rule that a
///     patient cannot create their own link is enforced structurally.
/// </summary>
public class OnInvitationRedeemedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnInvitationRedeemedHandler> logger) : IEventHandler<InvitationRedeemed>
{
    public async Task Handle(InvitationRedeemed notification, CancellationToken cancellationToken)
    {
        // Isolated DI scope with its own DbContext: notifications are handled in parallel, and
        // sharing the request-scoped DbContext would raise a concurrency error.
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<ICareLinkCommandService>();

        var result = await commandService.Handle(
            new EstablishCareLinkCommand(notification.PatientId, notification.PractitionerId,
                notification.InvitationId), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning(
                "Could not establish a care link for patient {PatientId} after invitation {InvitationId} was redeemed",
                notification.PatientId, notification.InvitationId);
    }
}
