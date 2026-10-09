using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Scheduling;

/// <summary>
///     IN-7. Policy "When A Photo Analysis Expires": deletes the meal photo analyses whose lifetime (24 hours by
///     default) has passed, every <c>Scheduling:MealPhotoAnalysisPurgeIntervalMinutes</c> (60). Runs even with AI off,
///     so analyses made while it was on still go.
/// </summary>
/// <remarks>
///     Same guards as the other workers: the cycle body never brings the host down, the command service is resolved
///     from a scope of its own, the stopping token reaches every call, and the cycle is idempotent.
/// </remarks>
public class MealPhotoAnalysisPurgeHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MealPhotoAnalysisPurgeHostedService> logger) : BackgroundService
{
    public const int DefaultIntervalMinutes = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = configuration.GetValue<int?>("Scheduling:MealPhotoAnalysisPurgeIntervalMinutes")
                      ?? DefaultIntervalMinutes;
        var interval = TimeSpan.FromMinutes(Math.Max(1, minutes));

        logger.LogInformation("Meal photo analysis purge running every {Interval}", interval);

        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunCycleAsync(stoppingToken);
        } while (await SafeWaitAsync(timer, stoppingToken));
    }

    public async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var commandService = scope.ServiceProvider.GetRequiredService<IMealPhotoAnalysisCommandService>();
            var result = await commandService.Handle(new PurgeExpiredMealPhotoAnalysesCommand(), stoppingToken);
            if (result is Result<int, IntakeError>.Success { Value: > 0 } purged)
                logger.LogInformation("Purged {Count} expired meal photo analyses", purged.Value);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The meal photo analysis purge cycle did not complete");
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
