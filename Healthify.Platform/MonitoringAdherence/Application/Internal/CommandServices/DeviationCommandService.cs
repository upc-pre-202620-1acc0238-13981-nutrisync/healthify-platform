using Cortex.Mediator;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;

/// <summary>
///     Subflow 5.6.
/// </summary>
/// <remarks>
///     Business rule: Never Evaluated Under Seven Days (Subflow 5.6), and invariant 1. The first
///     thing Detect Deviation does is ask the window how long it has been running, and it stops there
///     if the answer is less than the configured span. A deviation of one day is not a deviation, it
///     is a Tuesday.
///     Business rule: Sustained If Persists Across Majority Of Window (Subflow 5.6). Only a sustained
///     deviation crosses the boundary, and it crosses exactly once, because the aggregate returns
///     true only on the transition.
/// </remarks>
public class DeviationCommandService(
    IDeviationRepository deviationRepository,
    IEvaluationWindowRepository evaluationWindowRepository,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<DeviationCommandService> logger,
    IMediator mediator) : IDeviationCommandService
{
    /// <summary>Subflow 5.6 - Detect Deviation.</summary>
    public async Task<Result<Deviation, MonitoringError>> Handle(DetectDeviationCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var window = await evaluationWindowRepository.FindOpenByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (window is null) return Failure(MonitoringError.EvaluationWindowNotFound);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Business rule: Never Evaluated Under Seven Days (Monitoring and Adherence, Subflow
            // 5.6), and invariant 1 of this bounded context.
            if (!window.HasMinimumSpan(today)) return Failure(MonitoringError.InsufficientWindowLength);

            var horizon = window.HorizonDays(today);

            // Business rule: Only Logged Days Count (Monitoring and Adherence, Subflow 5.6). Silence
            // is not evidence, so a horizon with nothing written in it yields nothing to interpret.
            if (horizon.Count(d => d.IsLogged) == 0) return Failure(MonitoringError.NoLoggedDays);

            var detected = Deviation.DetectFrom(window.Id, window.PatientId, horizon);
            if (detected is null) return Failure(MonitoringError.NoLoggedDays);

            var existing = await deviationRepository.FindLatestByWindowAndDirectionAsync(window.Id,
                detected.Direction, cancellationToken);

            if (existing is not null)
            {
                // The same tendency, restated with the horizon as it stands now. Detect Deviation
                // runs after every evaluated day, so without this the same tendency would leave a
                // new row behind every meal.
                if (!existing.Restate(detected.Magnitude, detected.LoggedDaysConsidered,
                        detected.DeviatingDaysConsidered))
                    return Success(existing);

                deviationRepository.Update(existing);
                await unitOfWork.CompleteAsync(cancellationToken);

                await PublishDetected(existing, cancellationToken);
                return Success(existing);
            }

            await deviationRepository.AddAsync(detected, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await PublishDetected(detected, cancellationToken);
            return Success(detected);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.NoLoggedDays);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not detect a deviation for patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>Subflow 5.6 - Flag Sustained Deviation.</summary>
    public async Task<Result<Deviation, MonitoringError>> Handle(FlagSustainedDeviationCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deviation = await deviationRepository.FindByIdAsync(command.DeviationId,
                cancellationToken);
            if (deviation is null) return Failure(MonitoringError.DeviationNotFound);

            var ratio = configuration.GetValue<decimal?>("Monitoring:SustainedDeviationRatio") ?? 0.5m;

            // Business rule: Sustained If Persists Across Majority Of Window (Monitoring and
            // Adherence, Subflow 5.6). The aggregate owns the comparison and returns true only on
            // the transition, so the signal leaves this context exactly once.
            if (!deviation.MarkSustained(ratio)) return Success(deviation);

            deviationRepository.Update(deviation);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Integration event 12 of 13. It notifies; it cannot modify a plan, and there is no
            // command in this context that could.
            await mediator.PublishAsync(
                new SustainedDeviationDetected(deviation.Id.Value, deviation.PatientId,
                    deviation.Magnitude.RelativeValue, deviation.Direction.Value, deviation.Evidence(),
                    deviation.DeviatingDaysConsidered, deviation.LoggedDaysConsidered,
                    deviation.MagnitudeEnergyKcal),
                cancellationToken);

            return Success(deviation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not flag deviation {DeviationId} as sustained",
                command.DeviationId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    private async Task PublishDetected(Deviation deviation, CancellationToken cancellationToken)
    {
        await mediator.PublishAsync(
            new DeviationDetected(deviation.Id.Value, deviation.PatientId, deviation.WindowRef.Value,
                deviation.Magnitude.RelativeValue, deviation.Direction.Value), cancellationToken);
    }

    private static Result<Deviation, MonitoringError> Success(Deviation deviation)
    {
        return new Result<Deviation, MonitoringError>.Success(deviation);
    }

    private static Result<Deviation, MonitoringError> Failure(MonitoringError error)
    {
        return new Result<Deviation, MonitoringError>.Failure(error);
    }
}
