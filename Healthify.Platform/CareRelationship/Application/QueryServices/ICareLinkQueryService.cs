using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;

namespace Healthify.Platform.CareRelationship.Application.QueryServices;

public interface ICareLinkQueryService
{
    Task<CareLink?> Handle(GetCareLinkByIdQuery query, CancellationToken cancellationToken = default);

    Task<CareLink?> Handle(GetActiveCareLinkByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<CareLink>> Handle(GetCareLinksByPractitionerIdQuery query,
        CancellationToken cancellationToken = default);
}
