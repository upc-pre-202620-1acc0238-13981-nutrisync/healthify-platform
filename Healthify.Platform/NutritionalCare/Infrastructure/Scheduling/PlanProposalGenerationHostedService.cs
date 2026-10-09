using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Scheduling;

/// <summary>
///     NC-10. Background worker that generates the AI plan proposal (IA-8) of each sustained deviation queued by
///     <c>OnSustainedDeviationDetectedHandler</c>, one at a time.
/// </summary>
/// <remarks>
///     Business rule: No Signal Or Algorithm Modifies The Plan Without An Explicit Action Of The Practitioner (NC-10).
///     This worker resolves the review item command service and issues Generate Plan Proposal, which attaches a
///     proposal to the item; no plan command is reachable from here.
///     Guards: each generation in a scope of its own; any failure is logged and the item stays without a proposal
///     (PR14 without AI); the item leaves the pending set whatever happens, so <c>GET /plan-proposal</c> stops
///     answering 202.
/// </remarks>
public class PlanProposalGenerationHostedService(
    IPlanProposalGenerationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PlanProposalGenerationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int reviewItemId;
            try
            {
                reviewItemId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await GenerateAsync(reviewItemId, stoppingToken);
        }
    }

    /// <summary>One generation. Never throws.</summary>
    public async Task GenerateAsync(int reviewItemId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IReviewItemCommandService>();
            var result = await commandService.Handle(new GeneratePlanProposalCommand(reviewItemId), stoppingToken);
            if (result.IsFailure)
                logger.LogInformation("Review item {ReviewItemId} has no plan proposal", reviewItemId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The plan proposal of review item {ReviewItemId} could not be generated",
                reviewItemId);
        }
        finally
        {
            queue.Complete(reviewItemId);
        }
    }
}
