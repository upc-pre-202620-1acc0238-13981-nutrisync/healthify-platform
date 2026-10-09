using Healthify.Platform.Iam.Application.CommandServices;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Events;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;

namespace Healthify.Platform.Iam.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Role Claim Issued" (Iam, Subflow 1.2): the session that just received a role
///     claim gets the one navigation shell that role may mount.
/// </summary>
/// <remarks>
///     The only policy of this bounded context, and it does not cross a boundary: producer and
///     subscriber are both Iam.
/// </remarks>
public class OnRoleClaimIssuedHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OnRoleClaimIssuedHandler> logger) : IEventHandler<RoleClaimIssued>
{
    public async Task Handle(RoleClaimIssued notification, CancellationToken cancellationToken)
    {
        // Isolated DI scope with its own DbContext: notifications are handled in parallel, and
        // sharing the request-scoped DbContext would raise a concurrency error.
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IUserSessionCommandService>();

        var shell = NavigationShell.ForRole(new Role(notification.Role));
        var result = await commandService.Handle(
            new SelectNavigationShellCommand(notification.SessionId, shell.Value), cancellationToken);

        if (result.IsFailure)
            logger.LogWarning("Could not select a navigation shell for session {SessionId}",
                notification.SessionId);
    }
}
