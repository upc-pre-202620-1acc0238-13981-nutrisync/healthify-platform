using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Domain.Repositories;

public interface IPatientBaselineRepository : IBaseRepository<PatientBaseline>
{
    Task<PatientBaseline?> FindByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>RM-1. The baselines of these patients, in one query.</summary>
    Task<IReadOnlyList<PatientBaseline>> ListByPatientIdsAsync(IReadOnlyCollection<int> patientIds,
        CancellationToken cancellationToken = default);
}
