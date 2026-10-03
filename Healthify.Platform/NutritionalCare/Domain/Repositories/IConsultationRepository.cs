using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Domain.Repositories;

public interface IConsultationRepository : IBaseRepository<Consultation>
{
    Task<Consultation?> FindInProgressByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>RM-1. The consultations in progress of these patients, in one query.</summary>
    Task<IReadOnlyList<Consultation>> ListInProgressByPatientIdsAsync(IReadOnlyCollection<int> patientIds,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsCompletedForPatientAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>NC-2. The consultations of a patient, newest first, optionally of one state only.</summary>
    Task<IEnumerable<Consultation>> ListByPatientIdAsync(int patientId, ConsultationState? state,
        CancellationToken cancellationToken = default);
}
