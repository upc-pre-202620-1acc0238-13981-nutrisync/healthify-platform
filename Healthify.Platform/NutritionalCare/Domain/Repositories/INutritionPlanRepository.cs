using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Domain.Repositories;

public interface INutritionPlanRepository : IBaseRepository<NutritionPlan>
{
    /// <summary>Backs the business rule One Active Version Per Patient (Subflow 3.5).</summary>
    Task<NutritionPlan?> FindActiveByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>RM-1. The active version of each of these patients, in one query.</summary>
    Task<IReadOnlyList<NutritionPlan>> ListActiveByPatientIdsAsync(IReadOnlyCollection<int> patientIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     The whole history, superseded versions included. Read model: Plan Version History. Discarded drafts
    ///     (NC-2) are not history.
    /// </summary>
    Task<IEnumerable<NutritionPlan>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    /// <summary>Highest version number issued for a patient so far, or zero. Discarded drafts (NC-2) do not count.</summary>
    Task<int> GetLatestVersionAsync(int patientId, CancellationToken cancellationToken = default);
}
