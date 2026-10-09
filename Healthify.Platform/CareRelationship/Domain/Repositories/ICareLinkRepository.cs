using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.CareRelationship.Domain.Repositories;

public interface ICareLinkRepository : IBaseRepository<CareLink>
{
    /// <summary>The one link that currently grants access, or null. Backs the Open Host Service.</summary>
    Task<CareLink?> FindActiveByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Backs the business rule One Active Link Per Patient (Subflow 2.2). A link counts as
    ///     occupying the slot from the moment it is established, before consent arrives, so that a
    ///     second invitation cannot be redeemed while the first link is still awaiting consent.
    /// </summary>
    Task<bool> ExistsUnclosedByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     The link occupying the slot, whether or not consent has arrived yet. Distinct from
    ///     <see cref="FindActiveByPatientIdAsync" />, which only returns a link that already grants
    ///     access.
    /// </summary>
    Task<CareLink?> FindUnclosedByPatientIdAsync(int patientId, CancellationToken cancellationToken = default);

    Task<IEnumerable<CareLink>> ListByPractitionerIdAsync(int practitionerId,
        CancellationToken cancellationToken = default);
}
