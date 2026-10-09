using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Scheduling;

/// <summary>
///     NC-10. Recovery of the plan proposal queue: at start-up and every
///     <c>Scheduling:PlanProposalRecoveryIntervalMinutes</c> (30) it queues again the open sustained deviations of the
///     last <c>NutritionalCare:PlanProposalRecoveryHours</c> (72) without a proposal and with the patient's AI consent.
/// </summary>
/// <remarks>
///     The queue is in memory, so a restart loses what was waiting. This worker resolves the review item command
///     service only and issues Recover Plan Proposals, which queues and never touches a plan. Guards, as the other
///     workers: the cycle in try/catch, a scope of its own, the stopping token propagated, and an idempotent cycle (an
///     item with a proposal, or already waiting, is left alone).
/// </remarks>
public class PlanProposalRecoveryHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<PlanProposalRecoveryHostedService> logger) : BackgroundService
{
    public const int DefaultIntervalMinutes = 30;
    public const int DefaultRecoveryHours = 72;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = configuration.GetValue<int?>("Scheduling:PlanProposalRecoveryIntervalMinutes") ??
                      DefaultIntervalMinutes;
        var interval = TimeSpan.FromMinutes(Math.Max(1, minutes));

        logger.LogInformation("Plan proposal recovery running at start-up and every {Interval}", interval);

        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunCycleAsync(stoppingToken);
        } while (await SafeWaitAsync(timer, stoppingToken));
    }

    /// <summary>One cycle. Never throws.</summary>
    public async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            var hours = configuration.GetValue<int?>("NutritionalCare:PlanProposalRecoveryHours") ??
                        DefaultRecoveryHours;

            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IReviewItemCommandService>();

            var result = await commandService.Handle(
                new RecoverPlanProposalsCommand(timeProvider.GetUtcNow(), TimeSpan.FromHours(Math.Max(1, hours))),
                stoppingToken);
            if (result.IsFailure) logger.LogWarning("The plan proposal recovery cycle did not complete");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The plan proposal recovery cycle did not complete");
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
