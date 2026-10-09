using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Domain.Repositories;

public interface INutritionalDiagnosisRepository : IBaseRepository<NutritionalDiagnosis>
{
    /// <summary>Backs the business rule One Active Diagnosis Per Patient (Subflow 3.2).</summary>
    Task<NutritionalDiagnosis?> FindActiveByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);
}
