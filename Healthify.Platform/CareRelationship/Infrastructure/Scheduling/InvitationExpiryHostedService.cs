using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;

namespace Healthify.Platform.CareRelationship.Infrastructure.Scheduling;

/// <summary>
///     Background worker for the time-driven policy of this bounded context.
/// </summary>
/// <remarks>
///     Implements the policy "When Expiration Date Reached" (Care Relationship, Subflow 2.1), which
///     issues Expire Invitation. Nobody triggers it: neither a user nor an event, only the passage
///     of time, which is why it is a hosted service and not an event handler.
///     Guards, in the order the platform requires them:
///     1. the whole cycle body is wrapped in try/catch, so an failing cycle never brings the host down;
///     2. scoped services are resolved from a scope of its own through IServiceScopeFactory;
///     3. the stopping token is propagated into every call;
///     4. the cycle is idempotent, because expiring an already expired invitation is a no-op that
///     publishes no second event;
///     5. EF migrations have already run in the composition root before the host starts.
/// </remarks>
public class InvitationExpiryHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<InvitationExpiryHostedService> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = configuration.GetValue<int?>("Scheduling:InvitationExpiryIntervalMinutes") ?? 60;
        var interval = TimeSpan.FromMinutes(Math.Max(1, minutes));

        logger.LogInformation("Invitation expiry policy running every {Interval}", interval);

        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunCycleAsync(stoppingToken);
        } while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Guard 2: a scope of its own, so this worker never shares a request DbContext.
            await using var scope = scopeFactory.CreateAsyncScope();
            var queryService = scope.ServiceProvider.GetRequiredService<IInvitationQueryService>();
            var commandService = scope.ServiceProvider.GetRequiredService<IInvitationCommandService>();

            var due = await queryService.Handle(
                new GetExpirableInvitationsQuery(DateTimeOffset.UtcNow, BatchSize), stoppingToken);

            foreach (var invitation in due)
            {
                if (stoppingToken.IsCancellationRequested) return;

                var result = await commandService.Handle(
                    new ExpireInvitationCommand(invitation.Id.Value), stoppingToken);

                if (result.IsFailure)
                    logger.LogWarning("Could not expire invitation {InvitationId}", invitation.Id.Value);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            // Guard 1: the host stays up whatever happens in here.
            logger.LogError(ex, "The invitation expiry cycle did not complete");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
