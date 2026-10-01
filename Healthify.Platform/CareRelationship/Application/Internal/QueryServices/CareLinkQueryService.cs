using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Domain.Repositories;

namespace Healthify.Platform.CareRelationship.Application.Internal.QueryServices;

public class CareLinkQueryService(ICareLinkRepository careLinkRepository) : ICareLinkQueryService
{
    public async Task<CareLink?> Handle(GetCareLinkByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await careLinkRepository.FindByIdAsync(query.CareLinkId, cancellationToken);
    }

    public async Task<CareLink?> Handle(GetActiveCareLinkByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await careLinkRepository.FindActiveByPatientIdAsync(query.PatientId, cancellationToken);
    }

    public async Task<IEnumerable<CareLink>> Handle(GetCareLinksByPractitionerIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await careLinkRepository.ListByPractitionerIdAsync(query.PractitionerId, cancellationToken);
    }
}
