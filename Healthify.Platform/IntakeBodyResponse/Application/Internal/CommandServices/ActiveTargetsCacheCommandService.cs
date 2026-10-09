using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;

/// <summary>
///     Subflow 4.1 - the patient copy of the published contract.
/// </summary>
/// <remarks>
///     Business rules: Published Contract Only and Diagnosis And Basis Never Cached (Subflow 4.1).
///     Both are structural. The command carries the contract and nothing else, the aggregate has no
///     field for anything else, and this service never talks to Nutritional Care: it only ever sees
///     what the published event chose to carry.
/// </remarks>
public class ActiveTargetsCacheCommandService(
    IActiveTargetsCacheRepository cacheRepository,
    IUnitOfWork unitOfWork,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    ILogger<ActiveTargetsCacheCommandService> logger,
    IMediator mediator) : IActiveTargetsCacheCommandService
{
    public async Task<Result<ActiveTargetsCache, IntakeError>> Handle(
        RefreshActiveTargetsCacheCommand command, CancellationToken cancellationToken = default)
    {
        if (command.PatientId <= 0) return Failure(IntakeError.PatientWriteOnly);

        // Business rule: Published Contract Only (Subflow 4.1). A payload without a version or
        // without an energy target is not the contract, whatever else it carries.
        if (command.PlanVersion <= 0 || command.EnergyKcal <= 0m)
            return Failure(IntakeError.PublishedContractOnly);

        try
        {
            // Targets reach a patient because somebody is looking after them. If the link is gone,
            // the existing cache is left exactly as it is - Cache Survives Offline applies to a
            // revoked link too - and nothing new is written.
            var careLink = await careRelationshipContextFacade.GetActiveCareLinkByPatientId(
                command.PatientId, cancellationToken);
            if (careLink is null) return Failure(IntakeError.ActiveCareLinkRequired);

            var cache = await cacheRepository.FindByPatientIdAsync(command.PatientId, cancellationToken);

            if (cache is null)
            {
                cache = new ActiveTargetsCache(command);
                await cacheRepository.AddAsync(cache, cancellationToken);
            }
            else
            {
                cache.Refresh(command);
                cacheRepository.Update(cache);
            }

            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new ActiveTargetsCacheRefreshed(cache.PatientId, cache.PlanVersion), cancellationToken);

            return new Result<ActiveTargetsCache, IntakeError>.Success(cache);
        }
        catch (ArgumentException)
        {
            return Failure(IntakeError.PublishedContractOnly);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not refresh the active targets cache of patient {PatientId}",
                command.PatientId);
            return Failure(IntakeError.UnexpectedError);
        }
    }

    private static Result<ActiveTargetsCache, IntakeError> Failure(IntakeError error)
    {
        return new Result<ActiveTargetsCache, IntakeError>.Failure(error);
    }
}
