using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.CareRelationship.Domain.Repositories;

public interface IAiPreferencesRepository : IBaseRepository<AiPreferences>
{
    /// <summary>The preferences of the patient, or null when they never had any.</summary>
    Task<AiPreferences?> FindByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);
}
