using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Domain.Repositories;

public interface INutritionalAssessmentRepository : IBaseRepository<NutritionalAssessment>
{
    Task<IEnumerable<NutritionalAssessment>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsForPatientAsync(int patientId, CancellationToken cancellationToken = default);
}
