using Healthify.Platform.FoodCatalog.Application.CommandServices;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;

namespace Healthify.Platform.FoodCatalog.Infrastructure.Scheduling;

/// <summary>
///     Background worker for the time-driven policy of this bounded context.
/// </summary>
/// <remarks>
///     Implements the policy "When Scheduled Import Due" (Food Catalog, Subflow 6.1), which issues
///     Import Catalog Snapshot. Nobody triggers it: neither a user nor an event, only the passage of
///     time, which is why it is a hosted service and not an event handler.
///     Guards, in the order the platform requires them:
///     1. the whole cycle body is wrapped in try/catch, so a failing cycle never brings the host down,
///     and an unreachable external provider is the expected case rather than an incident;
///     2. scoped services are resolved from a scope of its own through IServiceScopeFactory;
///     3. the stopping token is propagated into every call;
///     4. the cycle is idempotent, because each upstream record carries a stable digest and Cache
///     Food Locally treats a digest it already holds as a no-op that publishes no second event;
///     5. EF migrations have already run in the composition root before the host starts.
/// </remarks>
public class CatalogImportHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<CatalogImportHostedService> logger) : BackgroundService
{
    private const int RecordsPerProvider = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = configuration.GetValue<int?>("Scheduling:CatalogImportIntervalHours") ?? 24;
        var interval = TimeSpan.FromHours(Math.Max(1, hours));

        logger.LogInformation("Scheduled catalog import policy running every {Interval}", interval);

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
            var commandService = scope.ServiceProvider.GetRequiredService<IReferenceFoodCommandService>();

            // TODO: hotspot (event storming 6, hotspot 1) - Peruvian market coverage. The scheduled
            // snapshot asks the providers for the country slice, which is the widest term available
            // without a curated list. Whether that list should exist, and who curates it, is open.
            var term = configuration["OpenFoodFacts:Country"] ?? "peru";

            var result = await commandService.Handle(
                new ImportCatalogSnapshotCommand(term, RecordsPerProvider), stoppingToken);

            result.Match(
                summary => logger.LogInformation(
                    "Scheduled import for {Term}: {Translated} translated, {Failed} dropped, across {Providers} providers",
                    summary.Term, summary.TranslatedCount, summary.FailedCount, summary.ProvidersConsulted),
                error => logger.LogWarning("The scheduled catalog import reported {Error}", error));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Not an error.
        }
        catch (Exception ex)
        {
            // Guard 1: the host stays up whatever happens in here.
            logger.LogError(ex, "The scheduled catalog import cycle did not complete");
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
