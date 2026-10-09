using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Maintenance;

/// <summary>What one run of the one-shot recalculation did.</summary>
/// <param name="Patients">Patients with at least one reading.</param>
/// <param name="Recalculated">Trends rebuilt successfully.</param>
/// <param name="FailedPatientIds">Patients whose trend could not be rebuilt. Safe to rerun.</param>
public record WeightTrendRecalculationReport(int Patients, int Recalculated, IReadOnlyList<int> FailedPatientIds);

/// <summary>
///     IN-3. One-shot job that issues Recalculate Weight Trend for every patient with readings, so the trends
///     computed under the three-condition protocol are rebuilt under the configured one (fasted only).
/// </summary>
/// <remarks>
///     Never runs at start-up and has no endpoint. It runs only when the host is started with the
///     <see cref="CommandLineVerb" /> argument (see <c>Program.cs</c>), and then the host exits without serving.
///     Idempotent: the trend is rebuilt from scratch from every reading, so running it twice changes nothing
///     the second time. Each patient gets a scope of its own, so one failing patient does not stop the rest
///     and no DbContext grows with the whole population.
///     A rebuilt trend publishes Weight Trend Recalculated like any other recalculation, so Monitoring
///     recomputes the consistency index of each patient as it would after a new reading.
/// </remarks>
public class WeightTrendRecalculationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<WeightTrendRecalculationJob> logger)
{
    public const string CommandLineVerb = "recalculate-weight-trends";

    public async Task<WeightTrendRecalculationReport> RunAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<int> patientIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            patientIds = await scope.ServiceProvider.GetRequiredService<ISelfWeighInQueryService>()
                .Handle(new GetPatientIdsWithSelfWeighInsQuery(), cancellationToken);
        }

        logger.LogInformation("Recalculating the weight trend of {Count} patients", patientIds.Count);

        var recalculated = 0;
        var failed = new List<int>();

        foreach (var patientId in patientIds)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var commandService = scope.ServiceProvider.GetRequiredService<IWeightTrendCommandService>();

                var result = await commandService.Handle(new RecalculateWeightTrendCommand(patientId),
                    cancellationToken);

                if (result.IsSuccess) recalculated++;
                else failed.Add(patientId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not recalculate the weight trend of patient {PatientId}", patientId);
                failed.Add(patientId);
            }
        }

        if (failed.Count > 0)
            logger.LogWarning("Weight trends not recalculated for patients {PatientIds}; the job can be rerun",
                string.Join(", ", failed));

        return new WeightTrendRecalculationReport(patientIds.Count, recalculated, failed);
    }
}
