using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;

/// <summary>
///     Background worker of the appointment reminder policy («When The Visit Is Near», Monitoring and Adherence).
///     Nobody triggers it: a visit that is getting close produces no event, so only the clock can notice it.
/// </summary>
/// <remarks>
///     Guards, as in the other workers of this context:
///     1. the whole cycle is wrapped in try/catch, so a failing cycle never brings the host down;
///     2. scoped services are resolved from a scope of its own through IServiceScopeFactory;
///     3. the stopping token is propagated into every call;
///     4. the cycle is idempotent: every delivered reminder is recorded, and the policy never sends a recorded
///     one again;
///     5. without SMTP configuration, or with <c>Reminders:FollowUp:Enabled=false</c>, the service does nothing.
///     Configuration: <c>Reminders:FollowUp:*</c> (see <see cref="FollowUpReminderSettings" />).
/// </remarks>
public class FollowUpReminderHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<FollowUpReminderHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = FollowUpReminderSettings.From(configuration);
        if (!settings.Enabled)
        {
            logger.LogInformation("Appointment reminders are disabled (Reminders:FollowUp:Enabled=false)");
            return;
        }

        var interval = TimeSpan.FromMinutes(settings.IntervalMinutes);
        logger.LogInformation("Appointment reminder policy running every {Interval} for windows of {Windows} minutes",
            interval, string.Join(", ", settings.LeadMinutes));

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
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IFollowUpReminderCommandService>();
            var result = await service.Handle(new SendDueFollowUpRemindersCommand(DateTimeOffset.UtcNow),
                stoppingToken);

            if (result.Sent > 0 || result.Failed > 0)
                logger.LogInformation(
                    "Appointment reminders: {Sent} sent, {Failed} failed, {Evaluated} visits evaluated",
                    result.Sent, result.Failed, result.Evaluated);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The appointment reminder cycle did not complete");
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
